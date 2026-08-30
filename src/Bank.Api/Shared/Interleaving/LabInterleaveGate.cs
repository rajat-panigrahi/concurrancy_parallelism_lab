using System.Collections.Concurrent;

namespace Bank.Api.Shared.Interleaving;

/// <summary>
/// The only <see cref="IInterleaveGate"/> implementation. It is a no-op unless a run
/// has explicitly registered itself as a forced race.
/// </summary>
/// <remarks>
/// Note the default: an unregistered run id — which is every ordinary API request —
/// returns an already-completed task without allocating or locking. The cost on the
/// normal path is one dictionary lookup.
/// </remarks>
public sealed class LabInterleaveGate : IInterleaveGate
{
    /// <summary>
    /// A barrier that never fills would hang the run forever. Rather than trust every
    /// handler to gate in a safe place, we cap the wait: past this the gate gives up
    /// forcing and lets the actor through. A demo that degrades to "unsynchronised" is
    /// recoverable; one that hangs is not.
    /// </summary>
    public static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(5);

    private sealed record Registration(int Participants, ConcurrentDictionary<string, AsyncBarrier> Barriers);

    private readonly ConcurrentDictionary<Guid, Registration> _runs = new();

    /// <summary>
    /// Makes every actor in <paramref name="runId"/> wait at each checkpoint until all
    /// <paramref name="participants"/> have arrived. Dispose to stop forcing.
    /// </summary>
    public IDisposable ForceRace(Guid runId, int participants)
    {
        _runs[runId] = new Registration(participants, new ConcurrentDictionary<string, AsyncBarrier>());
        return new RegistrationHandle(this, runId);
    }

    public Task ReachAsync(Guid runId, string checkpoint, CancellationToken cancellationToken = default)
    {
        if (!_runs.TryGetValue(runId, out var registration))
        {
            return Task.CompletedTask;
        }

        var barrier = registration.Barriers.GetOrAdd(
            checkpoint,
            _ => new AsyncBarrier(registration.Participants));

        return WaitOrGiveUpAsync(barrier, cancellationToken);
    }

    private static async Task WaitOrGiveUpAsync(AsyncBarrier barrier, CancellationToken cancellationToken)
    {
        try
        {
            await barrier.SignalAndWaitAsync().WaitAsync(MaxWait, cancellationToken);
        }
        catch (TimeoutException)
        {
            // Everyone still parked here is let go, so the run finishes unsynchronised
            // instead of hanging.
            barrier.Release();
        }
    }

    private void Stop(Guid runId)
    {
        if (!_runs.TryRemove(runId, out var registration))
        {
            return;
        }

        // An actor that failed before reaching the checkpoint would leave the others
        // waiting forever. Releasing on teardown turns a hang into a normal finish.
        foreach (var barrier in registration.Barriers.Values)
        {
            barrier.Release();
        }
    }

    private sealed class RegistrationHandle(LabInterleaveGate gate, Guid runId) : IDisposable
    {
        public void Dispose() => gate.Stop(runId);
    }
}
