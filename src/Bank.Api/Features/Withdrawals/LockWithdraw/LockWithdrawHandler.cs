using Bank.Api.Shared.Contention;
using Bank.Api.Shared.Interleaving;
using Bank.Api.Shared.Locking;
using Bank.Api.Shared.Persistence;
using Bank.Api.Shared.Withdrawals;

namespace Bank.Api.Features.Withdrawals.LockWithdraw;

/// <summary>
/// The same withdrawal as <c>NaiveWithdraw</c>, with the read-decide-write sequence
/// wrapped in a per-account lock.
/// </summary>
/// <remarks>
/// <para>Two details do all the work, and both are easy to get wrong:</para>
/// <para><b>The read moves inside the lock.</b> Locking only the write would be
/// pointless — the stale value was already read. A lock protects an *invariant*, not a
/// statement, so it has to span every step that assumes the balance hasn't changed.</para>
/// <para><b>The lock is per account, not global.</b> One lock for the whole bank would
/// also be correct, and would serialise every customer behind every other customer.
/// Lock granularity is the dial between correctness and throughput: too coarse and you
/// have a queue, too fine and you have a deadlock (see the transfer slice).</para>
/// <para>And the catch, which is the point of the slice: this is correct on exactly one
/// instance. See <see cref="InProcessAccountLock"/>.</para>
/// </remarks>
public sealed class LockWithdrawHandler(
    InMemoryAccountStore store,
    IAccountLock accountLock,
    IContentionRecorder recorder,
    IInterleaveGate gate) : IWithdrawStrategy
{
    public string Name => "lock";
    public StorageKind Storage => StorageKind.InMemory;
    public string Summary => $"Read and write inside a per-account {accountLock.Kind}.";

    public async Task<WithdrawResult> WithdrawAsync(
        WithdrawCommand command,
        CancellationToken cancellationToken = default)
    {
        recorder.Record(command.RunId, command.ActorId, command.AccountId,
            ContentionPhase.Started, amount: command.Amount);

        // Gate BEFORE acquiring, never after. Holding actors inside the critical
        // section would deadlock: the lock holder waits for actors who cannot enter
        // until it releases. Here the barrier simply guarantees all actors reach for
        // the lock at the same instant, which is what makes the contention real.
        recorder.Record(command.RunId, command.ActorId, command.AccountId, ContentionPhase.Gate);
        await gate.ReachAsync(command.RunId, InterleaveCheckpoints.BeforeAcquire, cancellationToken);

        recorder.Record(command.RunId, command.ActorId, command.AccountId,
            ContentionPhase.LockWait, amount: command.Amount);

        await using var handle = await accountLock.AcquireAsync(command.AccountId, cancellationToken);

        recorder.Record(command.RunId, command.ActorId, command.AccountId,
            ContentionPhase.LockAcquired,
            amount: command.Amount,
            waitedMs: handle.WaitedMs,
            note: handle.WaitedMs > 1 ? $"Queued {handle.WaitedMs:0.0} ms behind another actor." : "Acquired immediately.");

        // Everything from here to the dispose is the critical section.
        var snapshot = store.Read(command.AccountId)
            ?? throw new KeyNotFoundException($"Account {command.AccountId} not found.");

        recorder.Record(command.RunId, command.ActorId, command.AccountId,
            ContentionPhase.Read,
            balanceSeen: snapshot.Balance,
            versionSeen: snapshot.Version,
            amount: command.Amount);

        if (command.ThinkTime > TimeSpan.Zero)
        {
            // Note what this costs: think time now happens with the lock held, so every
            // other actor waits through it. Pessimistic coordination converts other
            // people's latency into your own.
            await Task.Delay(command.ThinkTime, cancellationToken);
        }

        if (snapshot.Balance < command.Amount)
        {
            recorder.Record(command.RunId, command.ActorId, command.AccountId,
                ContentionPhase.Rejected,
                balanceSeen: snapshot.Balance,
                amount: command.Amount,
                waitedMs: handle.WaitedMs,
                note: "Insufficient funds — and this time the balance was current.");

            return WithdrawResult.Rejected(snapshot.Balance, "Insufficient funds.") with { WaitedMs = handle.WaitedMs };
        }

        var newBalance = snapshot.Balance - command.Amount;

        recorder.Record(command.RunId, command.ActorId, command.AccountId,
            ContentionPhase.Write,
            balanceSeen: snapshot.Balance,
            versionSeen: snapshot.Version,
            amount: command.Amount);

        var versionWritten = store.Write(command.AccountId, newBalance);

        recorder.Record(command.RunId, command.ActorId, command.AccountId,
            ContentionPhase.Committed,
            balanceSeen: newBalance,
            versionSeen: snapshot.Version,
            versionWritten: versionWritten,
            amount: command.Amount,
            waitedMs: handle.WaitedMs);

        return new WithdrawResult
        {
            Approved = true,
            BalanceAfter = newBalance,
            Reason = "Approved.",
            WaitedMs = handle.WaitedMs,
        };
    }
}
