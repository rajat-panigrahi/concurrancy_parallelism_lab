using Bank.Api.Shared.Contention;
using Bank.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Bank.Api.Features.Transfers.DeadlockTransfer;

/// <summary>
/// A transfer needs two accounts locked at once, which is where deadlocks come from.
/// </summary>
/// <remarks>
/// <para>Alice sends ₹50 to Bob while Bob sends ₹50 to Alice. Each transaction locks its
/// "from" account first, then reaches for its "to" account — which the other one is
/// already holding. Neither can proceed and neither will let go.</para>
/// <para>All four Coffman conditions are present: mutual exclusion (row locks), hold
/// and wait (holding one, requesting another), no preemption, and a circular wait. Break
/// any one and the deadlock is impossible; the cheapest to break is the circular wait,
/// by always acquiring in the same global order.</para>
/// <para>PostgreSQL detects the cycle and kills one transaction with SQLSTATE
/// <c>40P01</c>, so this fails loudly rather than hanging forever. That is a kindness
/// the in-process version in lesson 02 does not offer.</para>
/// </remarks>
public sealed class DeadlockTransferHandler(
    IDbContextFactory<BankDbContext> dbContextFactory,
    IContentionRecorder recorder)
{
    /// <summary>PostgreSQL's SQLSTATE for a detected deadlock.</summary>
    public const string DeadlockSqlState = "40P01";

    public async Task<TransferResult> TransferAsync(
        TransferCommand command,
        CancellationToken cancellationToken = default)
    {
        recorder.Record(command.RunId, command.ActorId, command.FromAccountId,
            ContentionPhase.Started, amount: command.Amount);

        // THE FIX, in one line. Sorting the ids means every transaction in the system
        // reaches for the same account first, so a cycle cannot form. The order is
        // arbitrary — it only has to be *consistent*.
        var (firstLock, secondLock) = command.OrderLocks && command.ToAccountId.CompareTo(command.FromAccountId) < 0
            ? (command.ToAccountId, command.FromAccountId)
            : (command.FromAccountId, command.ToAccountId);

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            recorder.Record(command.RunId, command.ActorId, firstLock,
                ContentionPhase.LockWait, note: $"Locking first account {Short(firstLock)}.");

            await LockAsync(db, firstLock, cancellationToken);

            recorder.Record(command.RunId, command.ActorId, firstLock,
                ContentionPhase.LockAcquired, note: $"Holding {Short(firstLock)}.");

            // Widen the window so both transactions are holding one lock before either
            // asks for the second. Without this the deadlock is timing-dependent; with
            // it, it is reliable.
            if (command.HoldBetweenLocks > TimeSpan.Zero)
            {
                await Task.Delay(command.HoldBetweenLocks, cancellationToken);
            }

            recorder.Record(command.RunId, command.ActorId, secondLock,
                ContentionPhase.LockWait,
                note: $"Now asking for {Short(secondLock)} while still holding {Short(firstLock)}.");

            await LockAsync(db, secondLock, cancellationToken);

            recorder.Record(command.RunId, command.ActorId, secondLock,
                ContentionPhase.LockAcquired, note: "Both accounts held.");

            var from = await db.Accounts.SingleAsync(a => a.Id == command.FromAccountId, cancellationToken);
            var to = await db.Accounts.SingleAsync(a => a.Id == command.ToAccountId, cancellationToken);

            if (from.Balance < command.Amount)
            {
                await transaction.RollbackAsync(cancellationToken);

                recorder.Record(command.RunId, command.ActorId, command.FromAccountId,
                    ContentionPhase.Rejected, note: "Insufficient funds.");

                return new TransferResult
                {
                    ActorId = command.ActorId,
                    Succeeded = false,
                    Deadlocked = false,
                    Reason = "Insufficient funds.",
                };
            }

            from.Balance -= command.Amount;
            to.Balance += command.Amount;

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            recorder.Record(command.RunId, command.ActorId, command.FromAccountId,
                ContentionPhase.Committed, amount: command.Amount,
                note: $"Transferred {command.Amount:0.00} to {Short(command.ToAccountId)}.");

            return new TransferResult
            {
                ActorId = command.ActorId,
                Succeeded = true,
                Deadlocked = false,
                Reason = "Transferred.",
            };
        }
        catch (PostgresException ex) when (ex.SqlState == DeadlockSqlState)
        {
            // Postgres broke the tie by killing this transaction. Its work is rolled
            // back entirely, so no money is lost — the cost of a deadlock here is a
            // failed request, not corruption.
            recorder.Record(command.RunId, command.ActorId, command.FromAccountId,
                ContentionPhase.Failed,
                note: "DEADLOCK. PostgreSQL detected a lock cycle and terminated this transaction (40P01).");

            return new TransferResult
            {
                ActorId = command.ActorId,
                Succeeded = false,
                Deadlocked = true,
                Reason = "Deadlock detected; this transaction was rolled back.",
            };
        }
    }

    private static Task LockAsync(BankDbContext db, Guid accountId, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlAsync(
            $"""SELECT 1 FROM "Accounts" WHERE "Id" = {accountId} FOR UPDATE""",
            cancellationToken);

    private static string Short(Guid id) => id.ToString("N")[..6];
}
