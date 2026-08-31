# 07 — Parallelism: making CPU work finish sooner

**Code:** `src/Bank.Api/Features/Interest/CalculateInterest/`
**Tests:** `tests/Bank.Api.UnitTests/Features/Interest/InterestStrategiesTests.cs`
**Benchmarks:** `src/Bank.Benchmarks/Benchmarks/AggregationBenchmarks.cs`

Lessons 01–06 were about *correctness* under concurrency. This one is about *speed*, and
it is a different problem with different tools.

## Run it

Nightly interest for 400,000 accounts, computed six ways:

```bash
curl -X POST localhost:5080/api/lab/interest -H 'Content-Type: application/json' \
  -d '{"accountCount":400000,"repeats":3}'
```

```
400000 accounts on 4 cores

strategy                     aggregation                  best ms  speedup
----------------------------------------------------------------------------
sequential                   none                           18.36    1.00x
parallel + lock              lock per item                 164.25    0.11x
parallel + Interlocked       atomic per item                35.44    0.52x
parallel + local sums        thread-local, merged once       4.98    3.68x
PLINQ                        thread-local, merged once       6.46    2.84x
parallel + ConcurrentQueue   shared collection             136.94    0.13x
```

All six produce the identical total. Look at the spread: **the fastest is 33× the
slowest, and every one of them uses all four cores.**

> **How you aggregate matters more than whether you parallelise.**

## Why "parallel + lock" is 9× slower than one core

```csharp
Parallel.For(0, accounts.Length, i =>
{
    var interest = ComputeInterestCents(in accounts[i]);
    lock (gate) { total += interest; }     // ← every single item
});
```

400,000 items means 400,000 lock acquisitions. The actual computation is a few hundred
nanoseconds; the lock costs more than that and, worse, it is a **single point every core
must pass through**. Four cores now take turns at one door.

You have paid for four cores and built a queue in front of them. This is the classic
"I parallelised it and it got slower" result, and there's a test pinning it down
(`ParallelWithPerItemLock_CanBeSlowerThanSequential`).

`Interlocked` (0.52×) is better — a single atomic CPU instruction instead of a
synchronisation primitive — but still slower than sequential, because all four cores are
hammering **one memory location**. Each write invalidates that cache line in the other
cores' caches, so they spend their time re-fetching it. That's **false sharing**, and
it's worth knowing the name.

`ConcurrentQueue` (0.13×) is the worst of the lot and allocated **2 MB**. A concurrent
collection is thread-safe, not fast, and it is not an aggregation strategy.

## The right way: partition, then merge

```csharp
Parallel.For(
    0, accounts.Length,
    localInit: () => 0L,                                        // each worker starts at zero
    body: (i, _, runningTotal) => runningTotal + Compute(i),    // accumulates PRIVATELY
    localFinally: partial => Interlocked.Add(ref total, partial)); // merges ONCE
```

Now the shared location is touched about four times instead of 400,000 times. The result
is 3.68× on 4 cores — near-linear, which is about as good as it gets.

> **Partition the work, aggregate at the end. Never synchronise per item.**

PLINQ (`accounts.AsParallel().Sum(...)`) does the same thing declaratively and lands
close behind. Prefer it for readability unless you have measured a reason not to.

## BenchmarkDotNet agrees

100,000 accounts, `ShortRun`, 4-core VM:

| Method | Mean | Ratio | Allocated |
|---|---:|---:|---:|
| Sequential (one core) | 3,447 µs | 1.00 | 3 B |
| Parallel + lock per item | 38,212 µs | **11.09** | 3,353 B |
| Parallel + Interlocked per item | 9,375 µs | 2.72 | 2,492 B |
| **Parallel + thread-local sums** | **964 µs** | **0.28** | 2,494 B |
| PLINQ | 1,575 µs | 0.46 | 3,826 B |
| Parallel + ConcurrentQueue | 35,704 µs | 10.36 | **2,104,358 B** |

(Ratios below 1.00 are faster than the baseline. Full reports and their caveats in
[`../benchmarks/`](../benchmarks/).)

## When parallelism helps at all

It needs **all** of these:

1. **CPU-bound work.** If it waits on anything, use `async` instead (lesson 08).
2. **Enough items** to cover the cost of partitioning and scheduling. A few hundred
   cheap items will be slower parallel than sequential.
3. **Expensive enough per item.** If the item costs less than scheduling it, you lose.
4. **Independent items.** If item 5 needs item 4's result, there's nothing to overlap.

And the ceiling is real. **Amdahl's law:** if 10% of a job is inherently sequential, then
even with infinite cores the best possible speedup is 10×. Speeding up the parallel part
stops mattering long before you think.

## Traps

**`Parallel.ForEach` with async work.** It takes `Action<T>`, so an `async` lambda becomes
`async void`: fire-and-forget, exceptions unobservable, and it returns before the work
finishes. Use `Parallel.ForEachAsync` (.NET 6+).

**Parallelising I/O.** `Parallel.ForEach` over 100 HTTP calls burns threads to sit
waiting. Use `Task.WhenAll`.

**Ignoring `MaxDegreeOfParallelism` on a web server.** A parallel loop grabs thread-pool
threads that ASP.NET needs to serve other requests. On a server, parallelising one
request can slow every other one down.

**Assuming more cores means proportionally faster.** Memory bandwidth, cache and the
sequential fraction all cap it. 3.68× on 4 cores is a *good* result.

## Trade-offs

**You gain:** wall-clock speed on genuinely CPU-bound work, roughly proportional to
cores, for very little code.

**You give up:** determinism of ordering (results arrive in whatever order); simple
debugging (a breakpoint in a parallel body hits on many threads); exception handling
(you get an `AggregateException` with N inner exceptions); and, on a shared server,
fairness — your parallel loop competes with every other request.

**When it's right:** batch jobs, report generation, nightly runs, image and data
processing. **When it isn't:** inside a per-request handler on a busy web server, where
the server is *already* using every core by serving many requests at once. If all cores
are busy, parallelising one request just steals from another.

## What an interviewer asks next

*"When would you not use `Parallel.ForEach`?"* — I/O work; small collections; on a web
server under load; and any body that synchronises per item, since that's usually slower
than sequential.

*"What's false sharing?"* — Two cores writing to different variables that share a cache
line, so each write invalidates the other's cache. Its symptom is exactly the
`Interlocked`-per-item row above: correct, parallel, and slower than expected.

*"How do you handle exceptions in a parallel loop?"* — `Parallel.For` collects them into
an `AggregateException`. Other iterations already running still complete, so it is not
"stop everything at the first failure".

*"What's the difference between `Parallel.ForEach` and PLINQ?"* — `Parallel.ForEach` is
for side effects, PLINQ for transformations that produce a result. PLINQ handles
partitioning and merging for you, which is why it beat hand-written per-item
synchronisation here without any tuning.
