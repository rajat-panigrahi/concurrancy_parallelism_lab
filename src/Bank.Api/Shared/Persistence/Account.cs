namespace Bank.Api.Shared.Persistence;

/// <summary>
/// A bank account as stored in PostgreSQL.
/// </summary>
/// <remarks>
/// <para><b>Balance is <see cref="decimal"/>, never <c>double</c>.</b> Binary floating
/// point cannot represent 0.1 exactly, so money arithmetic drifts. This is a stock
/// interview question and the answer is always decimal for currency.</para>
/// <para><see cref="Version"/> maps to PostgreSQL's system column <c>xmin</c> — the id
/// of the transaction that last wrote the row. Postgres bumps it on every UPDATE for
/// free, so we get an optimistic concurrency token without maintaining a column
/// ourselves. See ADR-0006 for why this rather than a <c>rowversion</c>-style byte[].</para>
/// </remarks>
public class Account
{
    public Guid Id { get; set; }
    public string AccountNumber { get; set; } = default!;
    public string Owner { get; set; } = default!;
    public decimal Balance { get; set; }

    /// <summary>Postgres <c>xmin</c>. Read-only to us; the database maintains it.</summary>
    public uint Version { get; set; }
}
