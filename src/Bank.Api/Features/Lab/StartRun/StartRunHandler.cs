using Bank.Api.Shared.Contention;
using Bank.Api.Shared.Interleaving;
using Bank.Api.Shared.Persistence;
using Bank.Api.Shared.Withdrawals;

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

        var account = store.Reset(Guid.NewGuid(), $"LAB-{runId:N}"[..12], request.StartingBalance);

        recorder.BeginRun(runId);

        using var forcing = request.ForceRace ? gate.ForceRace(runId, actors) : null as IDisposable;

        var commands = Enumerable.Range(1, actors).Select(i => new WithdrawCommand
        {
            RunId = runId,
            ActorId = $"actor-{i}",
            AccountId = account.Id,
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
        var final = store.Read(account.Id)!.Value;

        var summary = RunSummaryCalculator.Calculate(
            new RunFacts
            {
                RunId = runId,
                Strategy = strategy.Name,
                AccountId = account.Id,
                StartingBalance = request.StartingBalance,
                StartingVersion = 1,
                FinalBalance = final.Balance,
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
}
