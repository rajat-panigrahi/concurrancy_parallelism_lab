# ADR-0008 — A test-controlled interleaving seam in production code

- **Status:** Accepted
- **Date:** 2026-08-30

> The most arguable decision in this repository. If one ADR is worth reading properly,
> it's this one.

## Context

The project's core claim is that it makes race conditions *visible*. That requires
races that actually happen — and by nature a race depends on timing nobody controls.

Spawn five actors and hope they collide, and you get:

- **Flaky tests.** The lost update reproduces sometimes. In CI, on a busy shared
  runner, the interleaving differs from your laptop. A test that fails 1 run in 20
  gets muted, and a muted test is worse than no test because it looks like coverage.
- **An unreliable demo.** Clicking "run" in front of an interviewer and getting the
  *correct* answer from deliberately broken code is the opposite of the point.

Something has to control where the actors meet, and that something has to reach inside
the handler — between the read and the write — because that is where the gap is.

## Options considered

- **Statistical brute force.** Run 1,000 actors and assert the bug appears at least
  once. No production code changes.
- **`Thread.Sleep`/`Task.Delay` in the handler**, widening the gap so collisions are
  likely.
- **Test-only subclass or a virtual hook** overridden in tests.
- **An injected seam** — `IInterleaveGate` — called at named checkpoints, a no-op in
  production and a barrier when a run opts in.

## Decision

The injected seam. Handlers contain one extra line:

```csharp
await gate.ReachAsync(command.RunId, InterleaveCheckpoints.AfterRead, cancellationToken);
```

`LabInterleaveGate` returns `Task.CompletedTask` after a single dictionary lookup
unless a run has explicitly registered itself as a forced race.

## Why not the others

- **Statistical brute force** is slow, and it is still not deterministic — it makes
  failure unlikely rather than impossible. Worse, it inverts the assertion: "the bug
  appeared at least once in 1,000 runs" tells you nothing about *which* interleaving
  caused it, so the test can't distinguish a lost update from an unrelated fault. It
  also cannot express the interesting cases at all — "all five actors read before any
  writes" is a specific interleaving, not a frequent one.
- **Sleeps in the handler** are the worst option and the most commonly reached for.
  They make the race *likelier*, never certain; they slow every run including
  production; and the delay is a magic number that is simultaneously too short on a
  loaded CI box and too long everywhere else. They are also permanent production
  latency in exchange for a test-time property.
- **A test-only subclass** doesn't reach far enough. The seam must sit *inside* a
  method, between two statements. Overriding the whole method means the test exercises
  a copy of the logic rather than the logic, which defeats the purpose.

## Trade-offs accepted

Stated plainly, because this is the decision most open to challenge:

- **There is test-shaped code on the production path.** Every withdrawal handler calls
  a gate that exists for the benefit of tests and demos. A reviewer is entitled to call
  that a leak of test concerns into the domain, and they would not be wrong.
- **It costs something on every request** — a `ConcurrentDictionary` lookup and an
  already-completed `Task`. Tiny, not zero.
- **It is a new way to hang the system.** Proven during development: the locking
  handler originally gated *inside* its critical section, so the actor holding the lock
  waited at a barrier for four actors who could not enter until it released. A real
  deadlock. Mitigated two ways — the gate has a 5-second cap after which it stops
  forcing, and `InterleaveCheckpoints.BeforeAcquire` exists specifically so locking
  handlers gate before acquiring. The mitigation is real but the hazard is inherent:
  a hook that controls scheduling can deadlock the thing it hooks.
- **It could be misused.** Nothing stops someone calling `ForceRace` in production.

## What makes it defensible anyway

Two things:

1. **It is not test-only.** The lab's `forceRace` flag is a product feature — the whole
   point of the UI is a race that reproduces on demand. The seam has a legitimate
   runtime caller, which changes it from "test code in production" to "a feature tests
   also use".
2. **The alternative is worse.** Without it the project's central claim — deterministic,
   reproducible race conditions — is false.

In a real banking service, none of this would apply and the honest answer is that the
hook should not ship. You would put it behind `#if DEBUG`, a feature flag stripped at
build time, or an `InternalsVisibleTo` seam. That it stays compiled in here is a
property of this being a teaching lab, not a recommendation.

## Consequences

- Every withdrawal handler takes `IInterleaveGate` and calls it at exactly one
  checkpoint, chosen to make *its* contention certain: `AfterRead` for lock-free
  strategies, `BeforeAcquire` for locking ones.
- Gating in the wrong place deadlocks, so checkpoint choice is part of writing a slice,
  not an afterthought.
- `AsyncBarrier` exists because `System.Threading.Barrier` blocks its thread — using it
  would mean N thread-pool threads blocked waiting on each other, which is the
  starvation bug lesson 09 is about. The lab would cause the problem it teaches.
- Tests assert the race happens across 25 consecutive runs and pass every time.

## Interview angle

**The 60-second version:** "Race tests are usually flaky because the interleaving is
left to the scheduler. I made it a parameter instead: an injected gate that's a no-op
in production, and in a lab run holds every actor after its read until all of them have
read. The lost update then reproduces 100% of the time. The cost is a testability hook
on the production path — in a real service I'd compile it out — and the benefit is that
'test a race condition' stops meaning 'run it a lot and hope'."

**What a good interviewer asks next:** *"Isn't that test code in production?"* Yes —
concede it immediately rather than defending it, then give the mitigation (compile it
out, feature-flag it) and the reason it earns its place here (the lab's forced-race
mode is a real feature). Candidates who won't name the downside of their own design
read as inexperienced.

The other likely follow-up: *"What else can you use to test concurrent code?"* Worth
knowing that deterministic schedulers exist for this — CHESS historically, and
Microsoft's Coyote today, which systematically explores interleavings rather than
sampling them. Naming Coyote signals you know this is a solved problem class and that
a hand-rolled barrier is the small version of it.
