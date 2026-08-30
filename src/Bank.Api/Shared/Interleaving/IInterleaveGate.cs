namespace Bank.Api.Shared.Interleaving;

/// <summary>Named points in a handler where a lab run may hold every actor.</summary>
public static class InterleaveCheckpoints
{
    /// <summary>
    /// Reached after an actor has read the balance and before it writes. Holding every
    /// actor here guarantees they all read the same value, which is what makes a lost
    /// update reproduce every single time instead of occasionally.
    /// </summary>
    public const string AfterRead = "after-read";

    /// <summary>
    /// Reached before an actor asks for a lock. Holding everyone here guarantees they
    /// all contend for the lock at once.
    /// </summary>
    /// <remarks>
    /// A locking handler must gate <i>before</i> acquiring, never after. Gating inside
    /// the critical section deadlocks by construction: the one actor holding the lock
    /// waits at the barrier for actors who cannot get in until it releases. That is a
    /// real deadlock — a cycle of "I hold what you need and need what you hold" — and
    /// it happened while building this lab, which is why the gate also has a timeout.
    /// </remarks>
    public const string BeforeAcquire = "before-acquire";
}

/// <summary>
/// Lets a lab run control where concurrent actors meet.
/// </summary>
/// <remarks>
/// <para>A race that depends on luck is useless for both teaching and testing: it
/// reproduces on one machine and not another, and a test asserting it becomes flaky.
/// This seam makes the interleaving a parameter instead of an accident.</para>
/// <para>Outside a forced-race run this does nothing at all — see
/// <see cref="LabInterleaveGate"/>. ADR-0008 covers why a hook like this is allowed to
/// live in production code and what it would take to remove it.</para>
/// </remarks>
public interface IInterleaveGate
{
    Task ReachAsync(Guid runId, string checkpoint, CancellationToken cancellationToken = default);
}
