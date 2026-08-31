namespace Bank.Api.Features.Transfers.DeadlockTransfer;

public sealed record TransferCommand
{
    public required Guid RunId { get; init; }
    public required string ActorId { get; init; }
    public required Guid FromAccountId { get; init; }
    public required Guid ToAccountId { get; init; }
    public required decimal Amount { get; init; }

    /// <summary>
    /// When true, the two row locks are taken in a globally consistent order rather
    /// than in "from, then to" order. This one flag is the difference between code that
    /// deadlocks and code that does not.
    /// </summary>
    public bool OrderLocks { get; init; }

    /// <summary>Widens the window between the two lock acquisitions.</summary>
    public TimeSpan HoldBetweenLocks { get; init; } = TimeSpan.FromMilliseconds(50);
}

public sealed record TransferResult
{
    public required string ActorId { get; init; }
    public required bool Succeeded { get; init; }
    public required bool Deadlocked { get; init; }
    public required string Reason { get; init; }
}

public sealed record DeadlockDemoRequest
{
    /// <summary>Take the locks in a consistent order — the fix.</summary>
    public bool OrderLocks { get; init; }

    public decimal Amount { get; init; } = 50m;
    public int HoldBetweenLocksMs { get; init; } = 50;
}

public sealed record DeadlockDemoResponse
{
    public required Guid RunId { get; init; }
    public required bool OrderLocks { get; init; }
    public required bool DeadlockDetected { get; init; }
    public required IReadOnlyList<TransferResult> Results { get; init; }
    public required decimal TotalMoneyBefore { get; init; }
    public required decimal TotalMoneyAfter { get; init; }
    public required string Verdict { get; init; }
}
