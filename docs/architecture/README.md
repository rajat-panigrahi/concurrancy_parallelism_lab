# Architecture

This folder answers **"why is it built this way?"** — not "how does it work".
For "how", read [`../lessons/`](../lessons/).

The split matters. When you are learning a topic you want a narrative. When you are
in an interview you want a *defensible decision with its trade-offs already thought
through*. Those are different documents, so they live apart.

## Contents

| Doc | Read it when |
|---|---|
| [`system-overview.md`](system-overview.md) | You want the shape of the system in one page — diagrams, where state lives, what talks to what |
| [`trade-off-matrix.md`](trade-off-matrix.md) | Night before an interview. Every decision, what we gave up, and **when it breaks down** |
| [`adr/`](adr/) | You want the full reasoning behind one specific decision |

## Architecture Decision Records

An ADR records a decision *at the moment it was made*, with the reasoning still
warm. It is not documentation of the code — the code documents itself. It is
documentation of the **argument**.

Every ADR here follows the same template, and two sections are mandatory:

- **Why not the others** — a decision you can't argue against isn't a decision, it's
  a default you inherited.
- **Trade-offs accepted** — every real choice makes something worse. An ADR with no
  downside listed is a rationalisation.

Each also carries an **Interview angle**: how to defend it in 60 seconds, and the
follow-up question a good interviewer asks next. That second part is the one worth
rehearsing — anyone can state a choice; seniority shows in knowing its limits.

### Index

| ADR | Decision | Status |
|---|---|---|
| [0001](adr/0001-vertical-slice-architecture.md) | Vertical slice architecture, not layered/Clean | Accepted |
| [0002](adr/0002-no-mediatr.md) | Plain handler classes, no MediatR | Accepted |
| [0003](adr/0003-target-dotnet-8-lts.md) | Target .NET 8 LTS | Accepted |
| [0004](adr/0004-hybrid-persistence.md) | Hybrid persistence: in-memory *and* PostgreSQL | Accepted |
| [0005](adr/0005-postgresql-over-sqlserver-sqlite.md) | PostgreSQL as the database | Accepted |
| [0006](adr/0006-xmin-concurrency-token.md) | `xmin` as the concurrency token | Accepted |
| [0007](adr/0007-optimistic-by-default.md) | Optimistic by default, pessimistic by exception | Accepted |
| [0008](adr/0008-interleave-gate-in-production-code.md) | A test-only interleaving seam in production code | Accepted |
| [0009](adr/0009-signalr-for-live-timeline.md) | SignalR for the live timeline | Accepted |
| [0010](adr/0010-three-perf-tools.md) | Three perf tools, three different jobs | Accepted |
| [0011](adr/0011-angular-signals-no-ngrx.md) | Angular standalone + signals, no NgRx | Accepted |
| [0012](adr/0012-postgres-advisory-locks.md) | Postgres advisory locks for distributed locking | Accepted |
| [0013](adr/0013-in-memory-ring-buffer.md) | Contention events in a bounded ring buffer | Accepted |
| [0014](adr/0014-compose-not-kubernetes.md) | Docker Compose replicas, Kubernetes excluded | Accepted |
| [0015](adr/0015-local-postgres-over-testcontainers.md) | Local Postgres + Respawn over Testcontainers | Accepted |
| [0016](adr/0016-three-tier-test-strategy.md) | Three-tier test strategy | Accepted |

## Template

```markdown
# ADR-000N — <decision>

- **Status:** Accepted | Superseded by ADR-XXXX
- **Date:** YYYY-MM-DD

## Context
The forces at play: what problem, what constraints, what we knew at the time.

## Options considered
Each option with an honest case *for* it.

## Decision
What we chose.

## Why not the others
The specific disqualifier for each rejected option.

## Trade-offs accepted
What this decision makes *worse*. Stated plainly.

## Consequences
What this now forces or forbids downstream.

## Interview angle
The 60-second defence, and the follow-up a good interviewer asks next.
```
