namespace Bank.Api.Shared.Contention;

/// <summary>The verdict for one actor.</summary>
public sealed record ActorOutcome
{
    public required string ActorId { get; init; }
    public required ContentionPhase FinalPhase { get; init; }
    public required bool ToldItSucceeded { get; init; }

    /// <summary>Whether this actor's money movement is reflected in the final balance.</summary>
    public required bool ActuallyLanded { get; init; }

    public decimal Amount { get; init; }
    public int Attempts { get; init; }
    public double WaitedMs { get; init; }
    public double DurationMs { get; init; }
    public string? Note { get; init; }
}

/// <summary>
/// The scoreboard for a run: what should have happened, what did, and who is
/// responsible for the difference.
/// </summary>
public sealed record RunSummary
{
    public required Guid RunId { get; init; }
    public required string Strategy { get; init; }
    public required Guid AccountId { get; init; }

    public required decimal StartingBalance { get; init; }
    public required decimal FinalBalance { get; init; }

    /// <summary>What the balance would be if every actor that was told "yes" really got paid.</summary>
    public required decimal ExpectedBalance { get; init; }

    /// <summary>
    /// <c>ExpectedBalance - FinalBalance</c>. Non-zero means the bank's books do not
    /// add up: money was created or destroyed by the interleaving.
    /// </summary>
    public decimal Discrepancy => ExpectedBalance - FinalBalance;

    public bool MoneyIsConserved => Discrepancy == 0m;

    /// <summary>True if the balance went below zero — the invariant a bank cannot break.</summary>
    public required bool OverdrawnBeyondLimit { get; init; }

    public required IReadOnlyList<ActorOutcome> Outcomes { get; init; }

    /// <summary>Actors whose write stands.</summary>
    public IReadOnlyList<ActorOutcome> Winners =>
        Outcomes.Where(o => o.ActuallyLanded).ToArray();

    /// <summary>
    /// Actors that were told "yes" and whose write was then overwritten. The customer
    /// got their money; the bank never recorded it. This is the punchline of the naive
    /// slice, and the number to point at in an interview.
    /// </summary>
    public IReadOnlyList<ActorOutcome> SilentLosers =>
        Outcomes.Where(o => o.ToldItSucceeded && !o.ActuallyLanded).ToArray();

    /// <summary>Actors correctly refused.</summary>
    public IReadOnlyList<ActorOutcome> Rejected =>
        Outcomes.Where(o => !o.ToldItSucceeded).ToArray();

    public int TotalAttempts => Outcomes.Sum(o => o.Attempts);
    public int Conflicts => Outcomes.Sum(o => o.Attempts - 1);
    public double TotalWaitedMs => Outcomes.Sum(o => o.WaitedMs);
    public required double DurationMs { get; init; }

    /// <summary>A one-line human verdict, shown on the UI's result card.</summary>
    public string Verdict => (MoneyIsConserved, OverdrawnBeyondLimit) switch
    {
        (false, _) => $"{SilentLosers.Count} actor(s) were told 'approved' but their money never left the books — {Math.Abs(Discrepancy):0.00} unaccounted for.",
        (true, true) => "The books balance, but the account went overdrawn — the guard was checked against a stale balance.",
        (true, false) => $"Correct. {Winners.Count} succeeded, {Rejected.Count} were refused, and every rupee is accounted for.",
    };
}
