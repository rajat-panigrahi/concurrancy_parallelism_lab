using BenchmarkDotNet.Attributes;

namespace Bank.Benchmarks.Benchmarks;

/// <summary>
/// What <c>async</c> costs when there is nothing to wait for.
/// </summary>
/// <remarks>
/// Worth measuring because "async is slow" and "async is free" are both common claims
/// and both wrong. The state machine has a real cost on a hot path that always
/// completes synchronously — which is exactly the case <see cref="ValueTask{T}"/> exists
/// for. On anything that does actual I/O this overhead is noise.
/// </remarks>
[MemoryDiagnoser]
public class AsyncOverheadBenchmarks
{
    private const int Iterations = 10_000;

    [Benchmark(Baseline = true, Description = "Synchronous call")]
    public long Synchronous()
    {
        var total = 0L;

        for (var i = 0; i < Iterations; i++)
        {
            total += Compute(i);
        }

        return total;
    }

    [Benchmark(Description = "async Task, always completed")]
    public async Task<long> AsyncTask()
    {
        var total = 0L;

        for (var i = 0; i < Iterations; i++)
        {
            total += await ComputeTaskAsync(i);
        }

        return total;
    }

    [Benchmark(Description = "async ValueTask, always completed")]
    public async ValueTask<long> AsyncValueTask()
    {
        var total = 0L;

        for (var i = 0; i < Iterations; i++)
        {
            total += await ComputeValueTaskAsync(i);
        }

        return total;
    }

    [Benchmark(Description = "Task.Run wrapping sync work (anti-pattern)")]
    public async Task<long> TaskRunWrapper()
    {
        var total = 0L;

        for (var i = 0; i < Iterations; i++)
        {
            // Offloading trivial synchronous work to the thread pool. Pure overhead:
            // a queue, a thread hand-off and a context switch, to do a subtraction.
            total += await Task.Run(() => Compute(i));
        }

        return total;
    }

    private static long Compute(int i) => i * 2L + 1L;

    private static Task<long> ComputeTaskAsync(int i) => Task.FromResult(Compute(i));

    private static ValueTask<long> ComputeValueTaskAsync(int i) => new(Compute(i));
}
