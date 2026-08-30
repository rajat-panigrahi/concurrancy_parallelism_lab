using Bank.Api.Shared.Contention;
using Bank.Api.Shared.Endpoints;
using Bank.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Bank.Api.Features.Transfers.DeadlockTransfer;

/// <summary>
/// Runs Alice→Bob and Bob→Alice at the same instant, with the lock ordering as a flag,
/// so you can watch the same code deadlock and then not deadlock.
/// </summary>
public sealed class DeadlockDemoHandler(
    IDbContextFactory<BankDbContext> dbContextFactory,
    DeadlockTransferHandler transfers,
    ContentionRecorder recorder)
{
    public async Task<DeadlockDemoResponse> HandleAsync(
        DeadlockDemoRequest request,
        CancellationToken cancellationToken)
    {
        var runId = Guid.NewGuid();
        recorder.BeginRun(runId);

        var (alice, bob) = await SeedPairAsync(runId, cancellationToken);
        var hold = TimeSpan.FromMilliseconds(Math.Clamp(request.HoldBetweenLocksMs, 0, 2000));

        // Deliberately opposing directions. This is the circular wait, expressed as
        // two perfectly reasonable business operations.
        var aliceToBob = new TransferCommand
        {
            RunId = runId, ActorId = "alice->bob",
            FromAccountId = alice, ToAccountId = bob,
            Amount = request.Amount, OrderLocks = request.OrderLocks, HoldBetweenLocks = hold,
        };

        var bobToAlice = new TransferCommand
        {
            RunId = runId, ActorId = "bob->alice",
            FromAccountId = bob, ToAccountId = alice,
            Amount = request.Amount, OrderLocks = request.OrderLocks, HoldBetweenLocks = hold,
        };

        var before = await TotalAsync(alice, bob, cancellationToken);

        var results = await Task.WhenAll(
            transfers.TransferAsync(aliceToBob, cancellationToken),
            transfers.TransferAsync(bobToAlice, cancellationToken));

        var after = await TotalAsync(alice, bob, cancellationToken);
        var deadlocked = results.Any(r => r.Deadlocked);

        return new DeadlockDemoResponse
        {
            RunId = runId,
            OrderLocks = request.OrderLocks,
            DeadlockDetected = deadlocked,
            Results = results,
            TotalMoneyBefore = before,
            TotalMoneyAfter = after,
            Verdict = (request.OrderLocks, deadlocked) switch
            {
                (false, true) =>
                    "Deadlock. Each transaction locked its 'from' account and then waited for the other's. "
                    + "PostgreSQL broke the cycle by killing one of them (40P01) — so a customer got an error, "
                    + "though no money was lost.",
                (false, false) =>
                    "No deadlock this time — the two transactions happened not to overlap. That is exactly what "
                    + "makes deadlocks so hard to catch in testing. Raise holdBetweenLocksMs and try again.",
                (true, false) =>
                    "No deadlock, and it is not luck. Both transactions took the two row locks in the same global "
                    + "order, so a cycle cannot form. Same work, same locks, one changed line.",
                (true, true) =>
                    "Unexpected: a deadlock occurred despite ordered locking. That means some other code path is "
                    + "taking these locks in a different order.",
            },
        };
    }

    private async Task<(Guid Alice, Guid Bob)> SeedPairAsync(Guid runId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var alice = new Account
        {
            Id = Guid.NewGuid(), AccountNumber = $"ALI-{runId:N}"[..16], Owner = "Alice", Balance = 500m,
        };

        var bob = new Account
        {
            Id = Guid.NewGuid(), AccountNumber = $"BOB-{runId:N}"[..16], Owner = "Bob", Balance = 500m,
        };

        db.Accounts.AddRange(alice, bob);
        await db.SaveChangesAsync(cancellationToken);

        return (alice.Id, bob.Id);
    }

    private async Task<decimal> TotalAsync(Guid alice, Guid bob, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Accounts.AsNoTracking()
            .Where(a => a.Id == alice || a.Id == bob)
            .SumAsync(a => a.Balance, cancellationToken);
    }
}

public sealed class DeadlockDemoEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/lab/deadlock", async (
                DeadlockDemoRequest request,
                DeadlockDemoHandler handler,
                ContentionRecorder recorder,
                CancellationToken cancellationToken) =>
            {
                var response = await handler.HandleAsync(request, cancellationToken);
                return Results.Ok(new { response, timeline = recorder.EventsFor(response.RunId) });
            })
            .WithName("RunDeadlockDemo")
            .WithTags("Lab")
            .WithSummary("Two opposing transfers at once. Set orderLocks=true to see the fix.");
    }
}
