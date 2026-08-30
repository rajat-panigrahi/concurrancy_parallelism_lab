using System.Diagnostics;
using Bank.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Bank.Api.Shared.Locking;

/// <summary>
/// A lock that lives in PostgreSQL rather than in this process's memory, so it keeps
/// working at any number of instances.
/// </summary>
/// <remarks>
/// <para><c>pg_advisory_lock(key)</c> takes a lock on an arbitrary 64-bit number rather
/// than on a row. Nothing in the database schema is involved — it is a pure mutual
/// exclusion primitive that every connected instance shares.</para>
/// <para><b>Why this scales and <see cref="InProcessAccountLock"/> does not:</b> the
/// semaphore version keeps its locks in a field, so three replicas have three separate
/// sets of locks and coordinate nothing. This version's lock lives in the one database
/// they all connect to. Same interface, same call site, and the difference between
/// "works on my machine" and "works in production".</para>
/// <para><b>Session-scoped, not transaction-scoped.</b> We use <c>pg_advisory_lock</c>
/// (held until explicitly unlocked or the connection closes) rather than
/// <c>pg_advisory_xact_lock</c> (released at transaction end), because the caller here
/// may not be inside a transaction. That makes releasing our responsibility, which is
/// why the handle is careful.</para>
/// <para><b>If the process dies, the lock is released</b> — Postgres drops session
/// advisory locks when the connection closes. That is a genuine advantage over a
/// hand-rolled Redis lock, where a dead holder leaves the lock stuck until its TTL
/// expires. See ADR-0012.</para>
/// </remarks>
public sealed class PostgresAdvisoryAccountLock(IDbContextFactory<BankDbContext> dbContextFactory) : IAccountLock
{
    public string Kind => "PostgreSQL advisory lock";

    public bool WorksAcrossInstances => true;

    public async Task<IAccountLockHandle> AcquireAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        // A dedicated context, because an advisory lock is tied to its *connection*.
        // Sharing a connection with other work would release the lock when that work
        // finished with the connection.
        var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var key = ToLockKey(accountId);

        var clock = Stopwatch.StartNew();

        try
        {
            await db.Database.OpenConnectionAsync(cancellationToken);
            await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_lock({key})", cancellationToken);
        }
        catch
        {
            await db.DisposeAsync();
            throw;
        }

        clock.Stop();

        return new Handle(db, key, clock.Elapsed.TotalMilliseconds);
    }

    /// <summary>
    /// Folds a Guid into the 64-bit key advisory locks use.
    /// </summary>
    /// <remarks>
    /// This is a hash, so two different accounts can collide and share a lock. The
    /// consequence is a little unnecessary blocking, never incorrectness — a false
    /// *shared* lock is safe; a false *distinct* lock would not be. Worth stating out
    /// loud, because "we hashed the key" is exactly the kind of detail that hides a bug.
    /// </remarks>
    public static long ToLockKey(Guid accountId)
    {
        Span<byte> bytes = stackalloc byte[16];
        accountId.TryWriteBytes(bytes);

        return BitConverter.ToInt64(bytes[..8]) ^ BitConverter.ToInt64(bytes[8..]);
    }

    private sealed class Handle(BankDbContext db, long key, double waitedMs) : IAccountLockHandle
    {
        private int _released;

        public double WaitedMs { get; } = waitedMs;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0)
            {
                return;
            }

            try
            {
                await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_unlock({key})");
            }
            finally
            {
                // Disposing the context closes the connection, which Postgres treats as
                // releasing every advisory lock it held. So even if the unlock above
                // fails, the lock does not leak.
                await db.DisposeAsync();
            }
        }
    }
}
