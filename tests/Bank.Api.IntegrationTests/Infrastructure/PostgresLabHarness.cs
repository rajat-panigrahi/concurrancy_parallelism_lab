using Bank.Api.Features.Withdrawals.OptimisticWithdraw;
using Bank.Api.Features.Withdrawals.PessimisticWithdraw;
using Bank.Api.Shared.Contention;
using Bank.Api.Shared.Interleaving;
using Bank.Api.Shared.Persistence;
using Bank.Api.Shared.Withdrawals;
using Microsoft.EntityFrameworkCore;

namespace Bank.Api.IntegrationTests.Infrastructure;

/// <summary>
/// The database equivalent of the unit tests' <c>LabHarness</c>: seeds a real account,
/// runs N actors at it, and returns the verdict.
/// </summary>
public sealed class PostgresLabHarness(PostgresFixture fixture)
{
    private readonly ContentionRecorder _recorder = new();
    private readonly LabInterleaveGate _gate = new();

    private IDbContextFactory<BankDbContext> Factory { get; } = new TestDbContextFactory(fixture.ConnectionString);

    public IWithdrawStrategy Strategy(string name) => name switch
    {
        "optimistic" => new OptimisticWithdrawHandler(Factory, _recorder, _gate),
        "pessimistic" => new PessimisticWithdrawHandler(Factory, _recorder, _gate),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown strategy."),
    };

    public IReadOnlyList<ContentionEvent> Events(Guid runId) => _recorder.EventsFor(runId);

    public async Task<RunSummary> RunAsync(
        string strategyName,
        int actors,
        decimal amountEach,
        decimal startingBalance,
        bool forceRace,
        TimeSpan? thinkTime = null)
    {
        var strategy = Strategy(strategyName);
        var runId = Guid.NewGuid();

        var (accountId, startingVersion) = await SeedAsync(runId, startingBalance);

        _recorder.BeginRun(runId);

        using var forcing = forceRace ? _gate.ForceRace(runId, actors) : null as IDisposable;

        var commands = Enumerable.Range(1, actors).Select(i => new WithdrawCommand
        {
            RunId = runId,
            ActorId = $"actor-{i}",
            AccountId = accountId,
            Amount = amountEach,
            ThinkTime = thinkTime ?? TimeSpan.Zero,
        }).ToArray();

        var started = _recorder.ElapsedMs(runId);

        var results = await Task.WhenAll(commands.Select(async command =>
        {
            var result = await strategy.WithdrawAsync(command);
            return (command.ActorId, result.Approved);
        }));

        var duration = _recorder.ElapsedMs(runId) - started;

        await using var db = fixture.NewDbContext();
        var finalBalance = await db.Accounts.AsNoTracking()
            .Where(a => a.Id == accountId).Select(a => a.Balance).SingleAsync();

        return RunSummaryCalculator.Calculate(
            new RunFacts
            {
                RunId = runId,
                Strategy = strategyName,
                AccountId = accountId,
                StartingBalance = startingBalance,
                StartingVersion = startingVersion,
                FinalBalance = finalBalance,
                DurationMs = duration,
                Approvals = results.ToDictionary(r => r.ActorId, r => r.Approved),
            },
            _recorder.EventsFor(runId));
    }

    public async Task<(Guid AccountId, long Version)> SeedAsync(Guid runId, decimal openingBalance)
    {
        await using var db = fixture.NewDbContext();

        var account = new Account
        {
            Id = Guid.NewGuid(),
            AccountNumber = $"T-{runId:N}"[..16],
            Owner = "Integration test",
            Balance = openingBalance,
        };

        db.Accounts.Add(account);
        await db.SaveChangesAsync();

        return (account.Id, (long)account.Version);
    }

    private sealed class TestDbContextFactory(string connectionString) : IDbContextFactory<BankDbContext>
    {
        public BankDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<BankDbContext>().UseNpgsql(connectionString).Options);
    }
}
