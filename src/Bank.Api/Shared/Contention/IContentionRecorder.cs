namespace Bank.Api.Shared.Contention;

/// <summary>
/// Collects the timeline of a lab run. Handlers call <see cref="Record"/> at each
/// step; the answer to "who won" is derived from what they recorded.
/// </summary>
public interface IContentionRecorder
{
    /// <summary>
    /// Appends an event. Must never block or throw — it is called from the middle of
    /// the code under observation, and an observer that changes the timing of what it
    /// observes is worse than no observer.
    /// </summary>
    void Record(ContentionEvent contentionEvent);

    /// <summary>Starts a run's clock and buffer.</summary>
    void BeginRun(Guid runId);

    /// <summary>Milliseconds since <see cref="BeginRun"/>.</summary>
    double ElapsedMs(Guid runId);

    /// <summary>Next global sequence number for the run.</summary>
    long NextSequence(Guid runId);

    IReadOnlyList<ContentionEvent> EventsFor(Guid runId);
}
