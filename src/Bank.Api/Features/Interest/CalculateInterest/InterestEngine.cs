namespace Bank.Api.Features.Interest.CalculateInterest;

/// <summary>An account reduced to what the interest run needs. Cents, so the maths is exact.</summary>
public readonly record struct InterestAccount(int Id, long BalanceCents, int TierCode);

/// <summary>
/// The CPU-bound half of the lab. No database, no network, no waiting — just arithmetic
/// over a large array, which is the only kind of work parallelism actually speeds up.
/// </summary>
/// <remarks>
/// Everything here is deliberately compute-heavy and allocation-free. If the work
/// allocated or waited, the benchmark would be measuring the allocator or the network
/// rather than the parallelism.
/// </remarks>
public static class InterestEngine
{
    /// <summary>Monthly compounding over a year, plus a tier adjustment.</summary>
    /// <remarks>
    /// <see cref="Math.Pow"/> and <see cref="Math.Log"/> are here to make each item cost
    /// enough that the per-item work dominates the cost of scheduling it. Parallelising
    /// work that is cheaper than its own scheduling overhead makes things slower, which
    /// is one of the results the benchmark shows.
    /// </remarks>
    public static long ComputeInterestCents(in InterestAccount account)
    {
        var principal = account.BalanceCents / 100d;

        var baseRate = account.TierCode switch
        {
            0 => 0.0225d,
            1 => 0.0310d,
            2 => 0.0405d,
            _ => 0.0150d,
        };

        // A risk adjustment that varies per account, so the compiler cannot hoist it.
        var adjustment = Math.Log(1d + (account.Id % 997) / 1000d) * 0.004d;
        var rate = baseRate + adjustment;

        const int CompoundsPerYear = 12;
        var grown = principal * Math.Pow(1d + rate / CompoundsPerYear, CompoundsPerYear);

        return (long)Math.Round((grown - principal) * 100d);
    }

    /// <summary>Builds a deterministic portfolio, so every run measures the same work.</summary>
    public static InterestAccount[] BuildPortfolio(int count)
    {
        var accounts = new InterestAccount[count];

        for (var i = 0; i < count; i++)
        {
            accounts[i] = new InterestAccount(
                Id: i,
                BalanceCents: 10_000L + (i % 50_000) * 137L,
                TierCode: i % 4);
        }

        return accounts;
    }
}
