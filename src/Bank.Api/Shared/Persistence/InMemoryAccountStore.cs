using System.Collections.Concurrent;

namespace Bank.Api.Shared.Persistence;

/// <summary>A point-in-time copy of an account, as one actor saw it.</summary>
public readonly record struct AccountSnapshot(Guid Id, string AccountNumber, decimal Balance, long Version);

/// <summary>
/// The process-memory account store used by the teaching slices that need a race
/// with nothing else in the way — no EF change tracker, no transaction, no network.
/// Just read, think, write.
/// </summary>
/// <remarks>
/// <para><b>This type is deliberately not thread-safe at the account level.</b>
/// The <see cref="ConcurrentDictionary{TKey,TValue}"/> only makes the *lookup* safe;
/// it does nothing for the read-modify-write sequence a caller performs across
/// <see cref="Read"/> and <see cref="Write"/>. That gap is the entire lesson, and
/// removing it would delete the bug we are trying to show.</para>
/// <para>A concurrent collection protects its own internals, never your business
/// invariant. Interviewers ask this one a lot: "we used ConcurrentDictionary, so
/// we're thread-safe, right?" — no, not if you read a value, decide something, and
/// write it back.</para>
/// </remarks>
public sealed class InMemoryAccountStore
{
    private sealed class MutableAccount
    {
        public required Guid Id { get; init; }
        public required string AccountNumber { get; init; }
        public decimal Balance { get; set; }
        public long Version { get; set; }
    }

    private readonly ConcurrentDictionary<Guid, MutableAccount> _accounts = new();

    public AccountSnapshot Open(string accountNumber, decimal openingBalance)
    {
        var account = new MutableAccount
        {
            Id = Guid.NewGuid(),
            AccountNumber = accountNumber,
            Balance = openingBalance,
            Version = 1,
        };

        _accounts[account.Id] = account;
        return Snapshot(account);
    }

    /// <summary>Overwrites an account so a lab run can start from a known balance.</summary>
    public AccountSnapshot Reset(Guid id, string accountNumber, decimal openingBalance)
    {
        var account = new MutableAccount
        {
            Id = id,
            AccountNumber = accountNumber,
            Balance = openingBalance,
            Version = 1,
        };

        _accounts[id] = account;
        return Snapshot(account);
    }

    public AccountSnapshot? Read(Guid id) =>
        _accounts.TryGetValue(id, out var account) ? Snapshot(account) : null;

    /// <summary>
    /// Blind write. Whatever balance you hand in wins, regardless of what happened
    /// since you read. This is the lost update, in one method.
    /// </summary>
    /// <returns>The version this write produced, so a caller can record what it left behind.</returns>
    public long Write(Guid id, decimal newBalance)
    {
        if (!_accounts.TryGetValue(id, out var account))
        {
            throw new KeyNotFoundException($"Account {id} not found.");
        }

        // Not atomic, and deliberately so: the balance and the version are two
        // separate stores, and the caller decided `newBalance` long before now.
        account.Balance = newBalance;
        return ++account.Version;
    }

    /// <summary>
    /// Compare-and-swap write: succeeds only if nobody has written since the caller
    /// read <paramref name="expectedVersion"/>. This is optimistic concurrency
    /// implemented by hand, with no database involved.
    /// </summary>
    public bool TryWrite(Guid id, decimal newBalance, long expectedVersion, out long newVersion)
    {
        if (!_accounts.TryGetValue(id, out var account))
        {
            throw new KeyNotFoundException($"Account {id} not found.");
        }

        // Guarding the compare and the swap together is the whole point; doing them
        // as two separate statements would just move the race, not remove it.
        lock (account)
        {
            if (account.Version != expectedVersion)
            {
                newVersion = account.Version;
                return false;
            }

            account.Balance = newBalance;
            newVersion = ++account.Version;
            return true;
        }
    }

    public IReadOnlyCollection<AccountSnapshot> All() =>
        _accounts.Values.Select(Snapshot).ToArray();

    public void Clear() => _accounts.Clear();

    private static AccountSnapshot Snapshot(MutableAccount a) =>
        new(a.Id, a.AccountNumber, a.Balance, a.Version);
}
