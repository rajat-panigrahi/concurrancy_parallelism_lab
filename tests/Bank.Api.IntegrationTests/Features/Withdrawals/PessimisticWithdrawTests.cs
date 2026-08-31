using Bank.Api.IntegrationTests.Infrastructure;
using Bank.Api.Shared.Contention;
using Shouldly;

namespace Bank.Api.IntegrationTests.Features.Withdrawals;

[Collection(PostgresCollection.Name)]
public class PessimisticWithdrawTests(PostgresFixture fixture)
{
    [Fact]
    public async Task UnderAForcedRace_MoneyIsConserved()
    {
        var summary = await new PostgresLabHarness(fixture).RunAsync(
            strategyName: "pessimistic",
            actors: 5, amountEach: 100m, startingBalance: 100m, forceRace: true);

        summary.MoneyIsConserved.ShouldBeTrue();
        summary.SilentLosers.ShouldBeEmpty();
        summary.Winners.Count.ShouldBe(1);
        summary.Rejected.Count.ShouldBe(4);
        summary.FinalBalance.ShouldBe(0m);
    }

    [Fact]
    public async Task NobodyDoesWastedWork_TheCostIsWaitingInstead()
    {
        var summary = await new PostgresLabHarness(fixture).RunAsync(
            strategyName: "pessimistic",
            actors: 5, amountEach: 10m, startingBalance: 1000m, forceRace: true,
            thinkTime: TimeSpan.FromMilliseconds(20));

        // The mirror image of the optimistic test. Nobody retried, because SELECT ...
        // FOR UPDATE made them queue rather than collide.
        summary.Conflicts.ShouldBe(0, "a row lock prevents the conflict rather than detecting it");
        summary.TotalAttempts.ShouldBe(5, "one attempt each — no work is ever thrown away");

        // And here is what it cost.
        summary.TotalWaitedMs.ShouldBeGreaterThan(20d,
            "serialising five 20 ms critical sections has to show up as waiting somewhere");
    }

    [Fact]
    public async Task TheRowLockActuallySerialises_TheWaitsGrowWithTheQueue()
    {
        var harness = new PostgresLabHarness(fixture);

        var summary = await harness.RunAsync(
            strategyName: "pessimistic",
            actors: 4, amountEach: 10m, startingBalance: 1000m, forceRace: true,
            thinkTime: TimeSpan.FromMilliseconds(40));

        var waits = harness.Events(summary.RunId)
            .Where(e => e.Phase == ContentionPhase.LockAcquired && e.WaitedMs.HasValue)
            .Select(e => e.WaitedMs!.Value)
            .OrderBy(ms => ms)
            .ToArray();

        waits.Length.ShouldBe(4);

        // The last actor in the queue waits through everybody ahead of it. That is
        // pessimistic locking's cost curve, measured rather than asserted.
        waits[^1].ShouldBeGreaterThan(waits[0],
            "the last actor to get the lock must have waited longer than the first");
        waits[^1].ShouldBeGreaterThan(80d, "three 40 ms critical sections stack up ahead of the last actor");
    }

    [Fact]
    public async Task TheAccountNeverGoesOverdrawn()
    {
        var summary = await new PostgresLabHarness(fixture).RunAsync(
            strategyName: "pessimistic",
            actors: 6, amountEach: 30m, startingBalance: 100m, forceRace: true);

        summary.OverdrawnBeyondLimit.ShouldBeFalse();
        summary.FinalBalance.ShouldBe(10m, "three withdrawals of ₹30 fit in ₹100; the rest must be refused");
        summary.Winners.Count.ShouldBe(3);
        summary.MoneyIsConserved.ShouldBeTrue();
    }
}
