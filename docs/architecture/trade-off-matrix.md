# Trade-off matrix

Every decision in this repo on one page. The column that matters is the last one:
**"breaks down when"**. Senior interviews are mostly about knowing the limits of your own
choices, and anyone can recite what they picked.

## Concurrency mechanisms

| Mechanism | We gain | We give up | **Breaks down when…** |
|---|---|---|---|
| **No coordination** (`naive`) | Fastest, simplest, correct for one writer | Correctness under concurrency | A second actor reads before you write. Fails **silently** — 4 of 5 actors told "approved", ₹400 gone, nothing logged |
| **In-process lock** (`SemaphoreSlim`) | Correct, cheap (~8 ms total waiting), no schema change | Throughput under contention, deadlock-freedom once two locks are involved | **You run a second instance.** Three replicas = three separate semaphores. Silently wrong, zero code changed |
| **Optimistic** (`xmin` + retry) | No blocking, scales horizontally, cost is a retry | Retry logic in every write path; wasted work | Contention is high. Measured: 5 actors → 9 attempts. Degrades into a retry storm — correct, but N× the work for 1× the throughput |
| **Pessimistic** (`SELECT … FOR UPDATE`) | No wasted work, no retry logic, fair queueing | Throughput, latency predictability, a connection per waiter | Transactions are long. Measured: 244 ms of waiting for 5 actors. An external call inside the lock takes down the whole endpoint |
| **Distributed lock** (advisory) | Correct at any instance count, no new infrastructure | ~2.6 s total waiting vs ~8 ms in-process; a connection per lock | The thing you're protecting is one DB row — then `FOR UPDATE` is simpler and cheaper. Also Postgres-only |
| **Idempotency key** | Retries stop duplicating money | A key per operation; storage; expiry policy | The client doesn't send a stable key. Without one: 5 retries charged 5× |
| **One atomic statement** (`SET balance = balance - 40 WHERE …`) | No coordination needed at all | Only works if the logic fits in one statement | Your decision needs data the statement can't see |

## Parallelism and async

| Choice | We gain | We give up | **Breaks down when…** |
|---|---|---|---|
| **`Parallel` + per-item lock** | — | Everything | Always. Measured **11× slower than one core** — four cores queueing at one door |
| **`Parallel` + `Interlocked` per item** | Correct, no lock | Still one contended cache line | Item count is large. Measured 2.72× slower than sequential (false sharing) |
| **`Parallel` + thread-local sums** | Near-linear speedup (3.68× on 4 cores) | Slightly more code | Work is I/O-bound, items are few, or per-item cost is below scheduling cost |
| **PLINQ** | Same result, less code | Slightly less control | You need side effects rather than a computed result |
| **`Task.WhenAll`** | 2005 ms → 204 ms on 5 threads | Unbounded fan-out | You have 10,000 items — now *you* are your dependency's outage |
| **Throttled fan-out** (`SemaphoreSlim`) | Protects the dependency | Slower on purpose (603 ms vs 204 ms) | `Release()` isn't in a `finally` — the pool shrinks to nothing |
| **`Task.Run` around sync work** | — | Everything | Always. Measured **1,857× slower**. It doesn't make code async; it hides the blocking |
| **`ValueTask`** | Zero allocation vs 720 KB on hot sync paths | Can only be awaited once | You await it twice, or store it. Then it's worse than `Task` |

## Architecture

| Decision | Chosen | Alternative | We gain | We give up | **Breaks down when…** |
|---|---|---|---|---|---|
| [0001](adr/0001-vertical-slice-architecture.md) | Vertical slice | Clean/layered | One folder per feature; the race is readable in one file | Compile-time dependency rules; some duplication | Many teams share a codebase and need enforced boundaries |
| [0002](adr/0002-no-mediatr.md) | Plain handlers | MediatR | No licence, no indirection between you and the bug | Pipeline behaviours for cross-cutting concerns | You have 60 handlers all needing the same logging/validation/transaction |
| [0003](adr/0003-target-dotnet-8-lts.md) | .NET 8 LTS | .NET 10 | Installable here; most-deployed LTS | `System.Threading.Lock` (.NET 9+) | .NET 8 leaves support |
| [0004](adr/0004-hybrid-persistence.md) | In-memory **and** Postgres | Either alone | The raw race is visible; DB concurrency is real | Two models to hold in your head | A reader misses which store a slice uses |
| [0005](adr/0005-postgresql-over-sqlserver-sqlite.md) | PostgreSQL | SQL Server, SQLite | Real row locks, free token, advisory locks | Portability of the SQL | Your shop is SQL Server — concepts port, syntax doesn't |
| [0006](adr/0006-xmin-concurrency-token.md) | `xmin` | `rowversion`, own column | Token with **zero** schema cost; can't be forgotten by a raw UPDATE | Postgres-only; it's a `uint` | You port to SQL Server (→ `rowversion`) |
| [0007](adr/0007-optimistic-by-default.md) | Optimistic default | Pessimistic default | Scales horizontally; no locks across requests | Retry logic; unfair under load | Rows are hot — then switch, or stop the row being hot |
| [0008](adr/0008-interleave-gate-in-production-code.md) | Test seam in prod code | Sleeps, brute force | Races reproduce **100%** of the time | A testability hook on the hot path | You ship it to production — compile it out |
| [0009](adr/0009-signalr-for-live-timeline.md) | SignalR | SSE, polling | Groups, transport fallback, a .NET interview topic | A client dependency; connection lifecycle | **More than one replica** — groups are per-process; needs a Redis backplane |
| [0010](adr/0010-three-perf-tools.md) | BDN + harness + k6 | One tool | Right tool per question | Three things to explain | You point BenchmarkDotNet at an endpoint — category error |
| [0012](adr/0012-postgres-advisory-locks.md) | Advisory locks | Redis, etcd | No new infrastructure; auto-released on disconnect | ~300× slower than in-process; a connection each | You need cross-datastore coordination, or aren't on Postgres |
| [0013](adr/0013-in-memory-ring-buffer.md) | Bounded buffer | Persist, log | Recording never perturbs what it measures | Events lost on restart; last 50 runs only | You want a run from yesterday |
| [0014](adr/0014-compose-not-kubernetes.md) | Compose replicas | Kubernetes | 30 seconds to show the lock breaking | Production realism | You need rolling deploys, self-healing, autoscaling |
| [0015](adr/0015-local-postgres-over-testcontainers.md) | Local PG + Respawn | Testcontainers | Works without a Docker daemon | Hermetic isolation per run | State leaks between runs; version drift |
| [0016](adr/0016-three-tier-test-strategy.md) | Three tiers | One | Deterministic races **and** real-scheduler coverage | Machinery before the first assertion | Nobody maintains the distinction and tier 2 starts asserting orderings |

## Scaling, by layer

| Layer | Fix | **Breaks down when…** |
|---|---|---|
| **1. Code** | Async all the way down; don't lock the hot path; avoid N+1 | One `.Result` anywhere. Measured: **979 → 35 RPS**, p99 105 ms → 3531 ms, same hardware |
| **2. Instances** | Stateless services, N replicas, load balancer | Anything lives in process memory: locks, caches, sessions, counters, **SignalR groups** |
| **3. Data** | Shard hot rows, queue writes, read replicas, idempotency | One hot row — serialises regardless of replica count. Replicas also **triple** DB load |

## The five sentences worth memorising

1. **A race condition is the gap between read and write, not simultaneity.**
2. **`async` is for waiting; `Parallel` is for working. `Task.Run` is neither.**
3. **Optimistic pays in wasted work; pessimistic pays in waiting. Contention decides.**
4. **How you aggregate matters more than whether you parallelise.**
5. **Kubernetes multiplies instances; it doesn't make un-scalable code scalable.**
