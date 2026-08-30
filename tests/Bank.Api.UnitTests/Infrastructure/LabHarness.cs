using Bank.Api.Features.Withdrawals.LockWithdraw;
using Bank.Api.Features.Withdrawals.NaiveWithdraw;
using Bank.Api.Shared.Contention;
using Bank.Api.Shared.Interleaving;
using Bank.Api.Shared.Locking;
using Bank.Api.Shared.Persistence;
using Bank.Api.Shared.Withdrawals;

namespace Bank.Api.UnitTests.Infrastructure;

/// <summary>
/// Runs N actors against one account and returns the verdict, so a test can say what
/// it means rather than how to wire it.
/// </summary>
public sealed class LabHarness
{
    private readonly InMemoryAccountStore _store = new();
    private readonly ContentionRecorder _recorder = new();
    private readonly LabInterleaveGate _gate = new();
    private readonly InProcessAccountLock _lock = new();

    public IWithdrawStrategy Strategy(string name) => name switch
    {
        "naive" => new NaiveWithdrawHandler(_store, _recorder, _gate),
        "lock" => new LockWithdrawHandler(_store, _lock, _recorder, _gate),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown strategy."),
    };

    /// <summary>
    /// Every actor withdraws <paramref name="amountEach"/> from an account holding
    /// <paramref name="startingBalance"/>.
    /// </summary>
    /// <param name="forceRace">
    /// When true, every actor is held after its read until all have read. That turns a
    /// race that <i>might</i> happen into one that <i>always</i> happens, which is the
    /// difference between a test and a coin flip.
    /// </param>
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
        var account = _store.Open($"ACC-{runId:N}"[..12], startingBalance);

        _recorder.BeginRun(runId);

        using var forcing = forceRace
            ? _gate.ForceRace(runId, actors)
            : null as IDisposable;

        var commands = Enumerable.Range(1, actors)
            .Select(i => new WithdrawCommand
            {
                RunId = runId,
                ActorId = $"actor-{i}",
                AccountId = account.Id,
                Amount = amountEach,
                ThinkTime = thinkTime ?? TimeSpan.Zero,
            })
            .ToArray();

        var started = _recorder.ElapsedMs(runId);

        var results = await Task.WhenAll(commands.Select(async c =>
        {
            var result = await strategy.WithdrawAsync(c);
            return (c.ActorId, result);
        }));

        var duration = _recorder.ElapsedMs(runId) - started;
        var final = _store.Read(account.Id)!.Value;

        return RunSummaryCalculator.Calculate(
            new RunFacts
            {
                RunId = runId,
                Strategy = strategyName,
                AccountId = account.Id,
                StartingBalance = startingBalance,
                StartingVersion = 1,
                FinalBalance = final.Balance,
                DurationMs = duration,
                Approvals = results.ToDictionary(r => r.ActorId, r => r.result.Approved),
            },
            _recorder.EventsFor(runId));
    }
}
