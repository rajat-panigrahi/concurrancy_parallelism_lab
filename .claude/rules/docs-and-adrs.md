---
paths:
  - "docs/**"
---

# Documentation rules

Two sets that must reinforce, never duplicate:

- `docs/lessons/` — **how** it works. Plain developer language, banking analogies.
- `docs/architecture/` — **why** it was built this way. ADRs and the trade-off matrix.

## Every measured claim must be real

A number in a lesson must have been produced by an actual run in this repo. Do not write
"roughly 10× faster" from memory or from general knowledge — run it, or cite the
committed figure in `docs/benchmarks/`.

State the conditions when they matter. The benchmark reports carry an explicit caveat
that they came from a 4-core shared VM under a `ShortRun` job with wide error bars, good
only for order-of-magnitude comparisons. A benchmark whose conditions you cannot state is
a number, not evidence.

Never claim the 3-replica demo was executed. It has only been `docker compose config`
validated — there is no Docker daemon in this environment.

## ADR template

Numbered `NNNN-kebab-title.md` under `docs/architecture/adr/`. Required sections, in
order:

```markdown
# ADR-000N — <decision>

- **Status:** Accepted | Superseded by ADR-XXXX
- **Date:** YYYY-MM-DD

## Context
## Options considered
## Decision
## Why not the others
## Trade-offs accepted
## Consequences
## Interview angle
```

**Three of these are mandatory and a `PostToolUse` hook warns when they are missing:**
`## Why not the others`, `## Trade-offs accepted`, `## Interview angle`.

- **Why not the others** — the specific disqualifier for each rejected option. A decision
  you cannot argue against is not a decision, it is a default you inherited.
- **Trade-offs accepted** — what this makes *worse*, stated plainly. Every real choice
  costs something; an ADR with no downside listed is a rationalisation.
- **Interview angle** — the 60-second defence, then the follow-up a good interviewer asks
  next. That second half is the part worth rehearsing.

After adding an ADR, add it to the index table in `docs/architecture/README.md`, and to
`docs/architecture/trade-off-matrix.md` with its "breaks down when…" entry.

## Lessons

Each ends with **Trade-offs** and **What an interviewer asks next**. Cross-link to the ADR
that explains the design decision behind the lesson. Keep relative links working —
they are checked as part of the docs review pass.
