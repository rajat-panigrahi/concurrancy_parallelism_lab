# 03 — Optimistic concurrency: let everyone try, reject the stale

**Code:** `src/Bank.Api/Features/Withdrawals/OptimisticWithdraw/`
**Tests:** `tests/Bank.Api.IntegrationTests/Features/Withdrawals/OptimisticWithdrawTests.cs`

## The idea in one sentence

> Assume collisions are rare. Let everyone read and write at full speed, have the
> database refuse any write whose row changed since it was read, and make the loser
> start over.

Nobody waits for anybody. The price of a collision is **wasted work**, not waiting.

## Run it

```bash
curl -X POST localhost:5080/api/lab/runs -H 'Content-Type: application/json' \
  -d '{"strategy":"optimistic","actors":5,"amountEach":100,"startingBalance":100,"forceRace":true}'
```

```
final=0  expected=0  conserved=True
winners=1  silentLosers=0  rejected=4
attempts=9  conflicts=4  waited=0.0ms
```

Read those last two numbers together — they are the whole personality of optimistic
concurrency:

- **conflicts=4, attempts=9** — five actors did nine units of work to perform one
  withdrawal. Four of them worked for nothing and had to redo it.
- **waited=0.0ms** — nobody blocked anybody. Not for a microsecond.

## How it works

One line of configuration does it, in `BankDbContext`:

```csharp
account.Property(a => a.Version)
    .HasColumnName("xmin")
    .HasColumnType("xid")
    .ValueGeneratedOnAddOrUpdate()
    .IsConcurrencyToken();
```

`xmin` is a PostgreSQL **system column**: the id of the transaction that last wrote the
row. Postgres bumps it on every UPDATE for free, so you get a version counter with no
extra column and no code to maintain it.

Marking it a concurrency token makes EF Core append it to every UPDATE:

```sql
UPDATE "Accounts" SET "Balance" = 0 WHERE "Id" = '...' AND xmin = 746;
```

If somebody wrote first, `xmin` is no longer 746, the statement matches **zero rows**,
and EF raises `DbUpdateConcurrencyException`.

That's the entire mechanism. There's no lock anywhere.

## The retry loop

```csharp
for (var attempt = 1; attempt <= MaxAttempts; attempt++)
{
    await using var db = await dbContextFactory.CreateDbContextAsync(ct);   // fresh context
    var account = await db.Accounts.SingleAsync(...);                       // fresh read
    if (account.Balance < command.Amount) return Rejected(...);
    account.Balance -= command.Amount;
    try { await db.SaveChangesAsync(ct); return Approved(...); }
    catch (DbUpdateConcurrencyException) { /* someone won; go round again */ }
}
```

Three details that are easy to get wrong:

**1. A fresh `DbContext` per attempt.** Reusing it keeps the stale entity in the change
tracker, so the retry re-sends the same doomed UPDATE, conflicts again, and loops
forever. This is the most common bug in hand-written EF retry loops.

**2. Re-read *and* re-decide.** It is not enough to re-send the write. The balance may
now be too low, so the whole decision must be made again against fresh data. In the run
above, that's why four actors end up `rejected` rather than looping: they re-read, saw
₹0, and correctly refused.

**3. Retries are bounded.** An unbounded retry loop under heavy contention is a
**livelock** — everyone keeps colliding, keeps retrying, and the system does nothing but
burn CPU while making no progress. It is not a deadlock (nothing is blocked; everything
is busy) which makes it harder to spot. Giving up after N and telling the caller is the
honest outcome.

> In production, add **jittered exponential backoff** between attempts. Retrying
> immediately means the same actors collide again in the same order. Polly does this
> well; the principle is more important than the library.

## Why the losers are not silent

This is the contrast with lesson 01 worth holding onto:

| | Naive | Optimistic |
|---|---|---|
| Loser's write | Silently overwrote the winner | Refused by the database |
| Loser was told | "Approved" | "Insufficient funds" (after re-reading) |
| Money conserved | **No** — ₹400 gone | **Yes** |
| Detection | An auditor, months later | Immediately, in-process |

`DbUpdateConcurrencyException` is not a failure of the system — it is the system
working. It means the database just stopped you corrupting data.

## Where it breaks down

Optimistic concurrency degrades as contention rises, and it degrades in a specific way:
correctness holds, throughput collapses.

```
5 actors  → 9 attempts   (1.8× work)
24 actors → 40+ attempts (retry storm)
```

There's a test for exactly this
(`RetriesAreBounded_SoHighContentionFailsLoudlyRatherThanSpinningForever`). The point it
pins down: **optimistic concurrency does not fail by corrupting data. It fails by doing
lots of work and refusing some callers.** That's a far better failure mode, but it is
still a failure mode.

The killer case is a **hot row** — one account everyone hits, a global counter, a
shared inventory row. There every write conflicts with every other, and you are doing
N× the work to achieve 1× the throughput. That's when you switch to lesson 04.

## Portability: `xmin` vs `rowversion`

If you move to SQL Server, the concept is identical and the code changes slightly:

| | PostgreSQL | SQL Server |
|---|---|---|
| Token | `xmin` (system column) | `rowversion` / `timestamp` |
| Schema cost | **None** — already there | An 8-byte column you add |
| EF mapping | `.HasColumnName("xmin").IsConcurrencyToken()` | `[Timestamp]` or `.IsRowVersion()` |
| Type | `uint` | `byte[]` |

Reasoning in [ADR-0006](../architecture/adr/0006-xmin-concurrency-token.md).

A third option works anywhere: **your own `int Version` column**, incremented in code.
More portable, and easier to get wrong — you must remember to bump it on every write
path, and any code that forgets silently disables the protection.

## Trade-offs

**You gain:** no locks held across requests, so it scales horizontally without any
coordination infrastructure; no blocking, so one slow actor cannot stall others; and
it is safe across *any* number of instances, because the check lives in the database.

**You give up:** wasted work under contention, retry logic in every write path (which
is real complexity you must get right), unbounded latency for the unlucky actor that
keeps losing, and a failure mode that only appears under load — so it will look
perfect in testing.

**When it's right:** the default for web APIs. Short transactions, stateless
instances, conflicts genuinely rare. Also the *only* option when there's user think
time — you cannot hold a database lock while someone stares at an edit form.

## What an interviewer asks next

*"What happens if the retry keeps failing?"* — You bound it and surface the failure.
Then mention livelock by name, and jittered backoff as the mitigation.

*"How does this scale to three replicas?"* — Unchanged, and that's the point. The check
is in the database, which all instances share. Contrast with lesson 02's in-process
lock, which silently stops working.

*"Isn't `DbUpdateConcurrencyException` just an error you swallow?"* — No: swallowing it
without re-reading is a bug that produces the lost update you were trying to prevent.
The exception is the signal to *redo the decision*, not to retry the write.

*"What if the row is written by something outside EF?"* — Still safe, because `xmin` is
maintained by Postgres itself, not by EF. A hand-written `UPDATE` from a script bumps it
too. That's a genuine advantage over an application-maintained `Version` column, which
a raw SQL script will happily forget.
