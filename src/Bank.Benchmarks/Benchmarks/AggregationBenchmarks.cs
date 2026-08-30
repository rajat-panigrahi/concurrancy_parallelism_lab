using Bank.Api.Features.Interest.CalculateInterest;
using BenchmarkDotNet.Attributes;

namespace Bank.Benchmarks.Benchmarks;

/// <summary>
/// The same total, six ways. Answers "does parallelising this help?" — and the more
/// useful question, "does how I aggregate matter more than whether I parallelise?"
/// </summary>
[MemoryDiagnoser]
public class AggregationBenchmarks
{
    private InterestAccount[] _accounts = [];

    [Params(100_000)]
    public int AccountCount { get; set; }

    [GlobalSetup]
    public void Setup() => _accounts = InterestEngine.BuildPortfolio(AccountCount);

    [Benchmark(Baseline = true, Description = "Sequential (one core)")]
    public long Sequential() => InterestStrategies.Sequential(_accounts);

    [Benchmark(Description = "Parallel + lock per item")]
    public long ParallelWithLock() => InterestStrategies.ParallelWithLock(_accounts);

    [Benchmark(Description = "Parallel + Interlocked per item")]
    public long ParallelWithInterlocked() => InterestStrategies.ParallelWithInterlocked(_accounts);

    [Benchmark(Description = "Parallel + thread-local sums")]
    public long ParallelWithLocalSums() => InterestStrategies.ParallelWithLocalSums(_accounts);

    [Benchmark(Description = "PLINQ")]
    public long Plinq() => InterestStrategies.Plinq(_accounts);

    [Benchmark(Description = "Parallel + ConcurrentQueue")]
    public long ParallelWithConcurrentQueue() => InterestStrategies.ParallelWithConcurrentQueue(_accounts);
}
