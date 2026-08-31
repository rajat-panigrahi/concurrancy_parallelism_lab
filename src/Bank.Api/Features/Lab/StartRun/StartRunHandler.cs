using Bank.Api.Shared.Contention;
using Bank.Api.Shared.Interleaving;
using Bank.Api.Shared.Persistence;
using Bank.Api.Shared.Withdrawals;
using Microsoft.EntityFrameworkCore;

namespace Bank.Api.Features.Lab.StartRun;

/// <summary>
/// Sets up a fresh account, launches N actors at it simultaneously, and turns the
/// resulting timeline into a verdict.
/// </summary>
/// <remarks>
/// This is the single entry point the UI drives. Every strategy runs through the same
/// orchestration with the same inputs, which is what makes the comparison honest — the
/// only variable between two runs is the handler.
/// </remarks>
public sealed class StartRunHandler(
    IEnumerable<IWithdrawStrategy> strategies,
    InMemoryAccountStore store,
    IDbContextFactory<BankDbContext> dbContextFactory,
    ContentionRecorder recorder,
    LabInterleaveGate gate,
    LabRunStore runs)
{
    public IReadOnlyList<StrategyInfo> Strategies => strategies
        .Select(s => new StrategyInfo
        {
            Name = s.Name,
            Storage = s.Storage.ToString(),
            Summary = s.Summary,
            ScalesAcrossInstances = s.Storage == StorageKind.Postgres,
        })
        .OrderBy(s => s.Name, StringComparer.Ordinal)
        .ToArray();

    public async Task<StartRunResponse> HandleAsync(StartRunRequest request, CancellationToken cancellationToken)
    {
        var strategy = strategies.FirstOrDefault(s =>
            string.Equals(s.Name, request.Strategy, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Unknown strategy '{request.Strategy}'.", nameof(request));

        var actors = Math.Clamp(request.Actors, 1, 64);
        var runId = Guid.NewGuid();

        // Each run gets a brand new account so runs never interfere with each other,
        // in whichever store the chosen strategy coordinates through.
        var (accountId, startingVersion) = strategy.Storage switch
        {
            StorageKind.Postgres => await SeedPostgresAccountAsync(runId, request.StartingBalance, cancellationToken),
            _ => SeedInMemoryAccount(runId, request.StartingBalance),
        };

        recorder.BeginRun(runId);

        using var forcing = request.ForceRace ? gate.ForceRace(runId, actors) : null as IDisposable;

        var commands = Enumerable.Range(1, actors).Select(i => new WithdrawCommand
        {
            RunId = runId,
            ActorId = $"actor-{i}",
            AccountId = accountId,
            Amount = request.AmountEach,
            ThinkTime = TimeSpan.FromMilliseconds(Math.Clamp(request.ThinkTimeMs, 0, 2000)),
        }).ToArray();

        var started = recorder.ElapsedMs(runId);

        // Task.WhenAll starts all of them and waits for all of them. This is
        // concurrency, not parallelism: these actors mostly wait, and they would
        // interleave on a single core just as well.
        var results = await Task.WhenAll(commands.Select(async command =>
        {
            try
            {
                var result = await strategy.WithdrawAsync(command, cancellationToken);
                return (command.ActorId, Approved: result.Approved);
            }
            catch (Exception ex)
            {
                recorder.Record(runId, command.ActorId, command.AccountId,
                    ContentionPhase.Failed, note: ex.Message);
                return (command.ActorId, Approved: false);
            }
        }));

        var duration = recorder.ElapsedMs(runId) - started;

        var finalBalance = strategy.Storage switch
        {
            StorageKind.Postgres => await ReadPostgresBalanceAsync(accountId, cancellationToken),
            _ => store.Read(accountId)!.Value.Balance,
        };

        var summary = RunSummaryCalculator.Calculate(
            new RunFacts
            {
                RunId = runId,
                Strategy = strategy.Name,
                AccountId = accountId,
                StartingBalance = request.StartingBalance,
                StartingVersion = startingVersion,
                FinalBalance = finalBalance,
                DurationMs = duration,
                Approvals = results.ToDictionary(r => r.ActorId, r => r.Approved),
            },
            recorder.EventsFor(runId));

        runs.Save(summary);

        return new StartRunResponse
        {
            RunId = runId,
            Summary = summary,
            Timeline = recorder.EventsFor(runId),
        };
    }

    private (Guid AccountId, long StartingVersion) SeedInMemoryAccount(Guid runId, decimal openingBalance)
    {
        var account = store.Reset(Guid.NewGuid(), $"LAB-{runId:N}"[..12], openingBalance);
        return (account.Id, account.Version);
    }

    private async Task<(Guid AccountId, long StartingVersion)> SeedPostgresAccountAsync(
        Guid runId,
        decimal openingBalance,
        CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var account = new Account
        {
            Id = Guid.NewGuid(),
            AccountNumber = $"LAB-{runId:N}"[..16],
            Owner = "Lab run",
            Balance = openingBalance,
        };

        db.Accounts.Add(account);
        await db.SaveChangesAsync(cancellationToken);

        // Postgres assigns xmin on INSERT, so the starting version is whatever
        // transaction created the row — not a fixed number.
        return (account.Id, (long)account.Version);
    }

    private async Task<decimal> ReadPostgresBalanceAsync(Guid accountId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Accounts.AsNoTracking().Where(a => a.Id == accountId).Select(a => a.Balance)
            .SingleAsync(cancellationToken);
    }
}
