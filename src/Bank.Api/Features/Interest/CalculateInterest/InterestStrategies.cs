using System.Collections.Concurrent;

namespace Bank.Api.Features.Interest.CalculateInterest;

/// <summary>
/// The same total, computed six ways. The differences between them are the lesson.
/// </summary>
public static class InterestStrategies
{
    /// <summary>One core, no coordination. The baseline everything else is measured against.</summary>
    public static long Sequential(InterestAccount[] accounts)
    {
        var total = 0L;

        foreach (ref readonly var account in accounts.AsSpan())
        {
            total += InterestEngine.ComputeInterestCents(in account);
        }

        return total;
    }

    /// <summary>
    /// Parallel, aggregating into a shared variable under a <c>lock</c>.
    /// </summary>
    /// <remarks>
    /// Correct, and usually the slowest option here — often slower than sequential.
    /// Every one of the (say) 200,000 items takes and releases a lock, so the threads
    /// spend their time queueing on a single point of contention instead of computing.
    /// This is the classic "I parallelised it and it got slower" result.
    /// </remarks>
    public static long ParallelWithLock(InterestAccount[] accounts)
    {
        var total = 0L;
        var gate = new object();

        Parallel.For(0, accounts.Length, i =>
        {
            var interest = InterestEngine.ComputeInterestCents(in accounts[i]);

            lock (gate)
            {
                total += interest;
            }
        });

        return total;
    }

    /// <summary>
    /// Parallel, aggregating with <see cref="Interlocked"/>.
    /// </summary>
    /// <remarks>
    /// Faster than the lock — a single atomic instruction rather than a kernel-aware
    /// synchronisation primitive — but still one contended memory location touched by
    /// every item, so the cores fight over the same cache line. Better, not good.
    /// </remarks>
    public static long ParallelWithInterlocked(InterestAccount[] accounts)
    {
        var total = 0L;

        Parallel.For(0, accounts.Length, i =>
        {
            var interest = InterestEngine.ComputeInterestCents(in accounts[i]);
            Interlocked.Add(ref total, interest);
        });

        return total;
    }

    /// <summary>
    /// Parallel with thread-local partial sums. <b>This is the right way.</b>
    /// </summary>
    /// <remarks>
    /// Each worker accumulates privately and merges once at the end, so the shared
    /// location is touched a handful of times instead of once per item. The rule
    /// generalises well beyond this example: <b>partition the work, aggregate at the
    /// end</b> — don't synchronise per item.
    /// </remarks>
    public static long ParallelWithLocalSums(InterestAccount[] accounts)
    {
        var total = 0L;

        Parallel.For(
            0,
            accounts.Length,
            localInit: () => 0L,
            body: (i, _, runningTotal) => runningTotal + InterestEngine.ComputeInterestCents(in accounts[i]),
            localFinally: partial => Interlocked.Add(ref total, partial));

        return total;
    }

    /// <summary>PLINQ. Declarative, and it does the partitioning for you.</summary>
    public static long Plinq(InterestAccount[] accounts) =>
        accounts.AsParallel().Sum(account => InterestEngine.ComputeInterestCents(in account));

    /// <summary>
    /// The trap: a concurrent collection is not a substitute for good aggregation.
    /// </summary>
    /// <remarks>
    /// Thread-safe and slow. Every item allocates a queue node and touches a shared
    /// structure, so this usually loses to plain sequential code. Included because
    /// "just use a concurrent collection" is such a common instinct.
    /// </remarks>
    public static long ParallelWithConcurrentQueue(InterestAccount[] accounts)
    {
        var results = new ConcurrentQueue<long>();

        Parallel.For(0, accounts.Length, i =>
            results.Enqueue(InterestEngine.ComputeInterestCents(in accounts[i])));

        var total = 0L;

        while (results.TryDequeue(out var interest))
        {
            total += interest;
        }

        return total;
    }
}
