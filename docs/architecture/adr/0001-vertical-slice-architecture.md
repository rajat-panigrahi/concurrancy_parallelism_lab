# ADR-0001 — Vertical slice architecture, not layered/Clean

- **Status:** Accepted
- **Date:** 2026-08-30

## Context

This repository exists to teach concurrency. Every structural decision is judged by
one question: *does it make the concurrency easier or harder to see?*

The default in .NET shops is a layered solution — `Bank.Api`, `Bank.Application`,
`Bank.Domain`, `Bank.Infrastructure` — often labelled Clean or Onion architecture.
It is what most interviewers have in their own codebase, so choosing against it
needs a reason.

The concurrency lessons here have an awkward shape for layering. `NaiveWithdraw` and
`OptimisticWithdraw` are the *same feature* written two ways. What differs between
them is precisely the thing layering hides: how the read, the decision and the write
are sequenced against the database.

## Options considered

- **Layered / Clean.** Familiar to every .NET reviewer. Enforces dependency direction
  at the project level, so infrastructure can't leak into the domain. Swappable
  persistence.
- **Vertical slice.** One folder per feature holding its request, handler, endpoint
  and tests. Related code sits together; unrelated code stays away.
- **Modular monolith.** A middle path — vertical modules with enforced boundaries
  between them.

## Decision

Vertical slice. `Features/<Area>/<Slice>/` owns everything for that slice, with
genuinely shared machinery (contention recording, persistence, locking) in `Shared/`.

## Why not the others

- **Layered** loses the plot for this specific project. A withdrawal would be spread
  across four projects, and the read-modify-write sequence — the actual bug — would
  be split between an application service and a repository, in different assemblies.
  You'd have to hold four files in your head to see one race. Worse, the repository
  abstraction actively obscures the lesson: `IAccountRepository.Update(account)` looks
  identical whether it's a blind overwrite or a version-checked update, and that
  difference is the entire subject of ADR-0007.
- **Modular monolith** solves a problem we don't have. Its value is enforcing
  boundaries between *teams* and *bounded contexts*. There is one context here
  (accounts) and one reader (you). The ceremony would buy nothing.

## Trade-offs accepted

- **No compile-time enforcement of dependency direction.** In a layered solution the
  compiler stops the domain referencing EF Core. Here nothing does; discipline is by
  convention only. On a real team that discipline erodes.
- **Duplication between slices is expected and tolerated.** `OptimisticWithdraw` and
  `PessimisticWithdraw` repeat structure. In production you'd factor that out; here
  the duplication is deliberate, because each slice must be readable start to finish
  without chasing a shared base class.
- **It is not what most interviewers expect**, so you may have to justify it. That is
  a real cost, partially mitigated by this ADR existing.

## Consequences

- Adding a slice never edits shared files — hence the `IEndpoint` reflection scan in
  `Shared/Endpoints/EndpointExtensions.cs`. That reflection is the price of the
  property that a feature folder can be deleted with no other change.
- Tests mirror the slice layout under `tests/`, so a slice's tests are found by
  path, not by naming convention.
- If this repo later grows the microservice split mentioned in the roadmap, slices
  are already the natural seam — each is a candidate service with no shared
  application layer to untangle.

## Interview angle

**The 60-second version:** "Layering organises by technical role — controllers here,
repositories there. Vertical slice organises by reason to change. For a feature that
touches all four layers, layering means four files across four projects; the slice
means one folder. I use vertical slice when features are the unit of change and I
use layering when I need enforced dependency rules across teams. Here, the whole
point was making a read-modify-write sequence visible in one file, so slices won."

**What a good interviewer asks next:** *"How do you stop slices duplicating logic
until it rots?"* The honest answer is that you don't stop it, you watch it — you
extract to `Shared/` only when the third slice needs the same thing, and you accept
that vertical slice trades a little duplication for a lot of locality. If they push
on testability, note that slices are *more* testable in isolation, not less: there's
no layered mock chain to build.
