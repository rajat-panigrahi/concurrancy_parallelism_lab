# 11 — Interview questions, with answers you can defend

Every answer here is backed by something runnable in this repo. Numbers are measured on
a 4-core VM; yours will differ, the ratios won't much.

---

## Fundamentals

**Q: Concurrency vs parallelism?**

> Concurrency is structure — dealing with many things at once. Parallelism is execution —
> doing many things at once. One teller juggling five customers is concurrency; four
> tellers at four counters is parallelism. In .NET, `async`/`await` for concurrency over
> I/O, `Parallel`/PLINQ for parallelism over CPU work.

Follow-up — *concurrency without parallelism?* A single-threaded server handling 1,000
connections with `async`. **Measured here:** 10 × 200 ms calls finished in 204 ms using
5 threads on 4 cores.

**Q: Does `async` create threads?**

> No — it *releases* them. `await Task.Delay(200ms)` registers a continuation and returns
> the thread to the pool. `Thread.Sleep(200ms)` holds its thread doing nothing.

That one sentence is what the question is testing.

**Q: Is `Task.Run(() => Something())` making my code async?**

> No. It moves blocking work to a thread-pool thread and waits for it — you now occupy
> two threads instead of one. Async has to go all the way down; the library must
> genuinely release the thread.

**Measured:** `Task.Run` around trivial sync work is **1,857× slower** and allocates
1.3 MB.

**Q: What is a race condition?**

> Read → think → write, where somebody else wrote during your think. It doesn't need
> anything simultaneous — a single core with one thread still races if it's interrupted
> between the read and the write.

**Q: Is `ConcurrentDictionary` thread-safe?**

> Yes, and it doesn't save you. It protects its own internals. `TryGetValue`, decide,
> `TryUpdate` is still a race. Concurrent collections protect *their* invariants, never
> *yours*.

---

## Locking

**Q: Why `SemaphoreSlim` instead of `lock`?**

> You can't `await` inside a `lock`. A monitor is owned by the thread that entered it,
> and an `await` may resume on a different thread, which would try to release a lock it
> never took. `SemaphoreSlim(1,1)` is owned by nobody, so any continuation can release it.

Follow-up — *the catch?* No reentrancy, no ownership: you must guarantee one release per
acquire, always in a `finally`. Releasing twice raises the count above its maximum and
silently lets two actors into the critical section.

**Q: `lock` vs `Interlocked`?**

> `Interlocked` is a single atomic instruction for a single variable — cheaper, and it
> can't deadlock. `lock` gives mutual exclusion over a *region*. Use `Interlocked` for
> counters and flags, `lock` when several operations must be atomic together.

**Measured uncontended:** `Interlocked` 13.7×, `lock` 54.6×, `ReaderWriterLockSlim`
59.2×, `SemaphoreSlim` 117× the cost of a plain increment.

**Q: What's a deadlock, and how do you prevent it?**

> Two operations each holding what the other needs. Four conditions must all hold; the
> cheapest to break is the circular wait, by acquiring locks in a consistent global
> order.

**Runnable here:** `/api/lab/deadlock` deadlocks for real (Postgres 40P01) and stops
deadlocking with `orderLocks: true` — one changed line. Money is conserved either way: a
deadlock costs a request, not corruption.

Follow-up — *deadlock vs livelock?* A deadlock is stuck doing nothing; a livelock is busy
achieving nothing. Unbounded optimistic retries are a livelock risk.

---

## Optimistic vs pessimistic

**Q: Explain both and when you'd use each.**

> Optimistic assumes collisions are rare: let everyone write, have the database reject
> stale writes, retry the loser. Pessimistic assumes collisions are common: lock the row
> first, make everyone queue. Optimistic pays in **wasted work**, pessimistic pays in
> **waiting**. The deciding variable is the conflict rate, not preference.

**Measured, identical input:** optimistic 9 attempts / 4 conflicts / **0 ms waiting**;
pessimistic 5 attempts / 0 conflicts / **244 ms waiting**. Both correct.

**Q: Which for a banking withdrawal?**

> Optimistic for a normal retail account — two people rarely touch the same account at
> once. Pessimistic for something genuinely hot, like a merchant settlement account
> taking thousands of writes a second — or shard it so it stops being hot.

Naming the deciding variable is what they're listening for. "Optimistic, always" loses.

**Q: How does EF Core implement optimistic concurrency?**

> A concurrency token added to the UPDATE's `WHERE`. On Postgres I map the `xmin` system
> column, which the database maintains for free — zero schema cost. On SQL Server it's
> `rowversion` with `[Timestamp]`. Zero rows matched → `DbUpdateConcurrencyException`.

Follow-up — *why not your own `Version` column?* It only protects the paths that remember
to bump it. A bulk update or a hand-written SQL fix silently bypasses it. A
database-maintained token has no such hole.

**Q: What do you do when you catch `DbUpdateConcurrencyException`?**

> Re-read and re-**decide**, not just re-send. And with a fresh `DbContext` — reusing one
> keeps the stale entity tracked, so the retry re-sends the same doomed UPDATE forever.
> Bound the retries; unbounded is a livelock.

---

## Parallelism

**Q: I parallelised my loop and it got slower. Why?**

> Almost always per-item synchronisation. If every iteration takes a lock, four cores
> queue at one door instead of computing.

**Measured:** per-item `lock` in a `Parallel.For` is **11× slower than one core**. Fixed
with thread-local partial sums merged once at the end — **3.68× faster** on 4 cores.
Same work, same cores, 33× between fastest and slowest.

> How you aggregate matters more than whether you parallelise.

**Q: When would you not use `Parallel.ForEach`?**

> I/O work (use `Task.WhenAll`); small collections; on a busy web server, where the
> server is already using every core serving other requests; and any body that
> synchronises per item.

Also: `Parallel.ForEach` takes `Action<T>`, so an async lambda becomes `async void` —
fire-and-forget, exceptions unobservable. Use `Parallel.ForEachAsync`.

**Q: What's false sharing?**

> Two cores writing to different variables in the same cache line, so each write
> invalidates the other's cache. Symptom: correct, parallel, and slower than expected —
> exactly the `Interlocked`-per-item result above.

---

## Scaling

**Q: Can your app scale?**

The full answer is [lesson 09](09-scaling.md). The 60-second version:

> Three questions. **Code:** async all the way down — one endpoint doing `.Result`
> instead of `await` gets 35 RPS where the async one gets 979, same hardware, because
> blocking starves the thread pool. No infrastructure fixes that. **Instances:** scale
> out horizontally, but only if it's stateless — I have a slice with an in-memory lock
> that's provably correct on one instance and silently wrong on three, no code changed.
> **Data:** then the database is the bottleneck, usually row contention rather than CPU —
> shard the hot row, queue writes, and add idempotency keys because retries will
> duplicate operations at that scale.

Then the closer:

> "But I'd want to know which of the three is actually the problem first. Most 'we need
> to scale' turns out to be one N+1 query."

**Q: Is scaling an app concern or a Docker/Kubernetes concern?**

> Both, and in that order. Kubernetes multiplies instances; it doesn't make un-scalable
> code scalable. If state lives in process memory, an orchestrator runs three copies of
> the bug.

**Q: What breaks first when you scale out?**

> Anything process-local: locks, caches, sessions, rate-limit counters, in-process job
> schedulers, and SignalR groups. Then database connection count.

**Q: When do you actually need Kubernetes?**

> For operational properties — rolling deploys, self-healing, autoscaling, service
> discovery — not performance ones. If the answer to "why K8s" is "to make it fast",
> that's the wrong reason.

**Q: What's p99 and why not the average?**

> The average hides the tail. If 1% of requests take 5 s the mean looks fine and 1 in 100
> users has a bad time — and on a page making 10 calls, roughly 1 in 10 page loads is
> affected.

---

## Testing and tooling

**Q: How would you test a race condition?**

> Not by running it a lot and hoping. I make the interleaving a parameter: an injected
> gate that holds every actor after its read until all have read, so the lost update
> reproduces **100%** of the time. Then invariant tests run free and assert only what
> must be true under *any* interleaving — money conserved, balance never negative — never
> who won, because that's asserting the scheduler.

Follow-up — *isn't that test code in production?* Yes. Concede it, then give the
mitigation (compile it out behind a flag) and the reason it earns its place here. Also
worth naming: Coyote systematically explores interleavings rather than sampling them.

**Q: Is BenchmarkDotNet worth using?**

> Yes, for micro-benchmarks — it handles JIT warmup, statistical significance and
> allocation tracking, all of which hand-rolled `Stopwatch` code gets wrong below about a
> millisecond. But never point it at an HTTP endpoint. That's a load test — a different
> tool answering a different question. **Benchmarking finds slow code; load testing finds
> slow systems.**

**Q: An endpoint is slow. What do you do?**

> Profile first. The wrong instinct is to benchmark a method — the time is usually in
> I/O, a lock, or N+1 queries, none of which a micro-benchmark shows. Trace it, find
> where the time actually goes, *then* measure that.

---

## The questions to ask them

Interviews go both ways, and these signal you've thought about the topic:

- "What's your conflict rate on the hottest entity?" — most teams have never measured it.
- "Are your services genuinely stateless, or stateless-except-for-a-few-caches?"
- "Do you have idempotency keys on write endpoints? What happens when the LB retries?"
- "What's your p99, and what's your budget for it?"
