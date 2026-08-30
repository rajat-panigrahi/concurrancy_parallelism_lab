using Bank.Api.Shared.Contention;
using Bank.Api.Shared.Interleaving;
using Bank.Api.Shared.Locking;
using Bank.Api.Shared.Persistence;
using Bank.Api.Shared.Withdrawals;
using Microsoft.EntityFrameworkCore;

namespace Bank.Api.Features.Withdrawals.DistributedLockWithdraw;

/// <summary>
/// The same shape as <c>LockWithdraw</c>, with both of its process-local pieces replaced
/// by shared ones — so it is the version that survives scale-out.
/// </summary>
/// <remarks>
/// <para>Compare the two handlers side by side. The logic is identical: acquire, read,
/// decide, write, release. Only the dependencies differ:</para>
/// <list type="table">
/// <item><term>LockWithdraw</term><description>in-process semaphore + process memory —
/// correct on one instance, silently broken on three</description></item>
/// <item><term>DistributedLockWithdraw</term><description>Postgres advisory lock +
/// Postgres rows — correct at any number of instances</description></item>
/// </list>
/// <para>That is the whole answer to "is scaling an application concern or an
/// infrastructure concern?". Kubernetes will happily run three replicas of either one.
/// Only this version stays correct when it does.</para>
/// <para><b>When you would actually use this</b> rather than the pessimistic row lock:
/// when the thing you need to serialise is not a single database row. Calling an
/// external payment provider once, running one scheduled job across a cluster, or
/// coordinating work that spans several tables. If it *is* one row,
/// <c>SELECT … FOR UPDATE</c> is simpler and cheaper.</para>
/// </remarks>
public sealed class DistributedLockWithdrawHandler(
    IDbContextFactory<BankDbContext> dbContextFactory,
    PostgresAdvisoryAccountLock accountLock,
    IContentionRecorder recorder,
    IInterleaveGate gate) : IWithdrawStrategy
{
    public string Name => "distributed-lock";
    public StorageKind Storage => StorageKind.Postgres;
    public string Summary => "Postgres advisory lock around a read-decide-write. Survives scale-out.";

    public async Task<WithdrawResult> WithdrawAsync(
        WithdrawCommand command,
        CancellationToken cancellationToken = default)
    {
        recorder.Record(command.RunId, command.ActorId, command.AccountId,
            ContentionPhase.Started, amount: command.Amount);

        // Gate before acquiring, as always — holding actors inside a critical section
        // deadlocks the run.
        recorder.Record(command.RunId, command.ActorId, command.AccountId, ContentionPhase.Gate);
        await gate.ReachAsync(command.RunId, InterleaveCheckpoints.BeforeAcquire, cancellationToken);

        recorder.Record(command.RunId, command.ActorId, command.AccountId,
            ContentionPhase.LockWait,
            amount: command.Amount,
            note: "pg_advisory_lock — every instance queues on the same lock.");

        await using var handle = await accountLock.AcquireAsync(command.AccountId, cancellationToken);

        recorder.Record(command.RunId, command.ActorId, command.AccountId,
            ContentionPhase.LockAcquired,
            amount: command.Amount,
            waitedMs: handle.WaitedMs,
            note: handle.WaitedMs > 1
                ? $"Queued {handle.WaitedMs:0.0} ms behind another instance or request."
                : "Advisory lock granted immediately.");

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var account = await db.Accounts.SingleAsync(a => a.Id == command.AccountId, cancellationToken);
        var versionSeen = (long)account.Version;

        recorder.Record(command.RunId, command.ActorId, command.AccountId,
            ContentionPhase.Read,
            balanceSeen: account.Balance,
            versionSeen: versionSeen,
            amount: command.Amount);

        if (command.ThinkTime > TimeSpan.Zero)
        {
            await Task.Delay(command.ThinkTime, cancellationToken);
        }

        if (account.Balance < command.Amount)
        {
            recorder.Record(command.RunId, command.ActorId, command.AccountId,
                ContentionPhase.Rejected,
                balanceSeen: account.Balance,
                amount: command.Amount,
                waitedMs: handle.WaitedMs,
                note: "Insufficient funds — nobody on any instance could change it while we held the lock.");

            return WithdrawResult.Rejected(account.Balance, "Insufficient funds.") with { WaitedMs = handle.WaitedMs };
        }

        account.Balance -= command.Amount;

        recorder.Record(command.RunId, command.ActorId, command.AccountId,
            ContentionPhase.Write, versionSeen: versionSeen, amount: command.Amount);

        await db.SaveChangesAsync(cancellationToken);

        recorder.Record(command.RunId, command.ActorId, command.AccountId,
            ContentionPhase.Committed,
            balanceSeen: account.Balance,
            versionSeen: versionSeen,
            versionWritten: (long)account.Version,
            amount: command.Amount,
            waitedMs: handle.WaitedMs);

        return new WithdrawResult
        {
            Approved = true,
            BalanceAfter = account.Balance,
            Reason = "Approved.",
            WaitedMs = handle.WaitedMs,
        };
    }
}
