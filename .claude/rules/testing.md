---
paths:
  - "tests/**"
---

# Testing rules

Assertions use **Shouldly**. FluentAssertions is not available here — it is commercially
licensed from v8 and was deliberately not taken.

## Never assert an ordering you did not force

Legitimate: `summary.Winners.Count.ShouldBe(1)` — ₹100 funds exactly one ₹100 withdrawal
under any interleaving.

Not legitimate: `winners[0].ActorId.ShouldBe("actor-3")` — that is asserting the
scheduler's mood, and it will fail eventually.

## Timing assertions

- **Relative only.** `whenAll.DurationMs.ShouldBeLessThan(sequential.DurationMs / 2)`
  survives a uniformly slower machine. `ShouldBeLessThan(400d)` does not.
- **Absolute lower bounds are fine** when derived from the work itself — four batches of
  100 ms cannot finish in under 400 ms, however fast the machine.
- **Never assert a performance ratio.** "Thread-local aggregation is 2× faster than
  per-item locking" is a property of the machine, not the code. Two such assertions were
  deleted in M5 after failing under CPU load. Performance claims belong in
  `src/Bank.Benchmarks`, which has warmup, repeats and error bars.
- Any class asserting on elapsed time joins `TimingSensitiveCollection`, which disables
  parallelisation so the test is not competing with three others for the same cores.

## The three tiers

| Tier | Where | May assert |
|---|---|---|
| Deterministic | unit tests, `forceRace: true` | a *specific* interleaving and its exact consequence |
| Invariant / chaos | unit tests, free-running | only what holds under *every* interleaving |
| Integration | `Bank.Api.IntegrationTests`, real PostgreSQL | concurrency tokens, row locks, deadlocks |

The invariants, reused across strategies: money is conserved, the balance never goes
negative, no approved withdrawal is silently erased.

## Keep the suite honest

`InvariantSuiteHasTeethTests` points the same invariants at the naive handler and
**requires them to fail**. If those tests start passing, the suite has stopped applying
pressure. Do not "fix" them by relaxing the assertion.

## Integration tests

They need a real PostgreSQL (`banklab_test`, or `BANKLAB_TEST_DB`). Do not reach for the
EF Core in-memory provider as a shortcut: it implements neither concurrency tokens nor
row locks, so the tests would pass against a system that is still broken.

Create uniquely-numbered accounts per test rather than assuming an empty table.
