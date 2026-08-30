using Bank.Api.Shared.Contention;
using Bank.Api.UnitTests.Infrastructure;
using Shouldly;

namespace Bank.Api.UnitTests.Features.Withdrawals;

/// <summary>
/// These tests assert that the bug <i>happens</i>. They are not describing behaviour we
/// want — they are pinning down behaviour we are about to fix, so the fix has something
/// to be measured against.
/// </summary>
public class NaiveWithdrawTests
{
    [Fact]
    public async Task WhenEveryActorReadsBeforeAnyWrites_TheBankLosesMoney()
    {
        // Five actors, ₹100 each, against an account holding ₹100.
        // Exactly one should succeed. Watch what happens instead.
        var summary = await new LabHarness().RunAsync(
            strategyName: "naive",
            actors: 5,
            amountEach: 100m,
            startingBalance: 100m,
            forceRace: true);

        summary.Outcomes.Count(o => o.ToldItSucceeded)
            .ShouldBe(5, "every actor read ₹100 and every actor believed it could afford ₹100");

        summary.FinalBalance
            .ShouldBe(0m, "each actor blindly wrote 100 - 100, so the last write wins and reads zero");

        summary.ExpectedBalance
            .ShouldBe(-400m, "five approvals of ₹100 against ₹100 should have left the books at -400");

        summary.Discrepancy
            .ShouldBe(-400m, "the books say ₹100 left the bank; ₹500 actually did");

        summary.MoneyIsConserved.ShouldBeFalse();
    }

    [Fact]
    public async Task TheLosersAreSilent_TheyAreToldTheySucceeded()
    {
        var summary = await new LabHarness().RunAsync(
            strategyName: "naive", actors: 5, amountEach: 100m, startingBalance: 100m, forceRace: true);

        // This is the property that makes the bug dangerous. Nothing threw. Nothing
        // logged an error. Four customers walked away with money the bank never
        // recorded, and every one of them got a success response.
        summary.SilentLosers.Count.ShouldBe(4);
        summary.SilentLosers.ShouldAllBe(o => o.ToldItSucceeded);
        summary.SilentLosers.ShouldAllBe(o => !o.ActuallyLanded);
        summary.SilentLosers.ShouldAllBe(o => o.FinalPhase == ContentionPhase.LostUpdate);

        summary.Winners.Count.ShouldBe(1, "with blind overwrites only the last writer's deduction survives");
    }

    [Fact]
    public async Task TheRaceIsDeterministic_SoTheTestIsNotFlaky()
    {
        // The same forced interleaving 25 times. A test that only reproduces a race
        // sometimes is worse than no test: it fails in CI for reasons nobody can
        // reproduce, and gets muted. Forcing the interleaving removes the luck.
        for (var i = 0; i < 25; i++)
        {
            var summary = await new LabHarness().RunAsync(
                strategyName: "naive", actors: 5, amountEach: 100m, startingBalance: 100m, forceRace: true);

            summary.SilentLosers.Count.ShouldBe(4, $"run {i} should behave exactly like every other run");
        }
    }

    [Fact]
    public async Task TheAccountGoesOverdrawn_BecauseTheGuardReadAStaleBalance()
    {
        // Actors withdraw ₹40 each from ₹100. Two should succeed, the third refused.
        var summary = await new LabHarness().RunAsync(
            strategyName: "naive", actors: 3, amountEach: 40m, startingBalance: 100m, forceRace: true);

        summary.Outcomes.Count(o => o.ToldItSucceeded).ShouldBe(3);
        summary.ExpectedBalance.ShouldBe(-20m);

        // The `if (balance < amount)` check is not wrong. It is checking a number that
        // was true when it was read and false by the time it was used.
        summary.FinalBalance.ShouldBe(60m, "every actor computed 100 - 40 from the same stale read");
        summary.MoneyIsConserved.ShouldBeFalse();
    }

    [Fact]
    public async Task WithoutContention_TheNaiveVersionIsPerfectlyCorrect()
    {
        // Worth stating plainly: this code is not "bad code". With one actor it is
        // correct, readable and fast. It only breaks when someone else shows up —
        // which is why the bug survives code review and reaches production.
        var summary = await new LabHarness().RunAsync(
            strategyName: "naive", actors: 1, amountEach: 100m, startingBalance: 100m, forceRace: false);

        summary.MoneyIsConserved.ShouldBeTrue();
        summary.FinalBalance.ShouldBe(0m);
        summary.SilentLosers.ShouldBeEmpty();
    }
}
