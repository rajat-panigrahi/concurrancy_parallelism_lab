using System.Diagnostics;
using Bank.Api.Shared.Endpoints;

namespace Bank.Api.Features.Interest.CalculateInterest;

public sealed record InterestRequest
{
    public int AccountCount { get; init; } = 200_000;

    /// <summary>Repeats each strategy and keeps the best time, to blunt JIT and scheduling noise.</summary>
    public int Repeats { get; init; } = 3;
}

public sealed record StrategyTiming
{
    public required string Name { get; init; }
    public required string Aggregation { get; init; }
    public required double BestMs { get; init; }
    public required long Total { get; init; }
    public required double SpeedupVsSequential { get; init; }
    public required string Note { get; init; }
}

public sealed record InterestResponse
{
    public required int AccountCount { get; init; }
    public required int ProcessorCount { get; init; }
    public required IReadOnlyList<StrategyTiming> Timings { get; init; }
    public required string Verdict { get; init; }
}

public sealed class CalculateInterestHandler
{
    public InterestResponse Handle(InterestRequest request)
    {
        var count = Math.Clamp(request.AccountCount, 1_000, 2_000_000);
        var repeats = Math.Clamp(request.Repeats, 1, 10);
        var accounts = InterestEngine.BuildPortfolio(count);

        var candidates = new (string Name, string Aggregation, Func<InterestAccount[], long> Run, string Note)[]
        {
            ("sequential", "none",
                InterestStrategies.Sequential,
                "One core. The baseline."),
            ("parallel + lock", "lock per item",
                InterestStrategies.ParallelWithLock,
                "Every item takes a lock, so the cores queue instead of computing. Often slower than sequential."),
            ("parallel + Interlocked", "atomic per item",
                InterestStrategies.ParallelWithInterlocked,
                "One atomic instruction beats a lock, but every core still fights over the same cache line."),
            ("parallel + local sums", "thread-local, merged once",
                InterestStrategies.ParallelWithLocalSums,
                "Each worker sums privately and merges at the end. This is the right way."),
            ("PLINQ", "thread-local, merged once",
                InterestStrategies.Plinq,
                "Declarative, and it partitions for you. Usually within noise of hand-written local sums."),
            ("parallel + ConcurrentQueue", "shared collection",
                InterestStrategies.ParallelWithConcurrentQueue,
                "Thread-safe and slow. A concurrent collection is not an aggregation strategy."),
        };

        var results = new List<(string Name, string Aggregation, double BestMs, long Total, string Note)>();

        foreach (var candidate in candidates)
        {
            // Warm up so the JIT has compiled the delegate before we time it.
            candidate.Run(accounts);

            var best = double.MaxValue;
            var total = 0L;

            for (var i = 0; i < repeats; i++)
            {
                var clock = Stopwatch.StartNew();
                total = candidate.Run(accounts);
                clock.Stop();
                best = Math.Min(best, clock.Elapsed.TotalMilliseconds);
            }

            results.Add((candidate.Name, candidate.Aggregation, best, total, candidate.Note));
        }

        var sequentialMs = results[0].BestMs;

        var timings = results.Select(r => new StrategyTiming
        {
            Name = r.Name,
            Aggregation = r.Aggregation,
            BestMs = Math.Round(r.BestMs, 2),
            Total = r.Total,
            SpeedupVsSequential = Math.Round(sequentialMs / r.BestMs, 2),
            Note = r.Note,
        }).ToArray();

        var bestParallel = timings.Skip(1).MaxBy(t => t.SpeedupVsSequential)!;

        // Every strategy must agree on the answer. If they don't, the aggregation is
        // racy — and a "fast" wrong answer is the worst outcome of the three.
        var allAgree = timings.Select(t => t.Total).Distinct().Count() == 1;

        return new InterestResponse
        {
            AccountCount = count,
            ProcessorCount = Environment.ProcessorCount,
            Timings = timings,
            Verdict = allAgree
                ? $"All six agree on {timings[0].Total} cents. Best was '{bestParallel.Name}' at "
                  + $"{bestParallel.SpeedupVsSequential}x sequential on {Environment.ProcessorCount} cores — "
                  + "note that how you aggregate matters more than whether you parallelise."
                : "The strategies disagree on the total, which means an aggregation is racy.",
        };
    }
}

public sealed class CalculateInterestEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/lab/interest", (InterestRequest request, CalculateInterestHandler handler) =>
                Results.Ok(handler.Handle(request)))
            .WithName("CalculateInterest")
            .WithTags("Lab")
            .WithSummary("CPU-bound parallelism: the same total computed six ways.");
    }
}
