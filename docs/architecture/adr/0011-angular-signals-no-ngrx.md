# ADR-0011 — Angular standalone components + signals, no NgRx

- **Status:** Accepted
- **Date:** 2026-08-30

## Context

The UI's job is narrow: call the API, animate what comes back. Every verdict — who won,
who lost silently, whether money was conserved — is computed **server-side** by
`RunSummaryCalculator`, because that logic is the subject being taught and it has tests.
The UI must not reimplement or second-guess it.

So the question is how little state management can we get away with.

## Options considered

- **NgRx** — the standard Angular state library. Actions, reducers, effects, selectors,
  devtools time-travel.
- **A service holding RxJS `BehaviorSubject`s** — the pre-signals idiom.
- **Standalone components with signals** and a thin HTTP service.

## Decision

Standalone components, signals for local state, one injectable service per concern
(`LabApiService`, `ContentionHubService`). No store, no actions, no reducers.

## Why not the others

- **NgRx** would add actions, reducers, effects and selectors to a UI whose entire state
  is "the last run's result, and whether a request is in flight". Its real benefits —
  shared state across distant components, time-travel debugging, disciplined mutation in a
  large team's app — do not apply here: the pages don't share state, and each is a single
  component. The ceremony would also actively work against the repo's purpose, since a
  reader trying to understand a race condition should not first have to understand a
  reducer.
- **`BehaviorSubject`s** are what signals replaced. They work, but they need manual
  `async` pipes or subscription lifecycle management, and signals give the same thing with
  less code and automatic cleanup.

## Trade-offs accepted

- **No time-travel debugging or a devtools timeline.** For a UI about timelines, that is
  a slightly funny thing to give up — but the timeline that matters is the server's, and
  it is fetchable over REST.
- **State does not survive navigation.** Leave the Race lab and its result is gone; you
  re-run it. Acceptable for a lab where runs take milliseconds and are reproducible by
  design (forced-race mode makes them identical).
- **This would not scale to a large app.** Once several pages share mutable state, the
  "just use signals in the component" approach starts producing prop-drilling and
  duplicated fetches. This ADR is a decision for *this* app's size, and would be wrong at
  ten times the size.
- **The UI hardcodes `http://localhost:5080`.** Fine for a lab, wrong for anything
  deployed, where it should come from an environment file.

## Consequences

- Every page is one standalone component plus a template; routes lazy-load them, so the
  initial bundle stays around 85 kB gzipped.
- Animations are **plain CSS**, so `provideAnimations()` and `@angular/animations` are not
  dependencies at all. `prefers-reduced-motion` is one media query rather than something
  threaded through the animation API.
- `ContentionHubService` degrades silently: if the websocket never opens, the page still
  works, because the run response carries the whole timeline. A demo that dies because a
  socket didn't connect is a bad demo.
- The UI holds **no** business logic. It cannot disagree with the tests, because it does
  not compute anything they cover.

## Interview angle

**The 60-second version:** "Standalone components with signals, and no state library. The
UI's whole state is the last run's result and an in-flight flag — NgRx's value is shared
state across a large app and disciplined mutation in a big team, and neither applies.
Signals gave me reactivity without subscription management. I'd reach for a store when
several features genuinely share mutable state, not before."

**What a good interviewer asks next:** *"When would you actually add NgRx?"* When state
outlives a component and is read by features that don't know about each other — auth,
a cart, cross-page filters, optimistic UI updates that need rollback. The honest framing:
a store is a coordination tool, and adding one before you have a coordination problem
buys ceremony rather than safety. Which, in a repo about concurrency, is the same mistake
as reaching for a lock you don't need.
