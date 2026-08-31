using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;

namespace Bank.Api.Shared.Contention;

/// <summary>
/// In-memory, bounded recorder. Keeps the last <see cref="MaxEventsPerRun"/> events for
/// each of the last <see cref="MaxRuns"/> runs, and publishes every event to a channel
/// that the SignalR broadcaster drains.
/// </summary>
/// <remarks>
/// <para>Two properties matter more than anything else here: <b>never block the caller</b>
/// and <b>never grow without bound</b>. Recording happens inside the code we are
/// measuring, so a lock held here would change the interleaving we are trying to show,
/// and an unbounded buffer would turn a load test into an OutOfMemoryException.</para>
/// <para>Writing to a <see cref="Channel{T}"/> rather than pushing to SignalR inline is
/// the producer/consumer split: the handler's thread does an enqueue and moves on,
/// while a background reader does the slow network work. See ADR-0013.</para>
/// </remarks>
public sealed class ContentionRecorder : IContentionRecorder
{
    public const int MaxEventsPerRun = 20_000;
    public const int MaxRuns = 50;

    private sealed class RunLog
    {
        public Stopwatch Clock { get; } = Stopwatch.StartNew();
        public ConcurrentQueue<ContentionEvent> Events { get; } = new();
        public long Sequence;
        public int Count;
    }

    private readonly ConcurrentDictionary<Guid, RunLog> _runs = new();
    private readonly ConcurrentQueue<Guid> _runOrder = new();

    private readonly Channel<ContentionEvent> _live = Channel.CreateUnbounded<ContentionEvent>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    /// <summary>Live feed for the SignalR broadcaster.</summary>
    public ChannelReader<ContentionEvent> Live => _live.Reader;

    public void BeginRun(Guid runId)
    {
        _runs[runId] = new RunLog();
        _runOrder.Enqueue(runId);
        EvictOldRuns();
    }

    public double ElapsedMs(Guid runId) =>
        _runs.TryGetValue(runId, out var log) ? log.Clock.Elapsed.TotalMilliseconds : 0d;

    public long NextSequence(Guid runId) =>
        _runs.TryGetValue(runId, out var log) ? Interlocked.Increment(ref log.Sequence) : 0L;

    public void Record(ContentionEvent contentionEvent)
    {
        if (!_runs.TryGetValue(contentionEvent.RunId, out var log))
        {
            return;
        }

        // Interlocked rather than lock: this is a counter, and a counter never needs
        // mutual exclusion, only atomicity. Cheaper, and it cannot deadlock.
        if (Interlocked.Increment(ref log.Count) > MaxEventsPerRun)
        {
            log.Events.TryDequeue(out _);
            Interlocked.Decrement(ref log.Count);
        }

        log.Events.Enqueue(contentionEvent);

        // An unbounded channel writer always succeeds, so this cannot block the caller.
        _live.Writer.TryWrite(contentionEvent);
    }

    public IReadOnlyList<ContentionEvent> EventsFor(Guid runId) =>
        _runs.TryGetValue(runId, out var log)
            ? log.Events.OrderBy(e => e.Sequence).ToArray()
            : [];

    private void EvictOldRuns()
    {
        while (_runOrder.Count > MaxRuns && _runOrder.TryDequeue(out var oldest))
        {
            _runs.TryRemove(oldest, out _);
        }
    }
}
