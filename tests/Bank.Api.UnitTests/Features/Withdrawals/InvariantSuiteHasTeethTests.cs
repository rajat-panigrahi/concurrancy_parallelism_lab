using Bank.Api.UnitTests.Infrastructure;
using Shouldly;

namespace Bank.Api.UnitTests.Features.Withdrawals;

/// <summary>
/// Tests about the tests.
/// </summary>
/// <remarks>
/// A concurrency suite that has never gone red against known-broken code is unproven —
/// it may be asserting something too weak to ever fail. So we point the same invariants
/// at the naive handler and require them to break. If these tests start failing, it
/// means the invariant suite has quietly stopped applying pressure.
/// </remarks>
public class InvariantSuiteHasTeethTests
{
    [Fact]
    public async Task TheMoneyConservationInvariant_ActuallyFails_AgainstTheNaiveHandler()
    {
        var summary = await new LabHarness().RunAsync(
            strategyName: "naive", actors: 5, amountEach: 100m, startingBalance: 100m, forceRace: true);

        summary.MoneyIsConserved.ShouldBeFalse(
            "if the naive handler ever conserves money under a forced race, the harness has stopped forcing the race");
    }

    [Fact]
    public async Task TheNoOverdraftInvariant_ActuallyFails_AgainstTheNaiveHandler()
    {
        // Two actors take ₹100 each from ₹150. One must be refused. The naive handler
        // lets both through, because both read ₹150.
        var summary = await new LabHarness().RunAsync(
            strategyName: "naive", actors: 2, amountEach: 100m, startingBalance: 150m, forceRace: true);

        summary.Outcomes.Count(o => o.ToldItSucceeded).ShouldBe(2);
        summary.ExpectedBalance.ShouldBe(-50m, "two approvals of ₹100 against ₹150 overdraws the account");
        summary.MoneyIsConserved.ShouldBeFalse();
    }

    [Fact]
    public async Task TheSameInvariants_Hold_AgainstTheLockedHandler()
    {
        // The control. Same inputs, same assertions, opposite expectation — which is
        // what makes the pair meaningful rather than either test alone.
        var summary = await new LabHarness().RunAsync(
            strategyName: "lock", actors: 2, amountEach: 100m, startingBalance: 150m, forceRace: true);

        summary.Outcomes.Count(o => o.ToldItSucceeded).ShouldBe(1);
        summary.MoneyIsConserved.ShouldBeTrue();
        summary.OverdrawnBeyondLimit.ShouldBeFalse();
        summary.FinalBalance.ShouldBe(50m);
    }
}
