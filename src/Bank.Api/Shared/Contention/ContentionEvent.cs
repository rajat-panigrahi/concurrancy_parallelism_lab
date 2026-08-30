namespace Bank.Api.Shared.Contention;

/// <summary>What an actor was doing at one instant of a lab run.</summary>
public enum ContentionPhase
{
    /// <summary>The actor began its attempt.</summary>
    Started,

    /// <summary>The actor read the balance. <c>BalanceSeen</c> and <c>VersionSeen</c> say what it saw.</summary>
    Read,

    /// <summary>The actor is parked at a forced-race checkpoint.</summary>
    Gate,

    /// <summary>The actor asked for a lock and is waiting for it.</summary>
    LockWait,

    /// <summary>The actor got the lock. <c>WaitedMs</c> says how long it queued.</summary>
    LockAcquired,

    /// <summary>The actor issued its write.</summary>
    Write,

    /// <summary>The write landed.</summary>
    Committed,

    /// <summary>The write was refused because somebody else wrote first.</summary>
    Conflict,

    /// <summary>The actor is trying again after a conflict.</summary>
    Retry,

    /// <summary>The actor's money movement stands in the final balance.</summary>
    Won,

    /// <summary>
    /// The actor was told it succeeded, but its write was overwritten by someone else.
    /// This is the phase the whole project exists to make visible.
    /// </summary>
    LostUpdate,

    /// <summary>The actor was correctly refused — insufficient funds, or out of retries.</summary>
    Rejected,

    /// <summary>Something went wrong that is not a concurrency outcome.</summary>
    Failed,
}

/// <summary>
/// One line in the story of a contended account. The UI draws these as a swimlane per
/// actor; the run summary is computed from them.
/// </summary>
public sealed record ContentionEvent
{
    public required Guid RunId { get; init; }
    public required string ActorId { get; init; }
    public required Guid AccountId { get; init; }

    /// <summary>Global ordering within the run, assigned by the recorder.</summary>
    public required long Sequence { get; init; }

    public required ContentionPhase Phase { get; init; }

    /// <summary>Milliseconds since the run started. The x-axis of the timeline.</summary>
    public required double ElapsedMs { get; init; }

    /// <summary>Which attempt this is for this actor. 1 unless the actor retried.</summary>
    public int Attempt { get; init; } = 1;

    public decimal? BalanceSeen { get; init; }
    public long? VersionSeen { get; init; }
    public long? VersionWritten { get; init; }
    public decimal? Amount { get; init; }

    /// <summary>How long the actor spent blocked, for lock waits.</summary>
    public double? WaitedMs { get; init; }

    public string? Note { get; init; }
}
