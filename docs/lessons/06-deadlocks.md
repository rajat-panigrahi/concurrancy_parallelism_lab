# 06 — Deadlocks: when two correct operations stop each other

**Code:** `src/Bank.Api/Features/Transfers/DeadlockTransfer/`
**Tests:** `tests/Bank.Api.IntegrationTests/Features/Transfers/DeadlockTransferTests.cs`

## Run it

```bash
curl -X POST localhost:5080/api/lab/deadlock -H 'Content-Type: application/json' \
  -d '{"orderLocks":false,"amount":50,"holdBetweenLocksMs":100}'
```

```
deadlockDetected: True
  alice->bob   ok=False deadlocked=True   Deadlock detected; this transaction was rolled back.
  bob->alice   ok=True  deadlocked=False  Transferred.
money before/after: 1000.0 / 1000.0
```

Now the same call with `"orderLocks": true`:

```
deadlockDetected: False
  alice->bob   ok=True  deadlocked=False  Transferred.
  bob->alice   ok=True  deadlocked=False  Transferred.
```

Same work, same two locks, same contention. **One changed line.**

## What happened

A transfer needs two accounts locked at once. Alice sends ₹50 to Bob while Bob sends
₹50 to Alice:

```
alice->bob:  lock Alice ✔ ... now wants Bob   ← held by the other transaction
bob->alice:  lock Bob   ✔ ... now wants Alice ← held by the other transaction
```

Each holds what the other needs. Neither will let go, because neither can finish. This
is a **circular wait**, and no amount of waiting resolves it.

Note that both operations are individually correct. Neither has a bug. The deadlock is
a property of the *pair*, which is what makes it hard to catch in review.

## The four conditions

A deadlock needs all four (Coffman conditions). Break any one and it becomes impossible:

| Condition | Present here | Could we break it? |
|---|---|---|
| **Mutual exclusion** — a lock is exclusive | Yes, row locks | No, that's the point of locking |
| **Hold and wait** — hold one, request another | Yes | Yes: take all locks at once, or none |
| **No preemption** — locks aren't forcibly taken | Yes | The database does this *for* us, by killing a victim |
| **Circular wait** — a cycle in the wait graph | Yes | **Yes, cheaply — order the locks** |

Breaking the circular wait is nearly always the right fix, because it costs one line
and no throughput.

## The fix

```csharp
var (firstLock, secondLock) =
    command.OrderLocks && command.ToAccountId.CompareTo(command.FromAccountId) < 0
        ? (command.ToAccountId, command.FromAccountId)
        : (command.FromAccountId, command.ToAccountId);
```

Sort the two ids and always lock the lower one first.

Now *every* transaction in the system reaches for the same account first, so a cycle
cannot form: whoever gets the lower id wins and proceeds; the other waits once and then
gets both. **The order is arbitrary — it only has to be consistent.** Sorting by primary
key is the usual choice because it's stable and needs no coordination.

This generalises to any number of locks: acquire in a globally consistent order and
circular wait is impossible.

## Postgres does you a favour

PostgreSQL detects the cycle and kills one transaction with SQLSTATE `40P01`:

```csharp
catch (PostgresException ex) when (ex.SqlState == DeadlockSqlState)
```

The victim's work is rolled back **entirely** — a test asserts money before equals money
after. So a database deadlock costs you a **failed request**, not corruption.

Compare that to lesson 01's lost update, which cost ₹400 and produced no error at all.
A deadlock is loud, immediate and safe. The silent bug is the dangerous one, even
though the deadlock is scarier to look at.

> **In-process locks give you no such favour.** Two C# `SemaphoreSlim`s in a cycle just
> hang forever, with no detection and no timeout unless you added one. That's a real
> argument for pushing coordination into the database.

## Handling a deadlock in production

Because you cannot eliminate every one:

1. **Order your locks** — removes the vast majority.
2. **Keep transactions short** — narrower windows, fewer cycles.
3. **Retry the victim.** A deadlock rollback is safe to retry: nothing was committed.
   Retry with jittered backoff, bounded, and only on `40P01` (SQL Server: 1205) — never
   blanket-retry every exception.
4. **Touch tables in a consistent order** across the whole codebase, not just within one
   method. Deadlocks form *between* code paths, so one handler that locks
   accounts-then-ledger while another locks ledger-then-accounts is enough.
5. **Alert on the rate.** A few is normal. A rising rate means a lock-ordering bug or a
   transaction that grew too long.

## Why they are hard to test

Note the `holdBetweenLocksMs` parameter. With it at 0 the two transactions often don't
overlap and no deadlock happens — the endpoint even says so:

> *"No deadlock this time — the two transactions happened not to overlap. That is
> exactly what makes deadlocks so hard to catch in testing."*

Widening the window makes it reliable. Same trick as the interleaving gate in lesson 01:
**make the timing a parameter instead of an accident.** The test then asserts the fix
works across five consecutive runs, because a fix for an intermittent bug has to be
proven repeatedly.

## Trade-offs

**Ordered locking** costs essentially nothing — a comparison — and removes an entire
class of failure. There is no real argument against it. The catch is that it's a
*convention*: nothing in the compiler or the database enforces it, so one new code path
that takes locks in a different order reintroduces the bug. It has to be a rule the team
knows, ideally written down.

The deeper trade-off is that all of this is a cost of **pessimistic locking**.
Optimistic concurrency has no deadlocks at all — no locks, no cycles. That's a genuine
point in its favour that lesson 05's table doesn't capture: pessimistic buys you no
wasted work, and charges you deadlock risk.

## What an interviewer asks next

*"What's the difference between a deadlock and a livelock?"* — A deadlock is stuck and
doing nothing. A **livelock** is busy and achieving nothing — two actors politely
retrying forever, each backing off into the other. Optimistic concurrency's retry storm
is a livelock risk; that's why lesson 03's retries are bounded.

*"What about starvation?"* — A third failure: one actor keeps losing while others make
progress. Common with optimistic retries under load, where the unlucky actor is always
last. Fairness (a queue) fixes it; retries don't.

*"How would you debug one in production?"* — Postgres logs the full wait graph on
detection; SQL Server gives you deadlock graphs in Extended Events. Both name the two
statements involved, which usually points straight at the inconsistent lock ordering.

*"Can optimistic concurrency deadlock?"* — No. No locks, no cycles. It trades that for
livelock and starvation risk instead.
