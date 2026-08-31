using Bank.Api.Shared.Contention;

namespace Bank.Api.Features.Lab;

/// <summary>What to run. Every field maps to a control in the UI.</summary>
public sealed record StartRunRequest
{
    /// <summary>Strategy id: <c>naive</c>, <c>lock</c>, and later <c>optimistic</c>, <c>pessimistic</c>.</summary>
    public string Strategy { get; init; } = "naive";

    /// <summary>How many actors hit the same account at once.</summary>
    public int Actors { get; init; } = 5;

    public decimal AmountEach { get; init; } = 100m;

    public decimal StartingBalance { get; init; } = 100m;

    /// <summary>
    /// Hold every actor at its strategy's checkpoint so the contention is guaranteed
    /// rather than lucky. Turn this off to see how often the bug shows up by chance —
    /// which is the reason it survives testing in real systems.
    /// </summary>
    public bool ForceRace { get; init; } = true;

    /// <summary>Simulated work between reading and writing, in milliseconds.</summary>
    public int ThinkTimeMs { get; init; }
}

public sealed record StartRunResponse
{
    public required Guid RunId { get; init; }
    public required RunSummary Summary { get; init; }
    public required IReadOnlyList<ContentionEvent> Timeline { get; init; }
}

public sealed record StrategyInfo
{
    public required string Name { get; init; }
    public required string Storage { get; init; }
    public required string Summary { get; init; }
    public required bool ScalesAcrossInstances { get; init; }
}
