namespace Bank.Api.Shared.Persistence;

/// <summary>
/// Proof that a given operation has already been performed, keyed by a client-supplied
/// idempotency key.
/// </summary>
/// <remarks>
/// The primary key is the idempotency key itself, which is the entire mechanism: the
/// database's uniqueness constraint is what makes "only once" true even when several
/// instances process duplicate requests at the same instant. Checking "does this key
/// exist?" in application code and then inserting would just be the read-modify-write
/// race from lesson 01 wearing a different hat.
/// </remarks>
public class IdempotencyRecord
{
    public string Key { get; set; } = default!;
    public string Operation { get; set; } = default!;
    public string ResponseJson { get; set; } = default!;
    public DateTimeOffset CreatedAt { get; set; }
}
