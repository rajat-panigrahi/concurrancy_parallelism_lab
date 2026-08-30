# ADR-0005 — PostgreSQL as the database

- **Status:** Accepted
- **Date:** 2026-08-30

## Context

Half the slices need a database whose concurrency behaviour is genuine: a working
optimistic concurrency token, real row-level locking (`SELECT … FOR UPDATE`), real
deadlock detection, and something usable as a distributed lock (ADR-0012).

Most .NET shops run SQL Server, so that deserves serious weight — the point is to
prepare for interviews where SQL Server is the house database.

## Options considered

- **SQL Server.** The .NET default. `rowversion`/`timestamp` is the concurrency token
  most .NET developers have actually used; `UPDLOCK`/`HOLDLOCK` hints are widely
  quoted in interviews. Runs in Docker; EF Core support is first-class.
- **PostgreSQL.** Free, no licence acceptance, small footprint, `xmin` gives a
  concurrency token with no schema column, `SELECT … FOR UPDATE` is standard SQL, and
  advisory locks provide a distributed lock with no extra infrastructure.
- **SQLite.** Zero setup, file-based, trivially fast tests.

## Decision

PostgreSQL 16, with a `Version` property mapped onto the system column `xmin`.

## Why not the others

- **SQLite** is disqualified on capability. Its locking is file/database-level, not
  row-level — writers serialise across the whole database, so a "pessimistic lock on
  one account" is indistinguishable from a lock on every account. That would teach a
  false model of what row locking costs. It has no `SELECT … FOR UPDATE` and no
  meaningful deadlock scenario. Fine for CRUD tests; useless for this subject.
- **SQL Server** was the close call, and lost on friction rather than merit. The image
  is large, it requires accepting a EULA, it needs more memory than a lab container
  reasonably has, and — decisively for this environment — the Docker daemon is
  unavailable here while PostgreSQL 16 was already installed and startable natively.
  Its `rowversion` token is genuinely the more familiar one to .NET developers, which
  is why ADR-0006 covers the difference explicitly rather than pretending `xmin` is
  the only way.

## Trade-offs accepted

- **`xmin` is Postgres-specific.** The optimistic slice does not port to SQL Server
  unchanged — you'd add a `byte[] RowVersion` property with `[Timestamp]`. ADR-0006
  documents the diff so the lesson survives the port.
- **Advisory locks are Postgres-specific too**, so the distributed-lock slice
  (ADR-0012) is not portable either.
- **We teach against a database many readers don't run at work.** The *concepts*
  transfer completely; the syntax doesn't. Every database lesson therefore states the
  SQL Server equivalent alongside.
- `numeric(18,2)` and `decimal` behave slightly differently from SQL Server's
  `decimal(18,2)` at the edges. Irrelevant at lab scale, worth knowing at real scale.

## Consequences

- `BankDbContext` maps `Version` to the `xmin` system column with type `xid`. Verified
  in `FoundationTests`: Postgres assigns it on INSERT and changes it on every UPDATE,
  and a stale writer gets `DbUpdateConcurrencyException`.
- Because `xmin` is a system column (`pg_attribute.attnum = -2`), Npgsql's migration
  generator correctly emits **no DDL** for it — the token costs nothing in schema.
- The pessimistic slice uses standard `SELECT … FOR UPDATE`, which does port to SQL
  Server as `WITH (UPDLOCK, ROWLOCK)`.
- Tests run against a local PostgreSQL instance (ADR-0015), with `banklab` and
  `banklab_test` as separate databases.

## Interview angle

**The 60-second version:** "I picked Postgres because it gives real row-level locking,
a free concurrency token in `xmin`, and advisory locks for distributed locking without
adding Redis. If the shop runs SQL Server the concepts map one-for-one — `xmin`
becomes `rowversion`, `SELECT … FOR UPDATE` becomes `WITH (UPDLOCK, ROWLOCK)`."

**What a good interviewer asks next:** *"What's the difference between `xmin` and
`rowversion`?"* — see ADR-0006. Or the sharper one: *"Why not SQLite for tests?"* The
answer that lands is that SQLite locks at database granularity, so a test proving
"pessimistic locking works" would pass for the wrong reason and hide the cost that
matters, which is how much concurrency the lock destroys.
