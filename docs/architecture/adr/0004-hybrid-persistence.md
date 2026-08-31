# ADR-0004 — Hybrid persistence: in-memory *and* PostgreSQL

- **Status:** Accepted
- **Date:** 2026-08-30

## Context

The slices split cleanly into two groups that want different things from storage.

**Group A — the pure C# lessons.** `NaiveWithdraw` and `LockWithdraw` teach that a
read-modify-write sequence is unsafe, and that `lock`/`SemaphoreSlim` fixes it inside
one process. Nothing about those lessons is database-shaped.

**Group B — the database lessons.** `OptimisticWithdraw` and `PessimisticWithdraw`
teach concurrency tokens and row locks. These *are* database behaviours. Simulating
them proves nothing.

The tension: a single storage choice serves one group badly.

## Options considered

- **PostgreSQL everywhere.** Uniform, realistic, one mental model.
- **In-memory everywhere.** Zero setup, fast tests, trivial to clone and run.
- **Hybrid** — in-memory for group A, PostgreSQL for group B.

## Decision

Hybrid. `InMemoryAccountStore` (a singleton, deliberately unsafe at the account
level) backs the in-process slices; `BankDbContext` over PostgreSQL backs the
database slices.

## Why not the others

- **PostgreSQL everywhere** would smother the very first lesson. To show a lost update
  through EF Core you must first explain the change tracker, the unit of work, when
  `SaveChanges` actually emits SQL, and the fact that EF's default `UPDATE` writes only
  changed columns. A reader trying to understand *read → think → write* now has four
  EF concepts in the way, and can plausibly conclude the bug is "an EF thing" rather
  than a property of concurrent code in any language. It also makes the naive race
  harder to *produce* reliably, since transaction boundaries change the interleaving.
- **In-memory everywhere** fails the opposite way and worse: it makes the
  optimistic/pessimistic slices *lies*. `DbUpdateConcurrencyException` would never be
  raised; `SELECT … FOR UPDATE` would be a comment. Those are the two mechanisms
  interviewers probe hardest, so faking them defeats the project. Note the EF Core
  in-memory provider is not a shortcut either — it implements neither concurrency
  tokens nor row locking, so tests would pass against a system that is still broken.

## Trade-offs accepted

- **Two persistence models to hold in your head.** A reader must notice which store a
  slice uses. Mitigated by folder naming and by each slice's doc stating it up front.
- **The in-memory store duplicates concepts** the database provides — it has its own
  `Version` and its own `TryWrite` compare-and-swap. That duplication is deliberate
  (it shows optimistic concurrency is a *pattern*, not an EF feature) but it is still
  two implementations of one idea.
- **Cloning and running is not zero-setup.** You need PostgreSQL for half the slices.
- The in-memory store is **process-local**, which means the in-process slices behave
  differently under multiple replicas. That is inconvenient — and it is also exactly
  the point of ADR-0014, so we lean into it rather than fixing it.

## Consequences

- `InMemoryAccountStore` is registered as a **singleton**, because shared mutable
  process state is the precondition for the race. Registering it scoped would quietly
  delete the bug.
- Unit tests (fast, no database) cover group A; integration tests against real
  PostgreSQL cover group B. This is what makes the three-tier test strategy in
  ADR-0016 possible.
- The multi-replica demo in ADR-0014 gets a sharper result: under 3 instances the
  in-memory slices break and the database slices don't, in the same run. One
  experiment, two outcomes, one lesson.

## Interview angle

**The 60-second version:** "I used process memory where the lesson was about
in-process synchronisation, and a real database where the lesson was about database
concurrency control. Mostly that's about not letting EF's change tracker obscure a
plain read-modify-write bug — but it's also that optimistic and pessimistic
concurrency are database behaviours, and mocking them would mean testing my mock."

**What a good interviewer asks next:** *"When is in-memory state legitimate in
production?"* Good answers: caches you can afford to lose, per-request scoped state,
and single-writer designs. The trap to avoid: treating a `static
ConcurrentDictionary` as a source of truth. It works perfectly on one instance and
corrupts silently on two — which is the failure mode this repo demonstrates on
purpose.
