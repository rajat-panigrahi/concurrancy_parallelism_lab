using Bank.Api.IntegrationTests.Infrastructure;
using Bank.Api.Shared.Contention;
using Shouldly;

namespace Bank.Api.IntegrationTests.Features.Withdrawals;

[Collection(PostgresCollection.Name)]
public class OptimisticWithdrawTests(PostgresFixture fixture)
{
    [Fact]
    public async Task UnderAForcedRace_MoneyIsConserved_AndNobodyLosesSilently()
    {
        var harness = new PostgresLabHarness(fixture);

        var summary = await harness.RunAsync(
            strategyName: "optimistic",
            actors: 5, amountEach: 100m, startingBalance: 100m, forceRace: true);

        summary.MoneyIsConserved.ShouldBeTrue();
        summary.SilentLosers.ShouldBeEmpty("a rejected write is loud; that is the whole point of a concurrency token");
        summary.Winners.Count.ShouldBe(1);
        summary.Rejected.Count.ShouldBe(4);
        summary.FinalBalance.ShouldBe(0m);
        summary.OverdrawnBeyondLimit.ShouldBeFalse();
    }

    [Fact]
    public async Task TheLosersAreToldTheyLost_AsConflicts()
    {
        var harness = new PostgresLabHarness(fixture);

        var summary = await harness.RunAsync(
            strategyName: "optimistic",
            actors: 5, amountEach: 100m, startingBalance: 100m, forceRace: true);

        // The distinguishing property. In the naive handler the losers were told they
        // succeeded. Here they were refused by the database, retried, re-read fresh
        // data and correctly concluded there was no money left.
        summary.Conflicts.ShouldBeGreaterThan(0, "five actors reading the same version must produce conflicts");
        summary.TotalAttempts.ShouldBeGreaterThan(5, "a conflict costs an extra attempt");
    }

    [Fact]
    public async Task NobodyWaits_TheCostIsWastedWorkInstead()
    {
        var harness = new PostgresLabHarness(fixture);

        var summary = await harness.RunAsync(
            strategyName: "optimistic",
            actors: 5, amountEach: 10m, startingBalance: 1000m, forceRace: true);

        // This is the defining trade-off against pessimistic control: optimistic actors
        // never block each other. They pay in repeated work, not in queueing.
        summary.TotalWaitedMs.ShouldBe(0d, "optimistic concurrency takes no locks, so nothing can queue");
        summary.MoneyIsConserved.ShouldBeTrue();
        summary.Winners.Count.ShouldBe(5, "there is plenty of money; everyone should eventually succeed");
    }

    [Fact]
    public async Task RetriesAreBounded_SoHighContentionFailsLoudlyRatherThanSpinningForever()
    {
        var harness = new PostgresLabHarness(fixture);

        // Far more contention than optimistic control is suited to.
        var summary = await harness.RunAsync(
            strategyName: "optimistic",
            actors: 24, amountEach: 1m, startingBalance: 1000m, forceRace: true);

        summary.MoneyIsConserved.ShouldBeTrue("correctness must not degrade even when throughput does");
        summary.Outcomes.ShouldAllBe(o => o.Attempts <= OptimisticWithdrawHandlerLimits.MaxAttempts);

        // The lesson in one assertion: optimistic concurrency does not fail by
        // corrupting data, it fails by doing lots of work and refusing some callers.
        summary.TotalAttempts.ShouldBeGreaterThan(24);
    }

    [Fact]
    public async Task EveryConflictIsRecordedOnTheTimeline()
    {
        var harness = new PostgresLabHarness(fixture);

        var summary = await harness.RunAsync(
            strategyName: "optimistic",
            actors: 4, amountEach: 100m, startingBalance: 100m, forceRace: true);

        var events = harness.Events(summary.RunId);

        events.ShouldContain(e => e.Phase == ContentionPhase.Conflict);
        events.Where(e => e.Phase == ContentionPhase.Conflict)
            .ShouldAllBe(e => e.Note!.Contains("xmin is no longer"));
    }
}

/// <summary>Mirrors the handler's retry cap so the test reads without a magic number.</summary>
internal static class OptimisticWithdrawHandlerLimits
{
    public const int MaxAttempts = Bank.Api.Features.Withdrawals.OptimisticWithdraw.OptimisticWithdrawHandler.MaxAttempts;
}
