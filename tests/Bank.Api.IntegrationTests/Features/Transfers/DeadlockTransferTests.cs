using Bank.Api.Features.Transfers.DeadlockTransfer;
using Bank.Api.IntegrationTests.Infrastructure;
using Bank.Api.Shared.Contention;
using Bank.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Bank.Api.IntegrationTests.Features.Transfers;

/// <summary>
/// Alice sends ₹50 to Bob while Bob sends ₹50 to Alice. Two ordinary business
/// operations that, run together, form a lock cycle.
/// </summary>
[Collection(PostgresCollection.Name)]
public class DeadlockTransferTests(PostgresFixture fixture)
{
    [Fact]
    public async Task OpposingTransfers_WithNaiveLockOrder_Deadlock()
    {
        var (handler, alice, bob, runId) = await SetUpAsync();

        var results = await Task.WhenAll(
            handler.TransferAsync(Transfer(runId, "alice->bob", alice, bob, orderLocks: false)),
            handler.TransferAsync(Transfer(runId, "bob->alice", bob, alice, orderLocks: false)));

        results.ShouldContain(r => r.Deadlocked,
            "each transaction holds its 'from' row and waits for the other's — that is a cycle, and Postgres must break it");

        // Exactly one victim: Postgres kills one transaction and lets the other finish.
        results.Count(r => r.Deadlocked).ShouldBe(1);
        results.Count(r => r.Succeeded).ShouldBe(1);
    }

    [Fact]
    public async Task ADeadlockCostsARequest_ButNeverMoney()
    {
        var (handler, alice, bob, runId) = await SetUpAsync();
        var before = await TotalAsync(alice, bob);

        await Task.WhenAll(
            handler.TransferAsync(Transfer(runId, "alice->bob", alice, bob, orderLocks: false)),
            handler.TransferAsync(Transfer(runId, "bob->alice", bob, alice, orderLocks: false)));

        var after = await TotalAsync(alice, bob);

        // The victim's transaction is rolled back in full. A deadlock is a failed
        // request, not corruption — which is exactly why it is less dangerous than the
        // lost update in lesson 01, despite being far more alarming to look at.
        after.ShouldBe(before, "the rolled-back transaction must leave no trace");
    }

    [Fact]
    public async Task OrderingTheLocksConsistently_RemovesTheDeadlockEntirely()
    {
        var (handler, alice, bob, runId) = await SetUpAsync();
        var before = await TotalAsync(alice, bob);

        var results = await Task.WhenAll(
            handler.TransferAsync(Transfer(runId, "alice->bob", alice, bob, orderLocks: true)),
            handler.TransferAsync(Transfer(runId, "bob->alice", bob, alice, orderLocks: true)));

        // Same work, same two locks, same contention. The only change is that both
        // transactions reach for the lower account id first, so no cycle can form.
        results.ShouldAllBe(r => !r.Deadlocked);
        results.ShouldAllBe(r => r.Succeeded);
        (await TotalAsync(alice, bob)).ShouldBe(before, "money moved both ways and nets to zero");
    }

    [Fact]
    public async Task RepeatedOrderedTransfers_NeverDeadlock()
    {
        // A deadlock that shows up one run in ten is the worst kind of bug, so the fix
        // has to be proven repeatedly rather than once.
        for (var i = 0; i < 5; i++)
        {
            var (handler, alice, bob, runId) = await SetUpAsync();

            var results = await Task.WhenAll(
                handler.TransferAsync(Transfer(runId, "alice->bob", alice, bob, orderLocks: true)),
                handler.TransferAsync(Transfer(runId, "bob->alice", bob, alice, orderLocks: true)));

            results.ShouldAllBe(r => !r.Deadlocked, $"iteration {i} deadlocked despite ordered locking");
        }
    }

    private static TransferCommand Transfer(Guid runId, string actorId, Guid from, Guid to, bool orderLocks) =>
        new()
        {
            RunId = runId,
            ActorId = actorId,
            FromAccountId = from,
            ToAccountId = to,
            Amount = 50m,
            OrderLocks = orderLocks,
            HoldBetweenLocks = TimeSpan.FromMilliseconds(120),
        };

    private async Task<(DeadlockTransferHandler Handler, Guid Alice, Guid Bob, Guid RunId)> SetUpAsync()
    {
        var recorder = new ContentionRecorder();
        var runId = Guid.NewGuid();
        recorder.BeginRun(runId);

        await using var db = fixture.NewDbContext();

        var alice = new Account
        {
            Id = Guid.NewGuid(), AccountNumber = $"A-{Guid.NewGuid():N}"[..16], Owner = "Alice", Balance = 500m,
        };

        var bob = new Account
        {
            Id = Guid.NewGuid(), AccountNumber = $"B-{Guid.NewGuid():N}"[..16], Owner = "Bob", Balance = 500m,
        };

        db.Accounts.AddRange(alice, bob);
        await db.SaveChangesAsync();

        var handler = new DeadlockTransferHandler(
            new HarnessDbContextFactory(fixture.ConnectionString), recorder);

        return (handler, alice.Id, bob.Id, runId);
    }

    private async Task<decimal> TotalAsync(Guid alice, Guid bob)
    {
        await using var db = fixture.NewDbContext();
        return await db.Accounts.AsNoTracking()
            .Where(a => a.Id == alice || a.Id == bob)
            .SumAsync(a => a.Balance);
    }

    private sealed class HarnessDbContextFactory(string connectionString) : IDbContextFactory<BankDbContext>
    {
        public BankDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<BankDbContext>().UseNpgsql(connectionString).Options);
    }
}
