namespace Bank.Api.Shared.Contention;

/// <summary>Everything the calculator needs that is not in the event timeline.</summary>
public sealed record RunFacts
{
    public required Guid RunId { get; init; }
    public required string Strategy { get; init; }
    public required Guid AccountId { get; init; }
    public required decimal StartingBalance { get; init; }
    public required long StartingVersion { get; init; }
    public required decimal FinalBalance { get; init; }
    public required double DurationMs { get; init; }

    /// <summary>What each actor was told, keyed by actor id.</summary>
    public required IReadOnlyDictionary<string, bool> Approvals { get; init; }
}

/// <summary>
/// Turns a timeline into a verdict: who won, who was refused, and who was told "yes"
/// while their money quietly evaporated.
/// </summary>
/// <remarks>
/// <para><b>How "who won" is actually decided.</b> Naively you might compare each
/// actor's written balance with the final one, but that falls apart the moment two
/// actors write the same value. Instead we replay the writes using the version each
/// actor <i>read</i>.</para>
/// <para>Every write records the version it was computed from. A write therefore
/// carries forward exactly the set of deductions that were already reflected in that
/// version, plus its own. If an actor read a stale version, its write carries forward
/// the stale set — silently erasing every deduction made in between.</para>
/// <para>Whoever is not in the final set, yet was told "approved", is a silent loser.
/// This works unchanged for the in-memory counter and for Postgres <c>xmin</c>, because
/// it only needs version values to be unique per write, never ordered.</para>
/// </remarks>
public static class RunSummaryCalculator
{
    public static RunSummary Calculate(RunFacts facts, IReadOnlyList<ContentionEvent> events)
    {
        var ordered = events.OrderBy(e => e.Sequence).ToArray();

        // version value -> the set of actors whose deduction is reflected in it
        var reflectedAt = new Dictionary<long, HashSet<string>>
        {
            [facts.StartingVersion] = [],
        };

        var lastVersionWritten = facts.StartingVersion;

        foreach (var write in ordered.Where(e => e.Phase == ContentionPhase.Committed))
        {
            var readFrom = write.VersionSeen ?? lastVersionWritten;
            var carriedForward = reflectedAt.TryGetValue(readFrom, out var set)
                ? new HashSet<string>(set)
                : [];

            carriedForward.Add(write.ActorId);

            // A write with no recorded version still advances the chain, so synthesise
            // one rather than losing the link.
            var resultVersion = write.VersionWritten ?? readFrom + 1;
            while (reflectedAt.ContainsKey(resultVersion))
            {
                resultVersion++;
            }

            reflectedAt[resultVersion] = carriedForward;
            lastVersionWritten = resultVersion;
        }

        var reflected = reflectedAt[lastVersionWritten];

        var outcomes = facts.Approvals.Keys
            .OrderBy(a => a, StringComparer.Ordinal)
            .Select(actorId => BuildOutcome(actorId, facts, ordered, reflected))
            .ToArray();

        var approvedTotal = outcomes.Where(o => o.ToldItSucceeded).Sum(o => o.Amount);

        return new RunSummary
        {
            RunId = facts.RunId,
            Strategy = facts.Strategy,
            AccountId = facts.AccountId,
            StartingBalance = facts.StartingBalance,
            FinalBalance = facts.FinalBalance,
            ExpectedBalance = facts.StartingBalance - approvedTotal,
            OverdrawnBeyondLimit = facts.FinalBalance < 0m,
            Outcomes = outcomes,
            DurationMs = facts.DurationMs,
        };
    }

    private static ActorOutcome BuildOutcome(
        string actorId,
        RunFacts facts,
        IReadOnlyList<ContentionEvent> ordered,
        IReadOnlySet<string> reflected)
    {
        var mine = ordered.Where(e => e.ActorId == actorId).ToArray();
        var approved = facts.Approvals[actorId];
        var landed = approved && reflected.Contains(actorId);

        var amount = mine.FirstOrDefault(e => e.Amount.HasValue)?.Amount ?? 0m;
        var attempts = mine.Length == 0 ? 1 : mine.Max(e => e.Attempt);
        var waited = mine.Where(e => e.WaitedMs.HasValue).Sum(e => e.WaitedMs!.Value);

        var duration = mine.Length switch
        {
            0 => 0d,
            _ => mine[^1].ElapsedMs - mine[0].ElapsedMs,
        };

        var finalPhase = (approved, landed) switch
        {
            (true, true) => ContentionPhase.Won,
            (true, false) => ContentionPhase.LostUpdate,
            _ => ContentionPhase.Rejected,
        };

        var note = finalPhase switch
        {
            ContentionPhase.LostUpdate =>
                "Told 'approved', but another actor's write erased this deduction. The customer has the money; the bank has no record of it.",
            ContentionPhase.Won when attempts > 1 =>
                $"Succeeded on attempt {attempts} after losing {attempts - 1} conflict(s).",
            ContentionPhase.Won => "Succeeded; the deduction stands in the final balance.",
            _ => mine.LastOrDefault(e => e.Phase == ContentionPhase.Rejected)?.Note ?? "Refused.",
        };

        return new ActorOutcome
        {
            ActorId = actorId,
            FinalPhase = finalPhase,
            ToldItSucceeded = approved,
            ActuallyLanded = landed,
            Amount = amount,
            Attempts = attempts,
            WaitedMs = waited,
            DurationMs = duration,
            Note = note,
        };
    }
}
