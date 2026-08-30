# ADR-0012 — PostgreSQL advisory locks for distributed locking

- **Status:** Accepted
- **Date:** 2026-08-30

## Context

Lesson 02 ends on a cliffhanger: the in-process `SemaphoreSlim` is correct on one
instance and silently wrong on three. The repo needs to show the fix, and the fix is a
lock that lives somewhere all instances share.

Note this is only needed when the thing being serialised is **not a single database
row**. If it is one row, `SELECT … FOR UPDATE` (ADR-0007, lesson 04) is simpler, cheaper
and already sufficient. A distributed lock earns its place for: calling an external
provider exactly once, running one scheduled job across a cluster, or coordinating work
spanning several tables.

## Options considered

- **PostgreSQL advisory locks** (`pg_advisory_lock`) — mutual exclusion on an arbitrary
  64-bit key, in the database we already run.
- **Redis** — the most common answer, usually `SET NX PX` with a random token, or Redlock
  across multiple nodes.
- **ZooKeeper / etcd** — purpose-built coordination services with ephemeral nodes and
  proper consensus.

## Decision

PostgreSQL advisory locks, exposed through the same `IAccountLock` interface as the
in-process implementation.

## Why not the others

- **Redis** would be the obvious production choice in a system already running Redis, and
  it is faster. It loses here on two counts. First, it means adding infrastructure to
  teach one concept — the lab already has Postgres, and a lesson that requires standing
  up another service is a lesson people skip. Second, and more interesting: a
  single-node Redis lock is **not safe under failure** (if the node fails over, two
  holders can exist), and Redlock's safety is
  [actively disputed](https://martin.kleppmann.com/2016/02/08/how-to-do-distributed-locking.html) —
  it depends on timing assumptions that don't hold when a process is paused by GC or the
  network partitions. Teaching Redlock as "the way to do distributed locks" would be
  teaching something contested as though it were settled.
- **ZooKeeper/etcd** are the technically correct answer for distributed coordination —
  real consensus, ephemeral nodes that vanish when a client dies. They are also an entire
  additional system to run, and vastly disproportionate to a teaching repo.

## Trade-offs accepted

- **It is materially slower.** Measured in this repo: 5 contending actors spent ~2.6 s
  total waiting on the advisory lock versus ~8 ms on the in-process semaphore. Every
  acquire is a database round-trip. That cost is the price of correctness across
  instances, and lesson 09 states it rather than hiding it.
- **It consumes a connection for the lock's lifetime.** A session-scoped advisory lock is
  tied to its connection, so the implementation opens a dedicated `DbContext`. Enough
  concurrent lock holders and you exhaust the connection pool — the same failure mode as
  long pessimistic transactions (lesson 04).
- **The Guid key is hashed into 64 bits**, so two different accounts can collide and share
  a lock. The consequence is unnecessary blocking, never incorrectness — a false *shared*
  lock is safe; a false *distinct* lock would not be. Worth stating, because "we hashed
  the key" is exactly where a subtle bug would hide.
- **It is PostgreSQL-specific.** No equivalent in SQL Server; there you would use
  `sp_getapplock`, which behaves similarly.
- **It makes the database a coordination bottleneck** as well as a data store.

## Consequences

- `IAccountLock` has two implementations differing only in registration:

  ```csharp
  builder.Services.AddSingleton<IAccountLock, InProcessAccountLock>();   // 1 instance
  // swap for PostgresAdvisoryAccountLock                                 // N instances
  ```

  One line is the entire difference between code that scales and code that doesn't. That
  is the point of the slice, and the reason both live behind one interface.
- `WorksAcrossInstances` is exposed on the interface and surfaced through
  `/api/lab/strategies`, so the UI can label which strategies survive scale-out.
- Session-scoped (`pg_advisory_lock`) rather than transaction-scoped
  (`pg_advisory_xact_lock`), because the caller may not be in a transaction. Releasing is
  therefore our responsibility — the handle guards against double release, and disposing
  the context closes the connection, which Postgres treats as releasing every advisory
  lock it held. So the lock cannot leak even if the explicit unlock fails.

## Interview angle

**The 60-second version:** "For serialising work across instances I used a Postgres
advisory lock — mutual exclusion on an arbitrary key, in the database we already run, no
new infrastructure. Redis is the more common answer and it's faster, but a single-node
Redis lock isn't safe across failover, and Redlock's safety is genuinely disputed. And
before reaching for any distributed lock I'd ask whether the thing being protected is one
database row — because then `SELECT … FOR UPDATE` already does it, more cheaply."

**What a good interviewer asks next:** *"What happens if the process holding the lock
dies?"* This is the question that separates people who have used distributed locks from
people who have read about them. With a Postgres advisory lock, the connection drops and
the lock is released automatically — no TTL to tune, no orphan. With a Redis lock you need
a TTL, and then you have the harder problem: the TTL can expire **while you still think
you hold the lock** (a long GC pause is enough), so two processes are now in the critical
section. The mitigation is a fencing token — a monotonically increasing number the
downstream resource checks — and mentioning that is the answer that lands.
