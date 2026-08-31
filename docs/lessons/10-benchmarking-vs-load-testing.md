# 10 — Benchmarking vs load testing (and is BenchmarkDotNet worth it?)

Short answer: **yes — for the right job.** They are two different jobs, and confusing
them is itself an interview tell.

## The distinction

| | **Benchmarking** | **Load testing** |
|---|---|---|
| Tool | BenchmarkDotNet | k6, JMeter, Gatling, a custom harness |
| Scope | One method, in-process | The whole system, over the network |
| Units | nanoseconds / microseconds | requests per second, p95, p99 |
| Answers | "Is `Interlocked` faster than `lock`?" | "Does the API hold up at 500 concurrent users?" |
| Varies | the code | the load |
| Environment | one process, warmed up, quiet | realistic — network, database, connection pools |
| In this repo | `src/Bank.Benchmarks` | `tests/Bank.LoadTests` |

> **Benchmarking finds slow code. Load testing finds slow systems.**
> They fail in different places, and one cannot substitute for the other.

The classic case: a method that benchmarks at 50 ns can still produce an endpoint that
collapses at 100 concurrent users — because the bottleneck was the connection pool, or a
lock, or thread-pool starvation. None of those exist in a single-threaded benchmark.
Lesson 09 shows exactly that happening.

## What BenchmarkDotNet is genuinely good at

Naive timing code is wrong in ways that are easy to miss:

```csharp
var sw = Stopwatch.StartNew();       // ← measuring the JIT, not the code
DoWork();
sw.Stop();                           // ← one sample, no statistics
Console.WriteLine(sw.ElapsedMilliseconds);   // ← too coarse for microseconds
```

BenchmarkDotNet handles: JIT warmup, tiered compilation, multiple iterations with
outlier detection, statistical significance, memory allocation via `[MemoryDiagnoser]`,
dead-code elimination (returning a value keeps the work alive), and comparison against a
baseline with ratios.

The rule of thumb: **if you are measuring something faster than a millisecond, you need
BenchmarkDotNet.** Hand-rolled `Stopwatch` code will mislead you.

## What it is bad at

**Never point it at an HTTP endpoint.** It will happily produce a number, and that number
is meaningless: no concurrency, no connection pooling, no realistic contention, no
warm-up of the server, and a network hop it measures as if it were code.

That is a category error, and it's the thing to be able to name.

## Reading the results honestly

The reports in [`../benchmarks/`](../benchmarks/) come with a caveat, and the caveat is
part of the lesson:

> Produced on a **4-core shared cloud VM under a `ShortRun` job**, so the error bars are
> wide — some `Error` values are the same order as the `Mean`. Fine for the
> order-of-magnitude comparisons made here (11× slower, 1,857× slower). **Not** fine for
> anything subtler. Two results within 2× of each other on this hardware are "about the
> same".

A benchmark whose conditions you cannot state is a number, not evidence. Being able to
say "this comparison is only good to an order of magnitude, and here's why" is worth
more in an interview than a table of impressive figures.

## What each tool found here

**BenchmarkDotNet** (in-process, microseconds):

- Per-item `lock` in a parallel loop is **11× slower** than one core.
- Thread-local aggregation is **3.6× faster** than sequential on 4 cores.
- `Task.Run` around trivial sync work is **1,857× slower** than calling it.
- `ValueTask` allocates **nothing** where `async Task` allocated 720 KB.

None of those are visible from outside the process.

**Load testing** (over HTTP, RPS and percentiles) — lesson 09:

- Sync-over-async collapses under concurrency while the async version doesn't, on
  identical hardware.
- A hot row serialises throughput regardless of how many instances you run.
- The in-memory lock silently stops working at three replicas.

None of *those* are visible from a benchmark.

## On the licences

Worth knowing, because it comes up when you propose tooling at work:

- **BenchmarkDotNet** — MIT. Free, including commercial use.
- **NBomber** — a **paid commercial subscription** (licence v3.0, September 2025). This
  repo originally planned to use it and switched after reading the licence; the load
  harness in `tests/Bank.LoadTests` is a small purpose-built alternative.
- **k6** — AGPL-3.0 for the tool. Industry standard; scripts are committed under
  `loadtests/k6/`.

Same reasoning that removed MediatR and FluentAssertions
([ADR-0002](../architecture/adr/0002-no-mediatr.md)): a public teaching repo shouldn't
carry dependencies a reader can't use at work.

## A practical order of operations

1. **Profile first.** Don't benchmark what you haven't proven is hot. `dotnet-trace`,
   `dotnet-counters`, or a profiler.
2. **Benchmark the hot method** once you know which one it is.
3. **Load test the system**, because the bottleneck usually isn't the method.
4. **Measure in production.** Metrics and traces beat any pre-production estimate.

Most real performance problems are found at steps 3 and 4. Step 2 is where the
satisfying numbers are, which is exactly why people over-invest in it.

## Trade-offs

**BenchmarkDotNet costs** a separate project, a Release build, and minutes per run — a
full non-`ShortRun` pass can take a long time. It is overkill for anything you can
measure in milliseconds. Its results also apply to *one machine*: ratios travel between
machines reasonably well, absolute numbers don't.

**Load testing costs** an environment that resembles production, and results that are
only as good as that resemblance. Load testing against an empty database tells you
almost nothing.

## What an interviewer asks next

*"How would you find out why an endpoint is slow?"* — The wrong answer starts with
"benchmark the method". Start with a trace or profiler to find *where* the time goes; it
is usually I/O, a lock, or N+1 queries, none of which a micro-benchmark shows.

*"What's p99 and why not the average?"* — The average hides the tail. If 1% of requests
take 5 seconds, the mean looks fine and 1 in 100 users has a bad time — and on a page
making 10 calls, roughly 1 in 10 page loads is affected.

*"Have you used BenchmarkDotNet?"* — Say what you measured and what you concluded. "I
used it to compare `lock` and `Interlocked` for a counter, found `Interlocked` about 4×
cheaper uncontended, and then found it didn't matter because the real cost was
per-item synchronisation rather than the primitive" is a much better answer than "yes".
