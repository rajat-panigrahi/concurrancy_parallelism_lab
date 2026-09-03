---
paths:
  - "ui/**"
---

# Angular UI rules

Angular 19, standalone components, signals. The UI is a **presentation surface** and holds
no business logic: every verdict — who won, who lost silently, whether money was conserved
— is computed server-side by `RunSummaryCalculator`, which has tests. The UI must not
recompute or second-guess it.

- **No NgRx or any state library.** Signals in the component, one injectable service per
  concern ([ADR-0011](../../docs/architecture/adr/0011-angular-signals-no-ngrx.md)).
- **No `@angular/animations`,** and it is not a dependency. Every animation is plain CSS,
  which keeps the bundle small and makes `prefers-reduced-motion` a single media query.
  Do not add `provideAnimations()`.
- **Routes lazy-load** their components; keep it that way so the initial bundle stays
  around 85 kB gzipped.

## Two things that will bite

- **CORS covers both `localhost:4200` and `127.0.0.1:4200`.** A browser treats them as
  different origins, so allowing only one makes every call fail in a way that looks like
  the API being down. A wildcard origin is not an option because SignalR needs
  `AllowCredentials`.
- **SignalR groups are per-process.** A client cannot join a run's group before the run
  exists, since the server generates the id — hence the all-runs group. Behind more than
  one replica this needs a Redis backplane, and without one clients silently miss events.
  The live feed is an accelerator; the REST timeline stays the source of truth, and the
  page must still work when the websocket never opens.

## Verify in a browser, not just the build

`npm run build` passing does not mean the page works — the CORS bug above built fine and
failed every request. Drive the real page (Playwright against Chromium at
`/opt/pw-browsers/chromium`) and check the console is clean before claiming it works.
