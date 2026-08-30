# ADR-0016 — Three-tier test strategy for concurrent code

- **Status:** Accepted
- **Date:** 2026-08-30

## Context

Testing concurrent code is different in kind, not degree. An ordinary unit test asserts
that given input X the output is Y. Concurrent code has no single output — it has a
*set* of possible outcomes determined by an interleaving the test does not control.

Two failure modes follow, and both are common in real codebases:

- **Flaky tests.** Assert on a specific interleaving and the test passes locally,
  fails in CI, gets muted, and stops protecting anything.
- **Vacuous tests.** Assert something so weak it can never fail — "no exception was
  thrown" — while the code silently corrupts data. The naive withdrawal passes that
  test perfectly while destroying ₹400.

## Options considered

- **One tier: run it many times and assert the invariant.** Simple, no infrastructure.
- **One tier: control every interleaving.** Fully deterministic, no flakiness.
- **Three tiers**, each asserting a different kind of claim.

## Decision

Three tiers, with a rule about what each may assert.

| Tier | Where | Controls timing? | May assert |
|---|---|---|---|
| **Deterministic** | `Bank.Api.UnitTests`, forced race | Yes, fully | A *specific* interleaving and its exact consequence |
| **Invariant / chaos** | `Bank.Api.UnitTests`, free-running | No | Only properties that must hold under *every* interleaving |
| **Integration** | `Bank.Api.IntegrationTests`, real PostgreSQL | Partly | Real database behaviour: concurrency tokens, row locks |

The rule that keeps tier 2 honest: **never assert an ordering you did not force.**
`summary.Winners.Count.ShouldBe(1)` is legitimate — ₹100 funds exactly one ₹100
withdrawal under any interleaving. `winners[0].ActorId.ShouldBe("actor-3")` is asserting
the scheduler's mood, and it will fail eventually.

## Why not the others

- **Invariants alone** cannot express the interesting cases. "All five actors read
  before any of them wrote" is a specific interleaving; left to chance it happens
  rarely, so a test for it would be either flaky or absent. It also can't distinguish
  *why* an invariant broke — a lost update and an unrelated bug look identical.
- **Full determinism alone** would mean every test forces an interleaving, and then
  nothing ever exercises the real scheduler. Bugs that only appear under genuine
  parallelism — a missing `volatile`, a torn read, a lock taken on only one of two
  paths — would never surface. Forced tests prove *a* case; free-running tests explore
  cases you didn't think of.

## Trade-offs accepted

- **Three tiers is more machinery** than most projects need: a gate, a barrier, a
  harness and a fixture, before a single assertion is written.
- **Tier 2 gives probabilistic coverage.** Running 32 actors a handful of times explores
  a vanishing fraction of possible interleavings. It raises confidence; it does not
  prove absence of bugs. Tools that systematically explore interleavings (Coyote) do
  better, at the cost of another dependency and a slower suite.
- **Tier 1 tests can rot into documentation.** `WhenEveryActorReadsBeforeAnyWrites_TheBankLosesMoney`
  asserts that a bug *happens*. If someone fixes the naive handler, that test fails —
  correctly, but confusingly, unless the name and comment make the intent obvious.
- **Tier 3 needs a real database**, which means the suite has an external prerequisite
  (ADR-0015).

## Consequences

- Test names state the claim, not the mechanism:
  `WhenEveryActorReadsBeforeAnyWrites_TheBankLosesMoney`,
  `TheLosersAreSilent_TheyAreToldTheySucceeded`,
  `NoMatterHowManyActors_TheInvariantHolds`.
- The invariants are stated once and reused across every strategy: money is conserved,
  the balance never goes negative, and no approved withdrawal is silently erased. A new
  strategy inherits the whole suite by implementing `IWithdrawStrategy`.
- `TheRaceIsDeterministic_SoTheTestIsNotFlaky` runs the same forced interleaving 25
  times and asserts an identical result each time — a test *about* the test
  infrastructure, guarding the property everything else depends on.
- One test deliberately shows the naive handler passing with a single actor, because
  "correct until a second user arrives" is the reason this bug reaches production.

## Interview angle

**The 60-second version:** "Three tiers. Deterministic tests force a specific
interleaving with a barrier, so a lost update reproduces every run instead of one in
twenty. Invariant tests run free and assert only what must be true under *any*
interleaving — money conserved, balance never negative — never who won, because that's
asserting the scheduler. Integration tests hit real Postgres, because concurrency
tokens and row locks are database behaviour and mocking them means testing the mock."

**What a good interviewer asks next:** *"How do you know your invariant tests actually
exercise the race?"* Honest answer: you don't, fully — that's why tier 1 exists. Run
the invariant suite against the *naive* implementation and watch it fail; if it passes,
the test isn't applying real pressure. A concurrency test suite that has never gone red
against known-broken code is unproven.

The other one: *"What's the flakiest test you've written and how did you fix it?"* This
whole ADR is that answer.
