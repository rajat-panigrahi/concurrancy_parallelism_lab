# 04 — Pessimistic concurrency: take the lock, make them queue

**Code:** `src/Bank.Api/Features/Withdrawals/PessimisticWithdraw/`
**Tests:** `tests/Bank.Api.IntegrationTests/Features/Withdrawals/PessimisticWithdrawTests.cs`

## The idea in one sentence

> Assume collisions are common. Take a lock on the row before you read it, and make
> everyone else wait until you commit.

Nobody does wasted work. The price of a collision is **waiting**, not rework.

## Run it

```bash
curl -X POST localhost:5080/api/lab/runs -H 'Content-Type: application/json' \
  -d '{"strategy":"pessimistic","actors":5,"amountEach":100,"startingBalance":100,"forceRace":true}'
```

```
final=0  expected=0  conserved=True
winners=1  silentLosers=0  rejected=4
attempts=5  conflicts=0  waited=244.1ms
```

Now compare directly with the optimistic run on identical input:

| | Optimistic | Pessimistic |
|---|---|---|
| Attempts | **9** | **5** — one each, nothing redone |
| Conflicts | **4** | **0** — prevented, not detected |
| Total waiting | **0.0 ms** | **244.1 ms** |
| Correct | Yes | Yes |

Both are correct. They pay for it in different currencies. That table is the answer to
"which one should I use", and lesson 05 turns it into a decision.

## How it works

```csharp
await using var transaction = await db.Database.BeginTransactionAsync(ct);

var account = await db.Accounts
    .FromSql($"""SELECT *, xmin FROM "Accounts" WHERE "Id" = {command.AccountId} FOR UPDATE""")
    .SingleAsync(ct);

// ... check balance, subtract ...

await db.SaveChangesAsync(ct);
await transaction.CommitAsync(ct);
```

`SELECT … FOR UPDATE` takes a **row-level write lock**. Any other transaction asking for
the same row blocks until this one commits or rolls back.

Three things make it correct, and all three matter:

1. **It's inside a transaction.** A row lock is held until the transaction ends. Without
   `BeginTransaction` the lock is released immediately and protects nothing.
2. **The read is inside the lock.** Same rule as lesson 02 — a lock protects an
   invariant, not a statement. Lock, *then* read, decide and write.
3. **The lock is held until commit.** No window exists between the check and the write.

> **Why `SELECT *, xmin`?** `xmin` is a system column, so it isn't included in `SELECT *`,
> and EF needs every mapped column back. Easy to miss, and the error message is unhelpful.

**SQL Server equivalent:** `SELECT … FROM Accounts WITH (UPDLOCK, ROWLOCK) WHERE Id = @id`.

## What the waiting looks like

The test `TheRowLockActuallySerialises_TheWaitsGrowWithTheQueue` measures it. Four
actors, 40 ms of work each:

```
actor A:   0 ms wait  ────work 40ms────┐
actor B:  40 ms wait                   └────work 40ms────┐
actor C:  80 ms wait                                     └────work 40ms────┐
actor D: 120 ms wait                                                       └──work──
```

Each actor waits for everyone ahead of it. Total wall time is the **sum**, not the max.
That's what "serialised" means, and it's the cost you're signing up for.

> **Pessimistic coordination converts other people's latency into your own.**

## The rule that prevents outages

**Never hold a lock across I/O you don't control.**

```csharp
// Catastrophic
await using var tx = await db.Database.BeginTransactionAsync(ct);
var account = await LockAccountAsync(id);
await paymentProvider.ChargeAsync(...);   // 2 seconds, sometimes 30, sometimes never
await tx.CommitAsync(ct);
```

Every other request for that account now waits on a third party's availability. Under
load the connection pool fills with blocked transactions and the whole service stops —
not just this endpoint. This is one of the most common causes of "the database is
down" incidents that turn out not to be the database's fault.

Do the slow call **outside** the lock, then take the lock and re-validate.

## The cost you might not expect: deadlocks

The moment a handler needs **two** locks, you can deadlock. That's lesson 06, and it is
the main reason pessimistic locking is harder to operate than it looks.

## Where it breaks down

- **Long transactions.** Locks held for seconds destroy throughput. The queue grows
  faster than it drains, and latency goes vertical.
- **User think time.** You cannot hold a row lock while someone edits a form for five
  minutes. That's not a tuning problem; it's a design error. Use optimistic there.
- **Lock escalation** (more a SQL Server concern): lock enough rows and the engine may
  promote to a page or table lock, so you accidentally serialise far more than you meant.
- **Connection pool exhaustion.** Every blocked transaction holds a connection. Enough
  waiters and you run out of connections, and *unrelated* endpoints start failing.

## Trade-offs

**You gain:** no wasted work, no retry logic to write or get wrong, predictable
behaviour under high contention (a queue is at least fair), and correctness across any
number of instances — the lock lives in the database that everyone shares.

**You give up:** throughput (work serialises), latency predictability (your p99 is
somebody else's critical section), deadlock-freedom (once two locks are involved), and
a connection per waiter.

**When it's right:** hot rows where optimistic would retry-storm; operations where
redoing the work is expensive or has side effects; and anywhere "first come, first
served" is a business requirement rather than an implementation detail — seat booking,
inventory, ticket allocation.

## What an interviewer asks next

*"What's the difference between this and a `lock` statement in C#?"* — Scope. A C#
lock coordinates threads in **one process**; a database row lock coordinates
transactions across **every** process. Same idea, different blast radius, and the
difference is the whole scaling story (lesson 09).

*"What happens if the process dies holding the lock?"* — The database rolls the
transaction back when the connection drops, so the lock is released. That's a genuine
advantage over a hand-rolled distributed lock, where a dead holder can leave a lock
stuck until its TTL expires (ADR-0012).

*"Isolation levels?"* — Worth knowing that `SELECT … FOR UPDATE` is explicit locking and
mostly orthogonal to isolation level. Postgres defaults to Read Committed; `Serializable`
would also prevent the lost update, but by *aborting* transactions with a
serialization failure — which makes it optimistic in character, and you'd need a retry
loop again.

*"How do you avoid deadlocks?"* — Acquire locks in a consistent global order. That's
lesson 06, and it's one line of code.
