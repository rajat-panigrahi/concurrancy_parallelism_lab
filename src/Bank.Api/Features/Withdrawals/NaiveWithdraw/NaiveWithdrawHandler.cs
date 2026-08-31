using Bank.Api.Shared.Contention;
using Bank.Api.Shared.Interleaving;
using Bank.Api.Shared.Persistence;
using Bank.Api.Shared.Withdrawals;

namespace Bank.Api.Features.Withdrawals.NaiveWithdraw;

/// <summary>
/// A withdrawal written the way almost everyone writes it the first time — and the
/// way a surprising amount of production code still does.
/// </summary>
/// <remarks>
/// <para>Read the balance. Check it's enough. Subtract. Write it back. Every step is
/// correct on its own, and the whole thing is wrong, because between the read and the
/// write somebody else can do the same thing.</para>
/// <para>Nothing here needs to happen "at the same time". Actor B only has to read
/// before actor A writes. On one core, with no parallelism at all, this still breaks —
/// which is why the fix is never "add more cores" or "make it async".</para>
/// </remarks>
public sealed class NaiveWithdrawHandler(
    InMemoryAccountStore store,
    IContentionRecorder recorder,
    IInterleaveGate gate) : IWithdrawStrategy
{
    public string Name => "naive";
    public StorageKind Storage => StorageKind.InMemory;
    public string Summary => "Read, decide, write. No coordination of any kind.";

    public async Task<WithdrawResult> WithdrawAsync(
        WithdrawCommand command,
        CancellationToken cancellationToken = default)
    {
        recorder.Record(command.RunId, command.ActorId, command.AccountId,
            ContentionPhase.Started, amount: command.Amount);

        // ---- READ -------------------------------------------------------------
        var snapshot = store.Read(command.AccountId)
            ?? throw new KeyNotFoundException($"Account {command.AccountId} not found.");

        recorder.Record(command.RunId, command.ActorId, command.AccountId,
            ContentionPhase.Read,
            balanceSeen: snapshot.Balance,
            versionSeen: snapshot.Version,
            amount: command.Amount);

        // ---- THINK ------------------------------------------------------------
        // Everything below is decided from `snapshot`, a value that stops being true
        // the moment anyone else writes. The wider this gap, the likelier the bug —
        // but the bug does not need the gap, it only needs the read to precede
        // someone else's write.
        recorder.Record(command.RunId, command.ActorId, command.AccountId, ContentionPhase.Gate);
        await gate.ReachAsync(command.RunId, InterleaveCheckpoints.AfterRead, cancellationToken);

        if (command.ThinkTime > TimeSpan.Zero)
        {
            await Task.Delay(command.ThinkTime, cancellationToken);
        }

        if (snapshot.Balance < command.Amount)
        {
            recorder.Record(command.RunId, command.ActorId, command.AccountId,
                ContentionPhase.Rejected,
                balanceSeen: snapshot.Balance,
                amount: command.Amount,
                note: "Insufficient funds, judged against the balance this actor read.");

            return WithdrawResult.Rejected(snapshot.Balance, "Insufficient funds.");
        }

        // ---- WRITE ------------------------------------------------------------
        // A blind overwrite. Not "subtract from whatever is there now" — "set it to
        // the number I calculated from a balance I read some time ago". Any write
        // that happened in between is now gone, with no error and no trace.
        var newBalance = snapshot.Balance - command.Amount;

        recorder.Record(command.RunId, command.ActorId, command.AccountId,
            ContentionPhase.Write,
            balanceSeen: snapshot.Balance,
            versionSeen: snapshot.Version,
            amount: command.Amount,
            note: $"Blind write of {newBalance:0.00}, computed from a read of {snapshot.Balance:0.00}.");

        var versionWritten = store.Write(command.AccountId, newBalance);

        recorder.Record(command.RunId, command.ActorId, command.AccountId,
            ContentionPhase.Committed,
            balanceSeen: newBalance,
            versionSeen: snapshot.Version,
            versionWritten: versionWritten,
            amount: command.Amount);

        return new WithdrawResult
        {
            Approved = true,
            BalanceAfter = newBalance,
            Reason = "Approved.",
        };
    }
}
