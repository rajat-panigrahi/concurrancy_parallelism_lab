using System.Diagnostics;
using Bank.Api.Features.Interest.CalculateInterest;
using Shouldly;

namespace Bank.Api.UnitTests.Features.Interest;

public class InterestStrategiesTests
{
    private static readonly InterestAccount[] Portfolio = InterestEngine.BuildPortfolio(50_000);

    [Fact]
    public void EveryStrategy_ProducesTheSameTotal()
    {
        // The assertion that matters most. A parallel aggregation that is fast and
        // wrong is worse than a slow one — and a racy accumulator produces a *slightly*
        // wrong total that is easy to miss.
        var expected = InterestStrategies.Sequential(Portfolio);

        InterestStrategies.ParallelWithLock(Portfolio).ShouldBe(expected);
        InterestStrategies.ParallelWithInterlocked(Portfolio).ShouldBe(expected);
        InterestStrategies.ParallelWithLocalSums(Portfolio).ShouldBe(expected);
        InterestStrategies.Plinq(Portfolio).ShouldBe(expected);
        InterestStrategies.ParallelWithConcurrentQueue(Portfolio).ShouldBe(expected);
    }

    [Fact]
    public void RepeatedParallelRuns_AreStable()
    {
        // A racy aggregation often gets the right answer most of the time. Running it
        // repeatedly is how you catch the occasional lost add.
        var expected = InterestStrategies.Sequential(Portfolio);

        for (var i = 0; i < 20; i++)
        {
            InterestStrategies.ParallelWithLocalSums(Portfolio).ShouldBe(expected, $"run {i} disagreed");
            InterestStrategies.Plinq(Portfolio).ShouldBe(expected, $"run {i} disagreed");
        }
    }

    [Fact]
    public void ThreadLocalAggregation_BeatsPerItemSynchronisation()
    {
        // The lesson as an assertion: HOW you aggregate matters more than WHETHER you
        // parallelise. Deliberately loose (2x) so it states the shape of the result
        // rather than pinning a number to this machine's core count.
        var localSums = BestOf(() => InterestStrategies.ParallelWithLocalSums(Portfolio));
        var perItemLock = BestOf(() => InterestStrategies.ParallelWithLock(Portfolio));

        localSums.ShouldBeLessThan(perItemLock / 2,
            "locking once per item makes cores queue instead of compute; it is often slower than not parallelising at all");
    }

    [Fact]
    public void ParallelWithPerItemLock_CanBeSlowerThanSequential()
    {
        // The "I parallelised it and it got slower" result, pinned down. Asserted only
        // as "not meaningfully faster", because on some machines it lands close to
        // sequential rather than well behind it.
        var sequential = BestOf(() => InterestStrategies.Sequential(Portfolio));
        var perItemLock = BestOf(() => InterestStrategies.ParallelWithLock(Portfolio));

        perItemLock.ShouldBeGreaterThan(sequential * 0.9,
            "four cores contending on one lock buy you nothing over one core contending on nothing");
    }

    private static double BestOf(Func<long> work, int repeats = 3)
    {
        work();

        var best = double.MaxValue;

        for (var i = 0; i < repeats; i++)
        {
            var clock = Stopwatch.StartNew();
            work();
            clock.Stop();
            best = Math.Min(best, clock.Elapsed.TotalMilliseconds);
        }

        return best;
    }
}
