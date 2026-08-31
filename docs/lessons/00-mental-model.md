# 00 — The mental model

> If you remember one thing from this repo, make it this page.

## Concurrency vs parallelism

**Concurrency is about structure. Parallelism is about execution.**

Picture a bank branch.

**Concurrency = one teller, many customers.** The teller serves you, and while you're
filling in a form they turn to the next customer. Nobody is served *simultaneously* —
the teller is one person — but five customers are *in progress* at once. When you
finish your form, the teller comes back to you.

That's `async`/`await`. One thread, many operations in flight. The thread doesn't sit
watching you write; it goes and does something else and comes back.

**Parallelism = four tellers, four counters.** Four customers genuinely being served at
the same instant. This needs four tellers — you cannot get parallelism by reorganising
one person's time.

That's `Parallel.ForEach` and PLINQ. It needs multiple CPU cores, because that's what
a core is: a teller.

|  | Concurrency | Parallelism |
|---|---|---|
| Question it answers | "How do I make progress on many things?" | "How do I finish this faster?" |
| Bank picture | One teller juggling | Many tellers at many counters |
| .NET | `async`/`await`, `Task.WhenAll` | `Parallel.ForEach`, PLINQ |
| Needs more cores | No | Yes |
| Good for | **waiting** — network, disk, database | **computing** — maths over lots of data |
| Wins you | throughput while waiting | speed while calculating |

You can have either without the other. A single-core machine runs concurrent code
fine. A tight parallel loop that never waits for anything isn't concurrent in any
interesting sense.

## The trap: `async` is not parallel

This is the single most common misunderstanding in .NET interviews.

```csharp
// Concurrency. ONE thread can run all ten of these.
// While each waits on the network, the thread is released to do other work.
var results = await Task.WhenAll(accountIds.Select(id => FetchBalanceAsync(id)));

// Parallelism. This wants a thread per core and keeps them all busy.
Parallel.ForEach(accounts, account => account.Interest = CalculateInterest(account));
```

The rule that makes it stick:

> **`async` is for waiting. `Parallel` is for working.**

If the operation is I/O — a database call, an HTTP request, reading a file — the CPU
has nothing to do while it waits, so you want `async`. Making it parallel just means
more threads sitting idle.

If the operation is CPU work — hashing, interest calculations, image processing — the
CPU is the bottleneck, so you want parallelism. Making it `async` achieves nothing;
there's no waiting to overlap.

### And `Task.Run` is not `async`

```csharp
// This does NOT make anything asynchronous. It moves synchronous, blocking work
// onto a thread-pool thread and then waits for it. On a web server this is usually
// worse than doing nothing: you've consumed two threads instead of one.
var balance = await Task.Run(() => GetBalanceFromDatabase(id));
```

`async` all the way down means the *library* is async — `GetBalanceFromDatabaseAsync`
genuinely releases the thread while the database works. Wrapping a blocking call in
`Task.Run` hides the blocking; it doesn't remove it. Lesson 09 shows what that costs
under load.

## What a race condition actually is

Most people define a race condition as "two things happening at the same time." That
definition is wrong, and believing it sends you looking for the wrong fix.

A race condition is:

> **read → think → write**, where somebody else wrote during your *think*.

Nothing has to be simultaneous. On a single core, with one thread, a race still
happens — the thread just has to be interrupted between the read and the write.

```csharp
var balance = store.Read(accountId);   // READ    — 100
if (balance >= amount)                 // THINK   — "100 >= 100, fine"
{                                      //         ← somebody else does all three here
    store.Write(accountId, balance - amount);  // WRITE — sets it to 0
}
```

The `if` is not wrong. It checked a number that was true when it was read and false by
the time it was used. **The bug is the gap, not the check.**

This is why the fix is never "add more cores", "make it async", or "use a
ConcurrentDictionary". The fix is always one of:

1. **Stop anyone else writing during the gap** (locking — pessimistic).
2. **Detect that someone did, and start over** (versioning — optimistic).
3. **Remove the gap** — make read-decide-write one atomic operation.

Run it yourself:

```bash
curl -X POST localhost:5080/api/lab/runs -H 'Content-Type: application/json' \
  -d '{"strategy":"naive","actors":5,"amountEach":100,"startingBalance":100}'
```

Five people withdraw ₹100 from an account holding ₹100. All five are approved. The
balance reads ₹0. ₹500 left the building. See lesson 01.

## The two ways to fix it

| | Optimistic | Pessimistic |
|---|---|---|
| Assumption | "Collisions are rare" | "Collisions are common" |
| Strategy | Let everyone try; detect conflicts; retry | Make everyone queue; one at a time |
| Bank picture | Everyone fills in a form; the clerk rejects yours if the details changed | One customer in the room at a time; a queue outside |
| Cost | **Wasted work** — losers redo their effort | **Waiting** — everyone else is blocked |
| Fails badly when | Contention is high (retry storms) | Transactions are long (queues grow) |
| .NET | `DbUpdateConcurrencyException` + retry | `SELECT … FOR UPDATE`, `SemaphoreSlim` |

Neither is "better". The deciding variable is **how often two people actually collide**,
and the trap is that both look fine in testing, where you're the only user. Lesson 05
turns this into a decision you can defend.

## A memory hook

- Concurrency — **one teller, many customers.** Structure. Waiting. `async`.
- Parallelism — **many tellers.** Execution. Working. `Parallel`.
- Race condition — **the gap between read and write**, not simultaneity.
- Optimistic — **apologise later.** Pessimistic — **ask permission first.**

## What an interviewer asks next

*"Give me an example of concurrency without parallelism."* — A single-threaded web
server handling 1,000 open connections with `async`/`await`. One thread, a thousand
requests in flight, one core.

*"Parallelism without concurrency?"* — A `Parallel.ForEach` summing an array. Multiple
cores, nothing waiting on anything, no interleaving to reason about.

*"Is `ConcurrentDictionary` thread-safe?"* — Yes, and it does not save you. It protects
its own internals. If you `TryGetValue`, decide something, then `TryUpdate`, that
sequence is still a race. Concurrent collections protect *their* invariants, never
*yours*. There's a comment saying exactly this in `InMemoryAccountStore`.
