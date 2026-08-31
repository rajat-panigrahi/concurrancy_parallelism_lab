using Bank.Api.Features.Withdrawals.DistributedLockWithdraw;
using Bank.Api.IntegrationTests.Infrastructure;
using Bank.Api.Shared.Contention;
using Bank.Api.Shared.Interleaving;
using Bank.Api.Shared.Locking;
using Bank.Api.Shared.Persistence;
using Bank.Api.Shared.Withdrawals;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Bank.Api.IntegrationTests.Features.Withdrawals;

[Collection(PostgresCollection.Name)]
public class DistributedLockWithdrawTests(PostgresFixture fixture)
{
    [Fact]
    public async Task TheAdvisoryLock_SerialisesConcurrentWithdrawals()
    {
        var (summary, _) = await RunAsync(actors: 5, amountEach: 100m, startingBalance: 100m);

        summary.MoneyIsConserved.ShouldBeTrue();
        summary.SilentLosers.ShouldBeEmpty();
        summary.Winners.Count.ShouldBe(1);
        summary.FinalBalance.ShouldBe(0m);
    }

    [Fact]
    public async Task ActorsQueue_WhichIsWhatMakesItCorrectAcrossInstances()
    {
        var (summary, _) = await RunAsync(actors: 4, amountEach: 10m, startingBalance: 1000m);

        summary.MoneyIsConserved.ShouldBeTrue();
        summary.Winners.Count.ShouldBe(4);

        // The waiting is the evidence the lock is real. A lock nobody ever waits on has
        // not been shown to lock anything.
        summary.TotalWaitedMs.ShouldBeGreaterThan(0d);
    }

    [Fact]
    public void TheLockKey_IsStableForAnAccount_AndUsuallyDistinctBetweenAccounts()
    {
        var id = Guid.NewGuid();

        PostgresAdvisoryAccountLock.ToLockKey(id)
            .ShouldBe(PostgresAdvisoryAccountLock.ToLockKey(id), "the same account must always map to the same lock");

        // Guid -> 64 bits is a hash, so collisions are possible. A collision costs
        // unnecessary blocking, never correctness. This asserts the rate is negligible
        // rather than pretending it is zero.
        var keys = Enumerable.Range(0, 5_000)
            .Select(_ => PostgresAdvisoryAccountLock.ToLockKey(Guid.NewGuid()))
            .ToHashSet();

        keys.Count.ShouldBeGreaterThan(4_990);
    }

    [Fact]
    public async Task TheLockIsReleased_SoLaterCallersAreNotBlockedForever()
    {
        var accountLock = new PostgresAdvisoryAccountLock(new HarnessFactory(fixture.ConnectionString));
        var accountId = Guid.NewGuid();

        await using (await accountLock.AcquireAsync(accountId))
        {
            // held
        }

        // If dispose failed to unlock, this would hang until the test timed out.
        var second = await accountLock.AcquireAsync(accountId).WaitAsync(TimeSpan.FromSeconds(10));
        await second.DisposeAsync();
    }

    private async Task<(RunSummary Summary, Guid AccountId)> RunAsync(
        int actors, decimal amountEach, decimal startingBalance)
    {
        var recorder = new ContentionRecorder();
        var gate = new LabInterleaveGate();
        var factory = new HarnessFactory(fixture.ConnectionString);

        var handler = new DistributedLockWithdrawHandler(
            factory, new PostgresAdvisoryAccountLock(factory), recorder, gate);

        var runId = Guid.NewGuid();
        var (accountId, startingVersion) = await SeedAsync(runId, startingBalance);

        recorder.BeginRun(runId);
        using var forcing = gate.ForceRace(runId, actors);

        var results = await Task.WhenAll(Enumerable.Range(1, actors).Select(async i =>
        {
            var command = new WithdrawCommand
            {
                RunId = runId, ActorId = $"actor-{i}", AccountId = accountId, Amount = amountEach,
            };

            var result = await handler.WithdrawAsync(command);
            return (command.ActorId, result.Approved);
        }));

        await using var db = fixture.NewDbContext();
        var finalBalance = await db.Accounts.AsNoTracking()
            .Where(a => a.Id == accountId).Select(a => a.Balance).SingleAsync();

        var summary = RunSummaryCalculator.Calculate(
            new RunFacts
            {
                RunId = runId, Strategy = "distributed-lock", AccountId = accountId,
                StartingBalance = startingBalance, StartingVersion = startingVersion,
                FinalBalance = finalBalance, DurationMs = recorder.ElapsedMs(runId),
                Approvals = results.ToDictionary(r => r.ActorId, r => r.Approved),
            },
            recorder.EventsFor(runId));

        return (summary, accountId);
    }

    private async Task<(Guid AccountId, long Version)> SeedAsync(Guid runId, decimal openingBalance)
    {
        await using var db = fixture.NewDbContext();

        var account = new Account
        {
            Id = Guid.NewGuid(), AccountNumber = $"D-{runId:N}"[..16], Owner = "Advisory lock test",
            Balance = openingBalance,
        };

        db.Accounts.Add(account);
        await db.SaveChangesAsync();

        return (account.Id, (long)account.Version);
    }

    private sealed class HarnessFactory(string connectionString) : IDbContextFactory<BankDbContext>
    {
        public BankDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<BankDbContext>().UseNpgsql(connectionString).Options);
    }
}
