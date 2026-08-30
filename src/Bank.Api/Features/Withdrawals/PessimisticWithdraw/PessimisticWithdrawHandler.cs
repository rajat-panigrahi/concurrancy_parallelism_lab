using System.Diagnostics;
using Bank.Api.Shared.Contention;
using Bank.Api.Shared.Interleaving;
using Bank.Api.Shared.Persistence;
using Bank.Api.Shared.Withdrawals;
using Microsoft.EntityFrameworkCore;

namespace Bank.Api.Features.Withdrawals.PessimisticWithdraw;

/// <summary>
/// Pessimistic concurrency: assume collisions are common, so take a lock on the row and
/// make everyone else queue.
/// </summary>
/// <remarks>
/// <para><c>SELECT … FOR UPDATE</c> takes a row-level write lock inside a transaction.
/// Any other transaction that asks for the same row blocks until this one commits or
/// rolls back. Nobody does wasted work; they wait instead.</para>
/// <para>Two things make this correct where the naive version was not: the read happens
/// <i>inside</i> the transaction that holds the lock, and the lock is held until commit.
/// The balance therefore cannot change between the check and the write.</para>
/// <para>This is the same shape as the in-process semaphore in lesson 02, with one
/// decisive difference: the lock lives in the database, which every instance shares. It
/// keeps working at any number of replicas.</para>
/// <para><b>SQL Server equivalent:</b>
/// <c>SELECT … FROM Accounts WITH (UPDLOCK, ROWLOCK) WHERE Id = @id</c>.</para>
/// </remarks>
public sealed class PessimisticWithdrawHandler(
    IDbContextFactory<BankDbContext> dbContextFactory,
    IContentionRecorder recorder,
    IInterleaveGate gate) : IWithdrawStrategy
{
    public string Name => "pessimistic";
    public StorageKind Storage => StorageKind.Postgres;
    public string Summary => "SELECT ... FOR UPDATE takes a row lock; everyone else queues.";

    public async Task<WithdrawResult> WithdrawAsync(
        WithdrawCommand command,
        CancellationToken cancellationToken = default)
    {
        recorder.Record(command.RunId, command.ActorId, command.AccountId,
            ContentionPhase.Started, amount: command.Amount);

        // Gate before the lock, never inside it. Holding actors inside a critical
        // section deadlocks: the lock holder waits for actors who cannot enter until it
        // releases. Same rule as the in-process lock (ADR-0008).
        recorder.Record(command.RunId, command.ActorId, command.AccountId, ContentionPhase.Gate);
        await gate.ReachAsync(command.RunId, InterleaveCheckpoints.BeforeAcquire, cancellationToken);

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        recorder.Record(command.RunId, command.ActorId, command.AccountId,
            ContentionPhase.LockWait,
            amount: command.Amount,
            note: "SELECT ... FOR UPDATE — blocks until whoever holds this row commits.");

        var clock = Stopwatch.StartNew();

        // xmin is a system column, so it is not included in SELECT * and has to be
        // named explicitly — EF needs every mapped column back.
        var account = await db.Accounts
            .FromSql($"""SELECT *, xmin FROM "Accounts" WHERE "Id" = {command.AccountId} FOR UPDATE""")
            .SingleAsync(cancellationToken);

        clock.Stop();
        var waitedMs = clock.Elapsed.TotalMilliseconds;
        var versionSeen = (long)account.Version;

        recorder.Record(command.RunId, command.ActorId, command.AccountId,
            ContentionPhase.LockAcquired,
            amount: command.Amount,
            waitedMs: waitedMs,
            note: waitedMs > 1
                ? $"Queued {waitedMs:0.0} ms behind another transaction."
                : "Row lock granted immediately.");

        recorder.Record(command.RunId, command.ActorId, command.AccountId,
            ContentionPhase.Read,
            balanceSeen: account.Balance,
            versionSeen: versionSeen,
            amount: command.Amount);

        if (command.ThinkTime > TimeSpan.Zero)
        {
            // Note the cost: this happens with the row lock held, so every other actor
            // waits through it. A long transaction is how pessimistic locking turns
            // into an outage.
            await Task.Delay(command.ThinkTime, cancellationToken);
        }

        if (account.Balance < command.Amount)
        {
            recorder.Record(command.RunId, command.ActorId, command.AccountId,
                ContentionPhase.Rejected,
                balanceSeen: account.Balance,
                amount: command.Amount,
                waitedMs: waitedMs,
                note: "Insufficient funds — and nobody could have changed the balance while we held the lock.");

            await transaction.CommitAsync(cancellationToken);
            return WithdrawResult.Rejected(account.Balance, "Insufficient funds.") with { WaitedMs = waitedMs };
        }

        account.Balance -= command.Amount;

        recorder.Record(command.RunId, command.ActorId, command.AccountId,
            ContentionPhase.Write,
            versionSeen: versionSeen,
            amount: command.Amount);

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        recorder.Record(command.RunId, command.ActorId, command.AccountId,
            ContentionPhase.Committed,
            balanceSeen: account.Balance,
            versionSeen: versionSeen,
            versionWritten: (long)account.Version,
            amount: command.Amount,
            waitedMs: waitedMs);

        return new WithdrawResult
        {
            Approved = true,
            BalanceAfter = account.Balance,
            Reason = "Approved.",
            WaitedMs = waitedMs,
        };
    }
}
