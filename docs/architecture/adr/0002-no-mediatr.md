# ADR-0002 — Plain handler classes, no MediatR

- **Status:** Accepted
- **Date:** 2026-08-30

## Context

Vertical slice architecture in .NET is almost always demonstrated with MediatR: the
endpoint sends a `Command`, MediatR resolves an `IRequestHandler`, pipeline behaviours
wrap it for logging, validation and transactions. It is the idiomatic pairing, and
plenty of interviewers will expect to see it.

Two things pushed against it here.

First, **licensing.** MediatR moved to a commercial licence. A public teaching
repository that people clone should not carry a paid dependency for something a
20-line class does. The same reasoning removed FluentAssertions (commercial from v8;
we use Shouldly, MIT) and NBomber (commercial subscription licence v3.0; we use a
small in-repo harness).

Second, and more important, **indirection costs comprehension.** With MediatR, the
path from HTTP request to the line that races is: endpoint → `ISender.Send` →
pipeline behaviour(s) → handler. To follow it you must know the framework. The race
this project exists to show lives at the bottom of that stack.

## Options considered

- **MediatR.** Idiomatic, gives free pipeline behaviours, decouples endpoint from
  handler, familiar to reviewers.
- **A hand-rolled dispatcher.** Same shape, no licence — an `ISender` of our own over
  a dictionary of handlers.
- **Plain handler classes injected directly** into the endpoint's delegate.

## Decision

Plain handler classes, registered in DI, injected straight into the minimal-API
delegate. No dispatcher, no pipeline, no reflection on the request path.

## Why not the others

- **MediatR** — the licence alone is disqualifying for a public teaching repo, but
  even free it would be wrong here. Its main benefit is the pipeline, and this project
  deliberately wants *no* invisible middleware around a withdrawal. If a retry or a
  transaction wraps the handler, it must be visible in the handler, because when and
  where you retry is the lesson (see ADR-0007).
- **A hand-rolled dispatcher** would reproduce MediatR's indirection while also being
  code we'd have to explain and maintain. We'd pay the comprehension cost to avoid a
  licence, and get none of MediatR's ecosystem. Worst of both.

## Trade-offs accepted

- **No pipeline behaviours.** Cross-cutting concerns — logging, validation,
  transaction scope — are either explicit in each handler or absent. In a real
  application of any size this becomes repetitive, and MediatR (or Wolverine, MIT)
  starts earning its keep.
- **Endpoints depend on concrete handler types**, not an abstraction. Swapping a
  handler means editing the endpoint. Acceptable when a slice owns both files.
- **Slightly less familiar** to reviewers who equate vertical slice with MediatR.

## Consequences

- Each slice's `Endpoint.cs` names its handler directly, so "what runs when I call
  this route" is answerable by reading two files with no framework knowledge.
- If the roadmap's saga/retry work lands later, it will need explicit orchestration
  rather than pipeline behaviours — which is arguably clearer for teaching retries
  anyway, since a retry you can see beats a retry configured somewhere else.
- The project takes a general stance: **no dependency whose licence would stop a
  reader using this at work.** Applied consistently to MediatR, FluentAssertions and
  NBomber.

## Interview angle

**The 60-second version:** "MediatR's value is the pipeline — one place for logging,
validation and transactions across every handler. I skipped it here for two reasons:
it's commercially licensed now, and this codebase exists to make the request path
obvious. Adding a dispatcher would have hidden the exact lines I wanted people to
read. On a real service with 60 handlers and cross-cutting concerns, I'd take
MediatR or Wolverine."

**What a good interviewer asks next:** *"So how do you do validation and transactions
without a pipeline?"* Explicitly, in the handler — and be ready to admit that scales
badly past a few dozen handlers. The follow-up worth pre-empting: MediatR does *not*
decouple anything meaningfully in a single deployable; the endpoint and handler still
ship together. Its real benefit is the pipeline, not the mediation.
