using System.Collections.Concurrent;
using Bank.Api.Shared.Contention;

namespace Bank.Api.Features.Lab;

/// <summary>
/// Keeps the last few completed runs so the UI can fetch one after the fact.
/// Bounded for the same reason the recorder is (ADR-0013): a lab left running under
/// load must not become a memory leak.
/// </summary>
public sealed class LabRunStore
{
    public const int MaxRuns = 50;

    private readonly ConcurrentDictionary<Guid, RunSummary> _summaries = new();
    private readonly ConcurrentQueue<Guid> _order = new();

    public void Save(RunSummary summary)
    {
        _summaries[summary.RunId] = summary;
        _order.Enqueue(summary.RunId);

        while (_order.Count > MaxRuns && _order.TryDequeue(out var oldest))
        {
            _summaries.TryRemove(oldest, out _);
        }
    }

    public RunSummary? Find(Guid runId) =>
        _summaries.TryGetValue(runId, out var summary) ? summary : null;

    public IReadOnlyList<RunSummary> Recent() =>
        _order.Reverse().Select(Find).OfType<RunSummary>().ToArray();
}
