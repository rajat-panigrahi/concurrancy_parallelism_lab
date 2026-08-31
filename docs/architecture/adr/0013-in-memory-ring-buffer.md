# ADR-0013 — Contention events in a bounded in-memory buffer

- **Status:** Accepted
- **Date:** 2026-08-30

## Context

Every step of every actor is recorded: reads, gate waits, lock waits, writes, commits,
conflicts. A 5-actor run produces ~25 events. The load tests in M5 fire thousands of
requests, so the same recorder can be asked to absorb hundreds of thousands.

Two constraints are unusual here, and both come from *what* is being recorded:

1. **Recording happens inside the code under observation.** A lock taken in `Record()`
   would change the interleaving we are trying to show. The observer must not perturb
   what it observes.
2. **It must never grow without bound.** A lab left running under load that eats memory
   until it dies is a bad demo and a worse lesson.

## Options considered

- **Persist events to PostgreSQL.** Durable, queryable with SQL, survives restarts.
- **A logging framework** (Serilog to a file or Seq) with structured events.
- **A bounded in-memory buffer per run**, published to a channel for live streaming.

## Decision

A bounded in-memory buffer: at most 20,000 events per run, at most 50 runs retained,
oldest evicted. Every event is also written to an unbounded `Channel<ContentionEvent>`
that a background service drains for SignalR.

## Why not the others

- **Persisting to PostgreSQL** puts a database round-trip inside the critical section
  of a benchmark about database contention. The recording would contend with the thing
  being recorded, and the timings would measure the instrument. It also adds write load
  precisely when we are trying to measure write load. Durability buys nothing: a run's
  timeline is interesting for the minute you look at it.
- **A logging framework** is the right answer for production observability and the
  wrong one here. Structured logs are for querying after the fact; this data drives a
  live UI and a computed verdict. We'd be writing events out and parsing them back.
  Log sinks also buffer and batch on their own schedule, so ordering and latency become
  the framework's business rather than ours — unacceptable when the *ordering is the
  data*.

## Trade-offs accepted

- **Events are lost on restart** and evicted after 50 runs. Fetching an older run
  returns 404. Acceptable: runs are cheap to reproduce, and forced-race mode makes them
  reproduce identically.
- **Memory is bounded but not small.** 50 runs × 20,000 events is a real allocation if
  every run is enormous. Sized for a lab, not a service.
- **The buffer is process-local**, so with multiple replicas a run's events live only on
  the instance that served it. Same limitation as SignalR groups (ADR-0009) — and, once
  again, the same lesson as the in-memory lock.
- **The channel is unbounded**, which is a deliberate small risk: `TryWrite` always
  succeeds so the producer can never block, but a stalled consumer would let it grow.
  Bounding it would mean either blocking a withdrawal or dropping events, and for a
  lab, "never distort the measurement" beats "never allocate".

## Consequences

- `Record()` does an `Interlocked.Increment`, a queue enqueue and a non-blocking channel
  write. No locks, no I/O, no allocation beyond the event itself.
- `Interlocked` rather than `lock` for counters — a counter needs atomicity, not mutual
  exclusion. Cheaper, and it cannot deadlock.
- The producer/consumer split via `Channel<T>` means the slow work (SignalR fan-out)
  happens on a background reader, so a slow client cannot slow a withdrawal. This is
  itself one of the lessons: `Channel<T>` is the modern .NET producer/consumer
  primitive, where the older answer, `BlockingCollection`, blocks threads instead of
  awaiting.
- `RunSummary` is computed once at the end of a run and stored separately in
  `LabRunStore`, so a verdict outlives the event eviction it was derived from.

## Interview angle

**The 60-second version:** "The recorder sits inside the code being measured, so it
can't block and it can't allocate much — otherwise the instrument changes the
measurement. It appends to a bounded ring buffer and does a non-blocking write to a
channel; a background service drains that channel and does the slow SignalR fan-out.
Bounded because an unbounded buffer under load is just a slow memory leak."

**What a good interviewer asks next:** *"Why a `Channel` rather than an event or a
`BlockingCollection`?"* — An event handler runs on the caller's thread, so a slow
subscriber becomes the caller's problem. `BlockingCollection` blocks a thread pool
thread instead of releasing it. `Channel<T>` is async-native and gives explicit
backpressure control. The sharper follow-up: *"what happens when the consumer can't
keep up?"* — with an unbounded channel, memory grows; with a bounded one you choose
between blocking the producer and dropping events, and that choice is a product
decision, not a technical one.
