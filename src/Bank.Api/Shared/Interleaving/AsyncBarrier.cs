namespace Bank.Api.Shared.Interleaving;

/// <summary>
/// A barrier that releases all participants once the expected number have arrived —
/// like <see cref="System.Threading.Barrier"/>, but awaitable.
/// </summary>
/// <remarks>
/// <para>The built-in <see cref="System.Threading.Barrier"/> blocks the calling thread
/// in <c>SignalAndWait</c>. Using it here would mean N thread-pool threads sitting
/// blocked waiting for each other — which is thread-pool starvation, the exact bug
/// <c>lessons/09-scaling.md</c> is about. Writing the async version is cheaper than
/// explaining why the lab causes the problem it teaches.</para>
/// <para>This is single-phase-per-generation: once the last participant arrives the
/// current generation completes and a fresh one begins, so the same barrier can be
/// reused for a later checkpoint.</para>
/// </remarks>
public sealed class AsyncBarrier(int participantCount)
{
    private readonly object _sync = new();
    private TaskCompletionSource _generation = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _arrived;

    public int ParticipantCount { get; } = participantCount > 0
        ? participantCount
        : throw new ArgumentOutOfRangeException(nameof(participantCount), "A barrier needs at least one participant.");

    /// <summary>Signals arrival and completes once every participant has arrived.</summary>
    public Task SignalAndWaitAsync()
    {
        TaskCompletionSource generation;
        var isLast = false;

        // The whole point is that incrementing the count and deciding whether we are
        // the last arrival must be one atomic step. Splitting them would reintroduce
        // the very race this type exists to control.
        lock (_sync)
        {
            generation = _generation;

            if (++_arrived == ParticipantCount)
            {
                _arrived = 0;
                _generation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                isLast = true;
            }
        }

        // Completed outside the lock: continuations must never run while we hold it.
        if (isLast)
        {
            generation.SetResult();
        }

        return generation.Task;
    }

    /// <summary>Releases everyone currently waiting, whether or not the barrier is full.</summary>
    public void Release()
    {
        TaskCompletionSource generation;

        lock (_sync)
        {
            generation = _generation;
            _arrived = 0;
            _generation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        generation.TrySetResult();
    }
}
