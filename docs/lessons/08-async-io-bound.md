# 08 — Async: making waiting overlap

**Code:** `src/Bank.Api/Features/Fraud/RunFraudChecks/`
**Tests:** `tests/Bank.Api.UnitTests/Features/Fraud/FraudFanOutTests.cs`
**Benchmarks:** `src/Bank.Benchmarks/Benchmarks/AsyncOverheadBenchmarks.cs`

Lesson 07 made CPU work finish sooner by using more cores. This lesson makes *waiting*
finish sooner by using **no extra cores at all**.

## Run it

Ten fraud checks, each a 200 ms call to a slow external service:

```bash
curl -X POST localhost:5080/api/lab/fraud -H 'Content-Type: application/json' \
  -d '{"checks":10,"latencyMs":200,"maxConcurrency":4}'
```

```
10 checks x 200ms on 4 cores

strategy                         duration  threads
--------------------------------------------------
sequential await                   2005ms        2
Task.WhenAll                        204ms        5
throttled to 4                      603ms        5
Parallel.ForEachAsync (4)           607ms        3
```

**2005 ms → 204 ms. Ten calls, five threads, four cores, and the CPU was idle the whole
time.**

This is the clearest demonstration in the repo of the lesson-00 distinction:

> This is **concurrency**, not parallelism. Nothing computed faster. The waiting
> overlapped.

## The bug: `await` inside a loop

```csharp
for (var i = 0; i < checks; i++)
{
    verdicts.Add(await fraud.CheckAsync(i, latency, ct));   // ← waits before starting the next
}
```

Each iteration waits for the previous one. Ten independent calls take ten times as long,
for no reason. This code *is* asynchronous — it releases the thread properly — it is
just needlessly serialised. **Async and concurrent are not the same thing.**

This is one of the most common performance bugs in real .NET code, and it looks
completely correct.

## The fix

```csharp
var tasks = Enumerable.Range(0, checks).Select(i => fraud.CheckAsync(i, latency, ct));
return await Task.WhenAll(tasks);
```

The subtlety: `.Select(...)` **starts** each task. They're already running by the time
`WhenAll` sees them. `WhenAll` doesn't launch anything — it waits for a set of things
already in flight.

Total time becomes the **slowest** call, not the sum.

> Beware `.Select(async …)` over a lazy `IEnumerable` that you then iterate twice — the
> tasks start on first enumeration. If in doubt, `.ToArray()` before awaiting so you
> know exactly when they started.

## Why five threads and not ten

This is the part that makes async click.

`await Task.Delay(200ms)` **does not hold a thread.** It registers a continuation and
returns the thread to the pool. Ten in-flight waits therefore cost roughly zero threads —
the handful you see are just whichever pool threads resumed the continuations.

Compare `Thread.Sleep(200ms)`, which holds its thread doing nothing for 200 ms. Ten of
those genuinely costs ten threads.

That difference is the entire value of async on a server:

| | 1,000 concurrent requests, each waiting 100 ms on the database |
|---|---|
| **Blocking** | ~1,000 threads. ~1 MB stack each ≈ 1 GB. Thread-pool starvation. |
| **Async** | A handful of threads. The rest is continuations. |

There's a test asserting this directly —
`TheSpeedupComesFromOverlappingWaits_NotFromMoreThreads`.

## Throttle, or you'll take down your dependency

`Task.WhenAll` over 10,000 items fires 10,000 simultaneous calls, and now *you* are the
outage. Cap it:

```csharp
using var throttle = new SemaphoreSlim(limit, limit);

var tasks = items.Select(async item =>
{
    await throttle.WaitAsync(ct);
    try     { return await CallAsync(item, ct); }
    finally { throttle.Release(); }          // ALWAYS in a finally
});

return await Task.WhenAll(tasks);
```

The maths checks out in the measurements: 10 checks at concurrency 4 = 3 batches ×
200 ms = **603 ms** observed. Slower than unbounded `WhenAll`, on purpose.

`Release()` must be in a `finally`. An exception that skipped it would permanently shrink
the pool until nothing could run at all — a slow-motion deadlock that looks like "the
service got gradually slower".

`Parallel.ForEachAsync` (.NET 6+) does the same thing with less code:

```csharp
await Parallel.ForEachAsync(items,
    new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct },
    async (item, ct) => await CallAsync(item, ct));
```

Note it is `ForEachAsync`, not `ForEach`. `Parallel.ForEach` takes `Action<T>`, so an
async lambda becomes `async void`: fire-and-forget, exceptions unobservable, returns
before the work is done.

## What async costs when there's nothing to wait for

| Method | Mean | Ratio | Allocated |
|---|---:|---:|---:|
| Synchronous call | 6.0 µs | 1.00 | – |
| `async Task`, always completed | 128.3 µs | 21.4 | 720,072 B |
| `async ValueTask`, always completed | 13.0 µs | 2.16 | **0 B** |
| **`Task.Run` wrapping sync work** | **11,122 µs** | **1,857** | 1,360,216 B |

Three things worth taking away:

1. **The state machine isn't free**, but at ~12 µs per 10,000 calls it is noise next to
   any real I/O. "Async is slow" is wrong for the case async exists for.
2. **`ValueTask` earns its keep on hot paths that usually complete synchronously** — a
   cache hit, a buffered read. Zero allocation vs 720 KB. Don't use it everywhere; it
   can only be awaited once, and misusing it is worse than the allocation.
3. **`Task.Run` around trivial synchronous work is 1,857× slower.** A thread-pool queue,
   a hand-off and a context switch — to do a subtraction.

## `Task.Run` is not how you make something async

```csharp
// Fake async. The blocking call is still blocking; you've just added a thread.
var balance = await Task.Run(() => GetBalanceFromDatabase(id));
```

Async has to go **all the way down**: the library must genuinely release the thread
(`GetBalanceFromDatabaseAsync`). Wrapping a blocking call hides the blocking; it doesn't
remove it. On a server it's usually worse than doing nothing, because you now occupy two
threads instead of one.

`Task.Run` is legitimate for one thing: getting genuinely CPU-bound work off the request
thread — and even then, ask whether it belongs in a background job instead.

## Cancellation

Every async method here takes a `CancellationToken` and passes it down. The test
`CancellationPropagates_AndStopsTheWorkPromptly` cancels ten 30-second calls and gets
control back in well under a second.

Threading the token everywhere is tedious and it is the difference between a client
disconnect freeing resources immediately and your server working on answers nobody will
read. ASP.NET Core gives you the request's token for free — use it.

## Trade-offs

**You gain:** enormous throughput on I/O-bound work, at almost no CPU cost. A server
handling thousands of concurrent requests on a handful of threads.

**You give up:** simplicity. Async is viral — one async call makes the whole call chain
async. Stack traces are harder to read. There's a family of subtle bugs that don't exist
in synchronous code: `async void`, deadlocks from `.Result`/`.Wait()` where a
`SynchronizationContext` exists, unobserved exceptions, and `ValueTask` awaited twice.
And it buys **nothing** for CPU-bound work — that's lesson 07.

**When it's right:** any I/O. Database, HTTP, file, queue. Essentially everything a web
API spends its time on.

## What an interviewer asks next

*"Does `async` create threads?"* — No. It releases them. That single sentence is what the
question is testing.

*"What's `async void` for?"* — Event handlers, and nothing else. Exceptions can't be
caught by the caller and will crash the process.

*"What happens if you call `.Result` on an async method?"* — You block the thread, and in
any context with a `SynchronizationContext` (classic ASP.NET, WinForms, WPF) you
deadlock. In ASP.NET Core there's no context so it "only" wastes a thread — which under
load is thread-pool starvation. That's lesson 09, measured.

*"How do you limit concurrency?"* — `SemaphoreSlim` or `Parallel.ForEachAsync` with
`MaxDegreeOfParallelism`. Then be ready for the follow-up: *why* limit it — because
unbounded fan-out moves your outage to your dependency.

*"`ConfigureAwait(false)`?"* — It tells the continuation not to resume on the captured
context. Important in library code and on the old ASP.NET; unnecessary in ASP.NET Core,
which has no `SynchronizationContext`. Knowing *why* it stopped mattering is the better
answer than knowing the rule.
