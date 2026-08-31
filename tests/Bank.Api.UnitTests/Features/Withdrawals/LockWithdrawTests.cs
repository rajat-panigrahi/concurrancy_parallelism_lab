using Bank.Api.UnitTests.Infrastructure;
using Shouldly;

namespace Bank.Api.UnitTests.Features.Withdrawals;

/// <summary>
/// The same scenarios as <see cref="NaiveWithdrawTests"/>, against the locked handler.
/// Read the two files side by side: identical inputs, opposite verdicts.
/// </summary>
public class LockWithdrawTests
{
    [Fact]
    public async Task UnderTheSameForcedRace_MoneyIsConserved()
    {
        var summary = await new LabHarness().RunAsync(
            strategyName: "lock", actors: 5, amountEach: 100m, startingBalance: 100m, forceRace: true);

        summary.MoneyIsConserved.ShouldBeTrue();
        summary.SilentLosers.ShouldBeEmpty();
        summary.Winners.Count.ShouldBe(1, "₹100 can fund exactly one ₹100 withdrawal");
        summary.Rejected.Count.ShouldBe(4);
        summary.FinalBalance.ShouldBe(0m);
    }

    [Fact]
    public async Task TheAccountNeverGoesOverdrawn()
    {
        var summary = await new LabHarness().RunAsync(
            strategyName: "lock", actors: 3, amountEach: 40m, startingBalance: 100m, forceRace: true);

        summary.OverdrawnBeyondLimit.ShouldBeFalse();
        summary.FinalBalance.ShouldBe(20m, "two withdrawals of ₹40 fit in ₹100; the third must be refused");
        summary.Winners.Count.ShouldBe(2);
        summary.Rejected.Count.ShouldBe(1);
        summary.MoneyIsConserved.ShouldBeTrue();
    }

    [Fact]
    public async Task ActorsQueue_SoTheCostOfCorrectnessIsWaiting()
    {
        // Correctness here is not free — it is paid for in latency. Each actor holds
        // the lock through its own think time, so everyone behind it waits.
        var summary = await new LabHarness().RunAsync(
            strategyName: "lock",
            actors: 4,
            amountEach: 10m,
            startingBalance: 1000m,
            forceRace: false,
            thinkTime: TimeSpan.FromMilliseconds(25));

        summary.MoneyIsConserved.ShouldBeTrue();
        summary.Winners.Count.ShouldBe(4);

        // Three of the four had to queue behind someone.
        summary.Outcomes.Count(o => o.WaitedMs > 10).ShouldBeGreaterThanOrEqualTo(3);
        summary.TotalWaitedMs.ShouldBeGreaterThan(50d,
            "serialising four 25 ms critical sections must show up as real waiting");
    }

    [Theory]
    [InlineData(2)]
    [InlineData(8)]
    [InlineData(32)]
    public async Task NoMatterHowManyActors_TheInvariantHolds(int actors)
    {
        // Free-running, no forced interleaving — real scheduling, whatever order the
        // thread pool happens to produce. We assert invariants, never an ordering:
        // asserting "actor-3 wins" would be asserting the scheduler's mood.
        var summary = await new LabHarness().RunAsync(
            strategyName: "lock", actors: actors, amountEach: 10m, startingBalance: 50m, forceRace: false);

        summary.MoneyIsConserved.ShouldBeTrue("no withdrawal may be silently erased");
        summary.OverdrawnBeyondLimit.ShouldBeFalse("the balance must never go below zero");
        summary.FinalBalance.ShouldBe(50m - summary.Winners.Count * 10m);
        summary.Winners.Count.ShouldBe(Math.Min(actors, 5), "₹50 funds exactly five ₹10 withdrawals");
    }
}
