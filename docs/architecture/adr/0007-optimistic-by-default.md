# ADR-0007 — Optimistic by default, pessimistic by exception

- **Status:** Accepted
- **Date:** 2026-08-30

## Context

Both strategies are implemented and both are correct. The repo still has to take a
position on which one a reader should reach for first, because "it depends" is not
guidance — and because the lab exists to help someone answer this in an interview,
where "it depends" without a follow-through is the answer that loses.

Measured on identical input (5 actors, ₹100 each, ₹100 balance, forced race):

| | Optimistic | Pessimistic |
|---|---|---|
| Attempts | 9 | 5 |
| Conflicts | 4 | 0 |
| Total waiting | 0.0 ms | 244.1 ms |

Both correct. One pays in wasted work, the other in waiting.

## Options considered

- **Optimistic by default**, pessimistic where contention or semantics demand it.
- **Pessimistic by default** — safest-looking, most intuitive, closest to how people
  reason about "locking".
- **No default** — decide per feature, every time.

## Decision

Optimistic is the default. Pessimistic is used deliberately, for named reasons: hot
rows, work that is expensive to redo, or where first-come-first-served is a business
rule.

## Why not the others

- **Pessimistic by default** couples correctness to *holding a resource*, and that
  coupling scales badly in ways that are invisible until they aren't. Every waiter
  occupies a connection, so a slow critical section doesn't degrade one endpoint — it
  exhausts the pool and takes down endpoints that have nothing to do with it. It also
  makes deadlock a standing risk on every multi-entity operation (lesson 06), which
  optimistic control simply doesn't have. And it is fundamentally incompatible with
  user think time: you cannot hold a row lock while someone edits a form.
- **No default** sounds principled and produces an inconsistent codebase. Different
  authors pick differently, lock ordering conventions diverge between features, and the
  deadlock risk in lesson 06 becomes real — deadlocks form *between* code paths, so
  consistency across the codebase is itself a correctness property.

## Trade-offs accepted

- **Retry logic in every optimistic write path.** It must use a fresh `DbContext`, must
  re-read *and* re-decide, and must be bounded. Each of those is a real bug if
  forgotten, and all three are easy to forget.
- **A failure mode that only appears under load.** Optimistic control looks flawless in
  testing, where nothing conflicts. The retry storm arrives in production.
- **Unfairness.** Retries have no queue, so an unlucky actor can lose repeatedly while
  others progress — starvation. Pessimistic locking is at least first-come-first-served.
- **The default is wrong for some entities**, and we're relying on the author noticing.
  The decision table in lesson 05 exists to make noticing easier, but nothing enforces
  it.

## Consequences

- `OptimisticWithdrawHandler.MaxAttempts = 5`, and exhausting it returns a refusal
  rather than looping. Unbounded retry under contention is a livelock — everything busy,
  nothing progressing — and it is harder to diagnose than a deadlock because nothing
  looks stuck.
- Only the first attempt joins the interleaving barrier. Retries must not, because by
  then some actors have finished and would never arrive, so the barrier would stall the
  rest until its timeout.
- Lesson 05 is a decision table rather than a recommendation, since the deciding
  variable is contention rate, which is a property of the workload and not of the code.
- Production advice that this repo names but does not implement: jittered exponential
  backoff between retries. Retrying immediately means the same actors collide again in
  the same order.

## Interview angle

**The 60-second version:** "Optimistic by default, because it holds no locks across
requests, so it scales horizontally and one slow actor can't stall the others. I switch
to pessimistic for hot rows — where optimistic degenerates into a retry storm doing N
times the work for the same throughput — and where redoing the work has side effects.
The deciding variable is the conflict rate, not preference."

**What a good interviewer asks next:** *"How do you know when to switch?"* Measure the
conflict rate. Rough thresholds: under ~1% of writes conflicting, optimistic is
comfortable; over ~10%, you're doing more work retrying than a lock would cost. The
better answer goes one step further: at that point, first ask whether the hot row can be
removed — shard the counter, queue the writes, or event-source it — because the best fix
for contention is usually not to arbitrate it better but to stop creating it.
