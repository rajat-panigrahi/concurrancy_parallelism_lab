using BenchmarkDotNet.Attributes;

namespace Bank.Benchmarks.Benchmarks;

/// <summary>
/// What each way of protecting a single counter actually costs, uncontended.
/// </summary>
/// <remarks>
/// <para>These are single-threaded on purpose. This measures the <i>base cost of the
/// primitive</i> — the price you pay even when nobody is competing with you. Contended
/// costs are far higher and far more variable, and they are what
/// <see cref="AggregationBenchmarks"/> shows.</para>
/// <para>The ordering is the takeaway: plain increment is free, <c>Interlocked</c> is a
/// single atomic instruction, <c>lock</c> is cheap uncontended but not free, and
/// <c>SemaphoreSlim</c> is much more expensive because it is built for async waiting
/// rather than raw speed.</para>
/// </remarks>
[MemoryDiagnoser]
public class SynchronizationBenchmarks
{
    private const int Iterations = 1_000_000;

    private readonly object _lockObject = new();
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly ReaderWriterLockSlim _readerWriterLock = new();

    private long _counter;

    [Benchmark(Baseline = true, Description = "No synchronisation (unsafe)")]
    public long Unsynchronised()
    {
        _counter = 0;

        for (var i = 0; i < Iterations; i++)
        {
            _counter++;
        }

        return _counter;
    }

    [Benchmark(Description = "Interlocked.Increment")]
    public long Interlocked_()
    {
        _counter = 0;

        for (var i = 0; i < Iterations; i++)
        {
            Interlocked.Increment(ref _counter);
        }

        return _counter;
    }

    [Benchmark(Description = "lock")]
    public long Lock()
    {
        _counter = 0;

        for (var i = 0; i < Iterations; i++)
        {
            lock (_lockObject)
            {
                _counter++;
            }
        }

        return _counter;
    }

    [Benchmark(Description = "ReaderWriterLockSlim (write)")]
    public long ReaderWriterLock()
    {
        _counter = 0;

        for (var i = 0; i < Iterations; i++)
        {
            _readerWriterLock.EnterWriteLock();

            try
            {
                _counter++;
            }
            finally
            {
                _readerWriterLock.ExitWriteLock();
            }
        }

        return _counter;
    }

    [Benchmark(Description = "SemaphoreSlim.Wait")]
    public long Semaphore()
    {
        _counter = 0;

        for (var i = 0; i < Iterations; i++)
        {
            _semaphore.Wait();

            try
            {
                _counter++;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        return _counter;
    }
}
