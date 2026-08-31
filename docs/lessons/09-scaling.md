# 09 — "Can your app scale?"

> The question that causes the most confusion, because it is **three different questions**
> and interviewers rarely say which one they mean.

You have 100 users today and 1,000,000 tomorrow, and the API has to stay fast. Is that
an application-code problem, or a Docker/Kubernetes problem?

**Both — and in that order.** Kubernetes multiplies instances. It cannot make
un-scalable code scalable. Here is that claim, measured.

---

## Layer 1 — the application code

**This is where most candidates lose the point.** Scaling starts before any
infrastructure exists.

Two endpoints. Same work, same hardware, same 100 ms wait. The only difference is
whether the code holds a thread while waiting:

```csharp
// /api/scale/async
await SimulateIoAsync(delayMs, ct);

// /api/scale/sync-over-async
SimulateIoAsync(delayMs, ct).GetAwaiter().GetResult();
```

100 virtual users, 10 seconds, 4-core VM:

| | RPS | p50 | p95 | p99 |
|---|---:|---:|---:|---:|
| **async (proper await)** | **979.0** | 101.9 ms | 104.2 ms | **105.4 ms** |
| **sync-over-async (`.Result`)** | **35.2** | 2760.2 ms | 3530.4 ms | **3530.7 ms** |

**27.8× the throughput. 33.5× lower p99. Zero infrastructure changes.**

Reproduce it:

```bash
dotnet run --project src/Bank.Api -c Release
dotnet run --project tests/Bank.LoadTests -c Release -- --seconds=10
```

### Why it collapses

`.Result` blocks a thread-pool thread for the entire wait. The pool starts at roughly
one thread per core (4 here) and **grows by only one or two threads per second**. So
with 100 concurrent requests, 96 of them queue — not for the database, but for a thread
to run on. Latency doesn't degrade gracefully; it falls off a cliff and stays there.

Watch it live while the load test runs:

```bash
curl localhost:5080/api/scale/threadpool
```

The async version never has the problem, because a waiting request holds no thread at
all (lesson 08).

**No amount of Kubernetes fixes this.** You would need ~28× the instances to match what
one instance achieves by awaiting properly. That is the app-layer answer, and it's the
half most people skip.

### The rest of layer 1

- **Async all the way down.** One `.Result` anywhere in the chain reintroduces the cliff.
- **Don't lock on the hot path.** Lesson 07: per-item synchronisation made a parallel
  loop **11× slower than one core**.
- **Connection pooling.** The pool is finite. Every blocked transaction holds one
  (lesson 04), so lock contention becomes connection exhaustion — and then *unrelated*
  endpoints start failing.
- **N+1 queries.** 1,000 round-trips instead of 1 is the most common real cause of a
  slow endpoint, and no infrastructure hides it.
- **Cache what doesn't change.** The fastest query is the one you don't run.

---

## Layer 2 — more instances (Docker / Kubernetes)

Now the code is efficient, add machines. `deploy/docker-compose.scale.yml` runs three
replicas behind nginx:

```bash
docker compose -f deploy/docker-compose.scale.yml up --build
```

**Nothing in the application code changes.** And that is exactly when you find out which
of your code was secretly single-instance:

| Strategy | 1 instance | 3 instances | Why |
|---|---|---|---|
| `naive` | broken | broken | no coordination anywhere |
| `lock` | **correct** | **BROKEN** | three replicas, three separate semaphores |
| `optimistic` | correct | correct | the token lives in Postgres |
| `pessimistic` | correct | correct | the row lock lives in Postgres |
| `distributed-lock` | correct | correct | the advisory lock lives in Postgres |

The `lock` strategy is the one to stare at. It was correct. It had a test proving it was
correct. It is now silently wrong, and **not one line of it changed**.

```csharp
private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();
```

That dictionary is a field, in one process. Three replicas have three of them, each
happily granting access to its own instance at the same moment.

> **Kubernetes multiplies instances. It doesn't make un-scalable code scalable.**
> Horizontal scale works only if the code is stateless and coordinates through *shared*
> state, not process memory.

### What "stateless" actually means

Not "has no state" — it means **no state that matters lives in the process**:

| Process-local (breaks) | Shared (survives) |
|---|---|
| `static` dictionary as a source of truth | database |
| in-memory `SemaphoreSlim` | DB row lock, advisory lock, Redis |
| in-memory session | distributed cache |
| in-memory rate-limit counter | Redis counter |
| in-memory job scheduler | a queue, or leader election |
| **SignalR groups** | SignalR + Redis backplane |

That last row is not a footnote — this repo has the bug. `ContentionHub` uses SignalR
groups, which are per-process, so at three replicas a client connected to instance 2
misses events published on instance 1 ([ADR-0009](../architecture/adr/0009-signalr-for-live-timeline.md)).
Same class of failure as the in-memory lock, in a different costume. Being able to spot
that pattern across unrelated technologies is what the interview question is really
testing.

### And note what scaling out does NOT fix

Three replicas triple your capacity for *concurrent requests*. They do nothing for:

- a single slow query,
- a hot row every instance contends on (layer 3),
- an operation that must happen exactly once,
- and they **triple** the load on your database.

---

## Layer 3 — the data

Once instances are cheap, the database is the wall. And the limit usually isn't CPU —
it's **contention**.

`loadtests/k6/hot-account.js` offers identical load two ways: every withdrawal aimed at
one account, versus spread across many. Same requests, same work, very different
throughput — because writes to one row serialise **no matter how many replicas you
run**.

This is where the two strategies' failure modes diverge (lesson 05):

- **Optimistic** degrades into a retry storm: N× the work for 1× the throughput.
- **Pessimistic** degrades into a queue: throughput capped at one transaction at a time.

Neither is fixed by more instances. Both are fixed by making the row less hot:

- **Shard the counter** — 10 rows summed on read instead of 1 row contended by everyone.
- **Queue the writes** — accept fast, apply serially, turn contention into latency you control.
- **Event-source it** — append immutable events; appends don't conflict.
- **Batch** — 100 changes in one transaction, not 100 transactions.
- **Read replicas** — for read-heavy loads. They do nothing for write contention.

### Retries are their own problem

At scale, retries are not hypothetical: timeouts, load-balancer retries, double-clicks,
at-least-once queue delivery. Everything in lessons 01–06 makes *concurrent* operations
safe. **None of it makes a *repeated* operation safe.**

```bash
curl -X POST localhost:5080/api/lab/idempotency -H 'Content-Type: application/json' \
  -d '{"retries":5,"amount":100,"startingBalance":1000,"useIdempotencyKey":false}'
```

```
applied 5x, final=500, expected=900
Every retry was individually correct and concurrency-safe; the customer was still
charged 5 times.
```

With `"useIdempotencyKey": true`: applied **once**, balance 900. The guarantee comes from
a **uniqueness constraint in the database**, not from application logic — checking "have
I seen this key?" then inserting is the read-modify-write race from lesson 01 wearing a
different hat.

---

## The 60-second interview answer

> "It's three questions. First, the code: I'd make sure it's async all the way down —
> in this repo, one endpoint doing `.Result` instead of `await` gets 35 requests per
> second where the async one gets 979, same hardware, because blocking starves the
> thread pool. No amount of infrastructure fixes that.
>
> Second, instances: once the code is efficient, scale out horizontally. But that only
> works if it's stateless — I've got a slice using an in-memory lock that's provably
> correct on one instance and silently wrong on three, with no code change. Kubernetes
> multiplies instances; it doesn't make un-scalable code scalable.
>
> Third, data: at that point the database is the bottleneck, and usually it's row
> contention rather than CPU. That's where you shard the hot row, queue the writes, or
> add read replicas — and you need idempotency keys, because at that scale retries will
> duplicate operations."

Then the honest closer, which is what separates a senior answer:

> "But I'd want to know which of the three is actually the problem before touching any
> of it. Most 'we need to scale' turns out to be one N+1 query."

---

## Trade-offs

Scaling out is not free, and saying so is part of the answer:

- **Every replica multiplies database load.** Three API instances, one database.
- **Coordination gets slower.** In this repo the distributed lock took ~2.6 s of total
  waiting where the in-process semaphore took ~8 ms — correctness across instances costs
  a round-trip per acquire.
- **Debugging gets harder.** Which instance served that request? You need correlation
  ids and aggregated logging before you need three replicas.
- **Stateless costs infrastructure.** Session state → Redis. SignalR → a backplane. Jobs
  → a queue with leader election. Each is another thing to run and to break.
- **Vertical scaling is underrated.** A bigger machine is often cheaper than a
  distributed system, and it is always simpler. Do it until it stops being enough.

---

## What an interviewer asks next

*"How do you know which layer is the problem?"* — Measure. Traces show where time goes;
if latency is flat under load, you're CPU-bound; if it climbs with concurrency while the
CPU is idle, you're blocking or contending.

*"What breaks first when you scale out?"* — Anything in process memory: locks, caches,
sessions, counters, scheduled jobs, SignalR groups. Then the database connection count.

*"Do you need Kubernetes?"* — Usually not. Compose with a few replicas, or a managed
container service, gets you a long way. K8s is an operations decision, not a performance
one — which is why this repo deliberately doesn't use it
([ADR-0014](../architecture/adr/0014-compose-not-kubernetes.md)).

*"How would you handle 1,000,000 users?"* — Refuse to answer in the abstract. 1M users
doing what, how often, read or write? 1M users reading a cached homepage is a CDN
problem. 1M users writing to one row is a data-modelling problem. Asking that back is
the correct answer.
