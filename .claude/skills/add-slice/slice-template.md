# Slice handler template

Replace `<Name>`, and delete whichever branch does not apply (lock-based vs retry-based).

```csharp
using Bank.Api.Shared.Contention;
using Bank.Api.Shared.Interleaving;
using Bank.Api.Shared.Persistence;
using Bank.Api.Shared.Withdrawals;
using Microsoft.EntityFrameworkCore;

namespace Bank.Api.Features.Withdrawals.<Name>Withdraw;

/// <summary>One line: what this demonstrates.</summary>
/// <remarks>
/// What it costs and when it breaks down. This comment is teaching material — the repo
/// is read as much as it is run.
/// </remarks>
public sealed class <Name>WithdrawHandler(
    IDbContextFactory<BankDbContext> dbContextFactory,   // or InMemoryAccountStore
    IContentionRecorder recorder,
    IInterleaveGate gate) : IWithdrawStrategy
{
    public string Name => "<name>";
    public StorageKind Storage => StorageKind.Postgres;   // or InMemory
    public string Summary => "One line, shown in the UI's comparison table.";

    public async Task<WithdrawResult> WithdrawAsync(
        WithdrawCommand command,
        CancellationToken cancellationToken = default)
    {
        recorder.Record(command.RunId, command.ActorId, command.AccountId,
            ContentionPhase.Started, amount: command.Amount);

        // LOCK-BASED: gate BEFORE acquiring. Gating inside the critical section deadlocks.
        recorder.Record(command.RunId, command.ActorId, command.AccountId, ContentionPhase.Gate);
        await gate.ReachAsync(command.RunId, InterleaveCheckpoints.BeforeAcquire, cancellationToken);

        // ... acquire, then read INSIDE the lock ...

        // LOCK-FREE (naive/optimistic): gate AFTER the read instead, and in a retry loop
        // only when attempt == 1 — retrying actors that re-join a barrier stall the run.
        // await gate.ReachAsync(command.RunId, InterleaveCheckpoints.AfterRead, cancellationToken);

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
                note: "Insufficient funds.");

            return WithdrawResult.Rejected(account.Balance, "Insufficient funds.");
        }

        account.Balance -= command.Amount;

        recorder.Record(command.RunId, command.ActorId, command.AccountId,
            ContentionPhase.Write, versionSeen: versionSeen, amount: command.Amount);

        // ... commit ...

        // versionSeen AND versionWritten are both required: the "who won" replay in
        // RunSummaryCalculator follows the version chain to find silently-erased writes.
        recorder.Record(command.RunId, command.ActorId, command.AccountId,
            ContentionPhase.Committed,
            balanceSeen: account.Balance,
            versionSeen: versionSeen,
            versionWritten: (long)account.Version,
            amount: command.Amount);

        return new WithdrawResult
        {
            Approved = true,
            BalanceAfter = account.Balance,
            Reason = "Approved.",
        };
    }
}
```

Then register it in `Program.cs`, or it never appears in the lab:

```csharp
builder.Services.AddSingleton<IWithdrawStrategy, <Name>WithdrawHandler>();
```

**Watch out:** every constructor parameter must be used. `TreatWarningsAsErrors` turns an
unread primary-constructor parameter into CS9113, which fails the build.
