namespace Bank.Api.Shared.Withdrawals;

/// <summary>Which store a strategy coordinates through. Decides how far it scales.</summary>
public enum StorageKind
{
    /// <summary>Process memory. Correct on one instance; meaningless across several.</summary>
    InMemory,

    /// <summary>PostgreSQL. Shared by every instance, so coordination survives scale-out.</summary>
    Postgres,
}

public sealed record WithdrawCommand
{
    public required Guid RunId { get; init; }
    public required string ActorId { get; init; }
    public required Guid AccountId { get; init; }
    public required decimal Amount { get; init; }

    /// <summary>
    /// Simulated "think time" between reading the balance and writing it back —
    /// validating a card, calling a fraud service. Real systems always have a gap
    /// here; widening it just makes the race easier to hit.
    /// </summary>
    public TimeSpan ThinkTime { get; init; } = TimeSpan.Zero;
}

public sealed record WithdrawResult
{
    public required bool Approved { get; init; }

    /// <summary>The balance this actor believed it left behind.</summary>
    public required decimal BalanceAfter { get; init; }

    public required string Reason { get; init; }
    public int Attempts { get; init; } = 1;
    public double WaitedMs { get; init; }

    public static WithdrawResult Rejected(decimal balance, string reason, int attempts = 1) =>
        new() { Approved = false, BalanceAfter = balance, Reason = reason, Attempts = attempts };
}

/// <summary>
/// One way of performing a withdrawal. Each concurrency lesson is an implementation,
/// so the lab can run them all against identical input and compare the verdicts.
/// </summary>
/// <remarks>
/// This is the one contract shared across withdrawal slices. It exists because the
/// lab's whole value is comparing strategies side by side, which needs a common shape.
/// Everything else about a slice stays inside its own folder (ADR-0001).
/// </remarks>
public interface IWithdrawStrategy
{
    /// <summary>Stable id used in the API and the UI, e.g. <c>naive</c>.</summary>
    string Name { get; }

    StorageKind Storage { get; }

    /// <summary>One-line description of the mechanism, shown in the UI's comparison table.</summary>
    string Summary { get; }

    Task<WithdrawResult> WithdrawAsync(WithdrawCommand command, CancellationToken cancellationToken = default);
}
