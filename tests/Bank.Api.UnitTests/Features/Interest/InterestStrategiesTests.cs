using Bank.Api.Features.Interest.CalculateInterest;
using Bank.Api.UnitTests.Infrastructure;
using Shouldly;

namespace Bank.Api.UnitTests.Features.Interest;

[Collection(TimingSensitiveCollection.Name)]
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

    /// <summary>
    /// Performance claims deliberately do NOT live here.
    /// </summary>
    /// <remarks>
    /// <para>"Thread-local aggregation is faster than locking per item" is a real result —
    /// measured at roughly 40x on a quiet 4-core machine, and reported properly in
    /// <c>docs/benchmarks/</c>. It is not asserted as a test, because a wall-clock ratio
    /// on shared hardware is a flake waiting to happen: this exact assertion failed once
    /// under CPU load during development, which is precisely the outcome ADR-0016 says a
    /// concurrency suite must not tolerate.</para>
    /// <para>Tests assert correctness; benchmarks assert speed. That split is the subject
    /// of lesson 10, so the test suite had better honour it.</para>
    /// </remarks>
    [Fact]
    public void PerformanceClaimsBelongInBenchmarks_NotHere()
    {
        // What CAN be asserted deterministically about the fast path: it is correct.
        InterestStrategies.ParallelWithLocalSums(Portfolio)
            .ShouldBe(InterestStrategies.Sequential(Portfolio));
    }
}
