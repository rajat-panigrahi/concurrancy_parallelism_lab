using System.Diagnostics;
using Bank.Api.Shared.Endpoints;

namespace Bank.Api.Features.Fraud.RunFraudChecks;

public sealed record FraudRequest
{
    public int Checks { get; init; } = 10;
    public int LatencyMs { get; init; } = 200;

    /// <summary>Cap for the throttled strategy — how many calls the downstream will tolerate.</summary>
    public int MaxConcurrency { get; init; } = 4;
}

public sealed record FraudTiming
{
    public required string Name { get; init; }
    public required double DurationMs { get; init; }
    public required int DistinctThreads { get; init; }
    public required string Note { get; init; }
}

public sealed record FraudResponse
{
    public required int Checks { get; init; }
    public required int LatencyMs { get; init; }
    public required int ProcessorCount { get; init; }
    public required IReadOnlyList<FraudTiming> Timings { get; init; }
    public required string Verdict { get; init; }
}

/// <summary>
/// I/O-bound concurrency. Nothing here is CPU work, so parallelism has nothing to
/// speed up — but overlapping the waiting changes everything.
/// </summary>
public sealed class RunFraudChecksHandler(FraudCheckService fraud)
{
    public async Task<FraudResponse> HandleAsync(FraudRequest request, CancellationToken cancellationToken)
    {
        var checks = Math.Clamp(request.Checks, 1, 200);
        var latency = TimeSpan.FromMilliseconds(Math.Clamp(request.LatencyMs, 1, 2000));
        var limit = Math.Clamp(request.MaxConcurrency, 1, checks);

        var timings = new List<FraudTiming>
        {
            await TimeAsync("sequential await", ct => SequentialAsync(checks, latency, ct),
                $"One at a time. {checks} x {latency.TotalMilliseconds:0} ms, added up. Correct, and needlessly slow.",
                cancellationToken),

            await TimeAsync("Task.WhenAll", ct => WhenAllAsync(checks, latency, ct),
                "All in flight at once. Total time is now the SLOWEST call, not the sum — and note how few threads it took.",
                cancellationToken),

            await TimeAsync($"throttled to {limit}", ct => ThrottledAsync(checks, latency, limit, ct),
                $"SemaphoreSlim caps concurrency at {limit}. Slower than WhenAll on purpose: unbounded fan-out is how you DDoS your own dependency.",
                cancellationToken),

            await TimeAsync($"Parallel.ForEachAsync ({limit})", ct => ParallelForEachAsync(checks, latency, limit, ct),
                "The built-in throttled fan-out. Same idea as the semaphore, less code.",
                cancellationToken),
        };

        var sequential = timings[0].DurationMs;
        var whenAll = timings[1].DurationMs;

        return new FraudResponse
        {
            Checks = checks,
            LatencyMs = (int)latency.TotalMilliseconds,
            ProcessorCount = Environment.ProcessorCount,
            Timings = timings,
            Verdict =
                $"Sequential took {sequential:0} ms; Task.WhenAll took {whenAll:0} ms — "
                + $"{sequential / whenAll:0.0}x faster on {Environment.ProcessorCount} cores, "
                + $"using {timings[1].DistinctThreads} thread(s) for {checks} calls. "
                + "No extra cores were involved: this is concurrency, not parallelism. The CPU was idle either way; "
                + "the only change is that the waiting now overlaps.",
        };
    }

    private static async Task<FraudTiming> TimeAsync(
        string name,
        Func<CancellationToken, Task<IReadOnlyList<FraudVerdict>>> run,
        string note,
        CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        var verdicts = await run(cancellationToken);
        clock.Stop();

        return new FraudTiming
        {
            Name = name,
            DurationMs = Math.Round(clock.Elapsed.TotalMilliseconds, 1),
            DistinctThreads = verdicts.Select(v => v.ThreadId).Distinct().Count(),
            Note = note,
        };
    }

    private async Task<IReadOnlyList<FraudVerdict>> SequentialAsync(
        int checks, TimeSpan latency, CancellationToken cancellationToken)
    {
        var verdicts = new List<FraudVerdict>(checks);

        // `await` inside a loop is the classic accidental serialisation. Each iteration
        // waits for the previous one, so ten independent calls take ten times as long
        // for no reason at all.
        for (var i = 0; i < checks; i++)
        {
            verdicts.Add(await fraud.CheckAsync(i, latency, cancellationToken));
        }

        return verdicts;
    }

    private async Task<IReadOnlyList<FraudVerdict>> WhenAllAsync(
        int checks, TimeSpan latency, CancellationToken cancellationToken)
    {
        // Start them all, THEN await. Calling .Select(...) does not await anything —
        // the tasks are already running by the time WhenAll sees them.
        var tasks = Enumerable.Range(0, checks)
            .Select(i => fraud.CheckAsync(i, latency, cancellationToken));

        return await Task.WhenAll(tasks);
    }

    private async Task<IReadOnlyList<FraudVerdict>> ThrottledAsync(
        int checks, TimeSpan latency, int limit, CancellationToken cancellationToken)
    {
        using var throttle = new SemaphoreSlim(limit, limit);

        var tasks = Enumerable.Range(0, checks).Select(async i =>
        {
            await throttle.WaitAsync(cancellationToken);

            try
            {
                return await fraud.CheckAsync(i, latency, cancellationToken);
            }
            finally
            {
                // In a finally, always. An exception that skipped the release would
                // permanently shrink the pool until nothing could run at all.
                throttle.Release();
            }
        });

        return await Task.WhenAll(tasks);
    }

    private async Task<IReadOnlyList<FraudVerdict>> ParallelForEachAsync(
        int checks, TimeSpan latency, int limit, CancellationToken cancellationToken)
    {
        var verdicts = new FraudVerdict[checks];

        // Parallel.ForEachAsync (.NET 6+) is the async-aware sibling of Parallel.ForEach.
        // Using Parallel.ForEach with async work is a well-known bug: it takes
        // Action<T>, so `async void` lambdas run fire-and-forget and it returns before
        // the work finishes.
        await Parallel.ForEachAsync(
            Enumerable.Range(0, checks),
            new ParallelOptions { MaxDegreeOfParallelism = limit, CancellationToken = cancellationToken },
            async (i, ct) => verdicts[i] = await fraud.CheckAsync(i, latency, ct));

        return verdicts;
    }
}

public sealed class RunFraudChecksEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/lab/fraud", async (
                FraudRequest request,
                RunFraudChecksHandler handler,
                CancellationToken cancellationToken) =>
                Results.Ok(await handler.HandleAsync(request, cancellationToken)))
            .WithName("RunFraudChecks")
            .WithTags("Lab")
            .WithSummary("I/O-bound concurrency: sequential vs Task.WhenAll vs throttled fan-out.");
    }
}
