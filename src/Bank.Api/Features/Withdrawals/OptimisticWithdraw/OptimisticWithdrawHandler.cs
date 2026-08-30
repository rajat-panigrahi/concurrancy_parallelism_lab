using Bank.Api.Shared.Contention;
using Bank.Api.Shared.Interleaving;
using Bank.Api.Shared.Persistence;
using Bank.Api.Shared.Withdrawals;
using Microsoft.EntityFrameworkCore;

namespace Bank.Api.Features.Withdrawals.OptimisticWithdraw;

/// <summary>
/// Optimistic concurrency: assume collisions are rare, let everyone try, detect the
/// ones that collided, and make them start over.
/// </summary>
/// <remarks>
/// <para>Nobody waits for anybody. Every actor reads, decides and writes at full speed.
/// The database refuses any write whose row changed since it was read, and the loser
/// re-reads and re-decides against fresh data.</para>
/// <para>The mechanism is one line of configuration in <see cref="BankDbContext"/>:
/// <c>Version</c> is mapped to Postgres' <c>xmin</c> and marked a concurrency token, so
/// EF appends <c>WHERE xmin = @original</c> to every UPDATE. Matching zero rows means
/// somebody got there first, and EF raises
/// <see cref="DbUpdateConcurrencyException"/>.</para>
/// <para>The cost is <b>wasted work</b>, not waiting: the loser did its read and its
/// decision for nothing. That is cheap when conflicts are rare and ruinous when they
/// are not — see lesson 05.</para>
/// </remarks>
public sealed class OptimisticWithdrawHandler(
    IDbContextFactory<BankDbContext> dbContextFactory,
    IContentionRecorder recorder,
    IInterleaveGate gate) : IWithdrawStrategy
{
    /// <summary>
    /// Retries are bounded on purpose. An unbounded retry loop under heavy contention
    /// is a livelock: every actor keeps colliding, keeps retrying, and the system does
    /// nothing but burn CPU. Giving up and telling the caller is the honest outcome.
    /// </summary>
    public const int MaxAttempts = 5;

    public string Name => "optimistic";
    public StorageKind Storage => StorageKind.Postgres;
    public string Summary => "Everyone writes; the database rejects stale writes; losers retry.";

    public async Task<WithdrawResult> WithdrawAsync(
        WithdrawCommand command,
        CancellationToken cancellationToken = default)
    {
        recorder.Record(command.RunId, command.ActorId, command.AccountId,
            ContentionPhase.Started, amount: command.Amount);

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            // A fresh context per attempt. Reusing one would keep the stale entity in
            // the change tracker, so the retry would re-send the same doomed UPDATE.
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

            var account = await db.Accounts.SingleOrDefaultAsync(a => a.Id == command.AccountId, cancellationToken)
                ?? throw new KeyNotFoundException($"Account {command.AccountId} not found.");

            var versionSeen = (long)account.Version;

            recorder.Record(command.RunId, command.ActorId, command.AccountId,
                ContentionPhase.Read,
                attempt: attempt,
                balanceSeen: account.Balance,
                versionSeen: versionSeen,
                amount: command.Amount);

            // Only the first attempt joins the barrier. Retries must not, because by
            // then some actors have already finished and would never arrive — the
            // barrier would hold the rest until its timeout.
            if (attempt == 1)
            {
                recorder.Record(command.RunId, command.ActorId, command.AccountId,
                    ContentionPhase.Gate, attempt: attempt);
                await gate.ReachAsync(command.RunId, InterleaveCheckpoints.AfterRead, cancellationToken);
            }

            if (command.ThinkTime > TimeSpan.Zero)
            {
                await Task.Delay(command.ThinkTime, cancellationToken);
            }

            if (account.Balance < command.Amount)
            {
                recorder.Record(command.RunId, command.ActorId, command.AccountId,
                    ContentionPhase.Rejected,
                    attempt: attempt,
                    balanceSeen: account.Balance,
                    amount: command.Amount,
                    note: "Insufficient funds, judged against a balance the database confirmed was current.");

                return WithdrawResult.Rejected(account.Balance, "Insufficient funds.", attempt);
            }

            account.Balance -= command.Amount;

            recorder.Record(command.RunId, command.ActorId, command.AccountId,
                ContentionPhase.Write,
                attempt: attempt,
                versionSeen: versionSeen,
                amount: command.Amount,
                note: $"UPDATE ... WHERE xmin = {versionSeen}");

            try
            {
                await db.SaveChangesAsync(cancellationToken);

                recorder.Record(command.RunId, command.ActorId, command.AccountId,
                    ContentionPhase.Committed,
                    attempt: attempt,
                    balanceSeen: account.Balance,
                    versionSeen: versionSeen,
                    versionWritten: (long)account.Version,
                    amount: command.Amount);

                return new WithdrawResult
                {
                    Approved = true,
                    BalanceAfter = account.Balance,
                    Reason = attempt == 1 ? "Approved." : $"Approved on attempt {attempt}.",
                    Attempts = attempt,
                };
            }
            catch (DbUpdateConcurrencyException)
            {
                // Zero rows matched, so the row changed after we read it. Nothing was
                // corrupted and nothing was lost — the database refused, loudly. That
                // is the entire difference from the naive handler, which was refused
                // by nobody.
                recorder.Record(command.RunId, command.ActorId, command.AccountId,
                    ContentionPhase.Conflict,
                    attempt: attempt,
                    versionSeen: versionSeen,
                    amount: command.Amount,
                    note: $"Rejected: xmin is no longer {versionSeen}. Someone wrote first.");

                if (attempt < MaxAttempts)
                {
                    recorder.Record(command.RunId, command.ActorId, command.AccountId,
                        ContentionPhase.Retry, attempt: attempt + 1,
                        note: "Re-reading and deciding again against fresh data.");
                }
            }
        }

        recorder.Record(command.RunId, command.ActorId, command.AccountId,
            ContentionPhase.Rejected,
            attempt: MaxAttempts,
            amount: command.Amount,
            note: $"Gave up after {MaxAttempts} conflicts — contention is too high for optimistic control here.");

        return WithdrawResult.Rejected(0m, $"Gave up after {MaxAttempts} attempts.", MaxAttempts);
    }
}
