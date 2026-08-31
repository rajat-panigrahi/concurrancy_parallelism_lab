# 02 — Locking inside one process (and why it stops working)

**Code:** `src/Bank.Api/Features/Withdrawals/LockWithdraw/`, `src/Bank.Api/Shared/Locking/`
**Tests:** `tests/Bank.Api.UnitTests/Features/Withdrawals/LockWithdrawTests.cs`

## Run it

Identical input to lesson 01, one word changed:

```bash
curl -X POST localhost:5080/api/lab/runs -H 'Content-Type: application/json' \
  -d '{"strategy":"lock","actors":5,"amountEach":100,"startingBalance":100,"forceRace":true}'
```

```
final         : 0
discrepancy   : 0
conserved     : True
winners       : ['actor-4']
SILENT LOSERS : []
rejected      : 4
verdict       : Correct. 1 succeeded, 4 were refused, and every rupee is accounted for.
```

One winner, four honest refusals, books balanced.

## The fix

```csharp
await using var handle = await accountLock.AcquireAsync(command.AccountId, cancellationToken);

var snapshot = store.Read(command.AccountId);        // read INSIDE the lock
if (snapshot.Balance < command.Amount) return Rejected(...);
store.Write(command.AccountId, snapshot.Balance - command.Amount);
```

Two details do all the work, and both are easy to get wrong.

### 1. The read moves inside the lock

Locking only the write would be pointless — the stale value was already read. A lock
protects an **invariant**, not a statement. It has to span every step that assumes the
balance hasn't changed.

> Ask "what am I assuming stays true?" and lock that whole span.

### 2. The lock is per account, not global

```csharp
var semaphore = _locks.GetOrAdd(accountId, _ => new SemaphoreSlim(1, 1));
```

One lock for the whole bank would also be correct — and would put every customer in
the world in one queue. Lock granularity is the dial between correctness and
throughput:

| Granularity | Correct? | Throughput | Risk |
|---|---|---|---|
| One global lock | Yes | Terrible — everything serialises | None, but useless |
| Per account | Yes | Good — only same-account requests queue | Deadlock if you take two (lesson 06) |
| Per field | Yes | Best | Deadlock is now very likely |

Finer is faster and more dangerous. Per-entity is the usual sweet spot.

## Why `SemaphoreSlim` and not `lock`

**You cannot `await` inside a `lock` block.** The compiler forbids it, and it's a
favourite interview question.

```csharp
lock (_gate)
{
    await store.SaveAsync();   // ← does not compile
}
```

A `lock` is a *monitor*, and a monitor is owned by the **thread** that entered it. An
`await` may resume on a different thread, which would then try to release a lock it
never took. C# refuses rather than letting you corrupt the runtime's bookkeeping.

`SemaphoreSlim(1, 1)` is the async-aware answer — it's owned by nobody, so any
continuation can release it:

```csharp
await semaphore.WaitAsync(ct);
try { /* await freely */ }
finally { semaphore.Release(); }
```

The trade: a semaphore has no reentrancy and no ownership, so *you* must guarantee
every acquire has exactly one release. In this repo the handle guards double-release
with an `Interlocked.Exchange`, because releasing twice raises the count above its
maximum and silently lets two actors into the critical section — a bug that looks
exactly like having no lock at all.

> **.NET 9+:** there's now a `System.Threading.Lock` type — a real lock object rather
> than locking on `object`. It avoids the classic mistake of locking on something
> publicly reachable (`this`, a string, a `Type`), where unrelated code can lock the
> same instance and deadlock you. This repo targets .NET 8
> ([ADR-0003](../architecture/adr/0003-target-dotnet-8-lts.md)), so it's worth knowing
> but not used here. It still doesn't allow `await` inside.

## The cost: correctness is paid for in waiting

```csharp
[Fact]
public async Task ActorsQueue_SoTheCostOfCorrectnessIsWaiting()
```

Four actors, 25 ms of think time each, holding the lock throughout. Three of the four
queue behind someone; total waiting exceeds 50 ms. That's the deal you signed:

> **Pessimistic coordination converts other people's latency into your own.**

Which leads to the rule that matters in production:

**Never hold a lock across I/O you don't control.** If the critical section calls an
external payment provider that takes 2 seconds, every other request for that account
waits 2 seconds. Do the slow call *outside* the lock and re-validate inside it.

## And now the catch

This is correct on **exactly one instance**.

```csharp
private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();
```

That dictionary is a field, on one object, in one process. Run three replicas and you
have three dictionaries holding three separate semaphores for the same account id —
each happily granting access to its own instance at the same moment.

```
Instance 1:  [lock acquired] read 100 → write 0
Instance 2:  [lock acquired] read 100 → write 0     ← different semaphore entirely
Instance 3:  [lock acquired] read 100 → write 0
```

Three "critical sections", zero mutual exclusion. **You are back to lesson 01, but now
with code that looks correct.**

This is the hinge of the whole repo. The `lock` strategy reports
`scalesAcrossInstances: false` in `/api/lab/strategies` for exactly this reason, and
lesson 09 runs it under three replicas to watch it break.

The fix isn't a better lock — it's moving the coordination point somewhere all
instances share: the database (lessons 03 and 04) or a distributed lock (lesson 09).
Notice the interface doesn't change:

```csharp
builder.Services.AddSingleton<IAccountLock, InProcessAccountLock>();
// swap for PostgresAdvisoryAccountLock and the same handler survives scale-out
```

Same call site, same code, completely different scaling behaviour. **That is why "can
it scale?" is an application-design question before it is an infrastructure one.**

## Trade-offs

**What you gain:** correctness with almost no conceptual overhead, no schema changes,
no retry logic, and no wasted work — each actor does its job exactly once.

**What you give up:** throughput under contention (everyone queues), latency
predictability (your p99 is somebody else's critical section), and — decisively —
horizontal scalability. You also take on deadlock risk the moment a handler needs two
locks, which is lesson 06.

**When it's the right answer:** genuinely single-process state — an in-memory cache, a
singleton coordinating background work, a desktop app. It is the wrong answer for
anything that will ever run behind a load balancer.

## What an interviewer asks next

*"Why `SemaphoreSlim` rather than `lock`?"* — Covered above; the answer is `await`, and
the reason is thread ownership of monitors.

*"What happens to this code at three replicas?"* — It silently stops working. This is
the question the whole repo is built around, so be able to say *silently*.

*"How would you make it work across instances?"* — Move the coordination to shared
state: a database row lock, an optimistic concurrency token, or a distributed lock
(Postgres advisory lock, Redis). Then the follow-up they're really driving at: *"what
happens if the process holding a distributed lock dies?"* — you need a lease with a
TTL, and you have to handle the case where the lock expires while you still think you
hold it. That's [ADR-0012](../architecture/adr/0012-postgres-advisory-locks.md).

*"Is a lock enough to make your code thread-safe?"* — Only if every path that touches
the state takes the same lock. One unlocked read somewhere else and the guarantee is
gone. Locks are a convention the compiler doesn't enforce.
