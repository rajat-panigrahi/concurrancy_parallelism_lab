# Concurrency & Parallelism Lab

A small banking app where every concurrency concept is a **runnable experiment with a
visible verdict** — built to prepare for .NET interviews, and to fix the mental model
rather than memorise definitions.

You start five withdrawals against an account holding ₹100 and watch, event by event,
who read what, who wrote, **who won, and who silently lost**.

> **Status:** complete. All six milestones are built, tested and merged — 48 tests, 12
> lessons, 16 ADRs, and a working UI. [`docs/PLAN.md`](docs/PLAN.md) keeps the original
> plan alongside what actually shipped and where the two diverged.

---

## The mental model

Everything hangs off one image.

|  | Concurrency | Parallelism |
|---|---|---|
| Banking picture | **One teller juggling many customers.** Serve A; while A signs a form, turn to B. | **Many tellers at many counters.** Genuinely at the same time. |
| In code | `async` / `await` over I/O | `Parallel.ForEach` / PLINQ over CPU work |
| Needs more cores? | No | Yes |
| Progress is | interleaved | simultaneous |
| Wins you | throughput while waiting | speed while computing |

Three things worth committing to memory:

- **`async` is not parallel, and `Task.Run` is not `async`.** The most common
  interview trap in the topic.
- **A race condition isn't "two things at once."** It's *read → think → write* where
  somebody else wrote during your think. Nothing has to be simultaneous.
- **Optimistic** = collisions are rare, so detect and retry. **Pessimistic** =
  collisions are common, so queue up and wait.

## Documentation

| Where | What it answers |
|---|---|
| [`docs/lessons/`](docs/lessons/) | **How** it works — the teaching narrative, in plain developer language |
| [`docs/architecture/`](docs/architecture/) | **Why** it's built this way — ADRs with the trade-offs and the rejected options |
| [`docs/architecture/system-overview.md`](docs/architecture/system-overview.md) | Diagrams: what talks to what, and where mutable state lives |
| [`docs/PLAN.md`](docs/PLAN.md) | The original plan, and what shipped differently |

## Getting started

### Prerequisites

- .NET 8 SDK
- PostgreSQL 16 running locally
- Node 20+ (only for the Angular UI)

### Database

```bash
# create the role and both databases (app + tests)
sudo -u postgres psql -c "CREATE ROLE banklab LOGIN PASSWORD 'banklab' CREATEDB;"
sudo -u postgres createdb -O banklab banklab
sudo -u postgres createdb -O banklab banklab_test

# apply migrations
dotnet ef database update --project src/Bank.Api
```

Override the connection strings with `ConnectionStrings__BankDb` (app) and
`BANKLAB_TEST_DB` (tests) if your local setup differs.

### Run

```bash
dotnet run --project src/Bank.Api      # http://localhost:5080, Swagger at /swagger
dotnet test                            # the whole suite

cd ui/bank-lab-ui && npm ci && npm start   # http://localhost:4200
```

Open the UI and hit **Run** on the Race lab. Five people withdraw ₹100 from an account
holding ₹100; all five are approved; the balance reads ₹0. The timeline shows where the
other ₹400 went.

### Try it without the UI

```bash
# the bug
curl -X POST localhost:5080/api/lab/runs -H 'Content-Type: application/json' \
  -d '{"strategy":"naive","actors":5,"amountEach":100,"startingBalance":100,"forceRace":true}'

# the same input, fixed — swap naive for lock, optimistic, pessimistic or distributed-lock
curl localhost:5080/api/lab/strategies

# a real deadlock, then the one-line fix
curl -X POST localhost:5080/api/lab/deadlock -H 'Content-Type: application/json' \
  -d '{"orderLocks":false,"holdBetweenLocksMs":100}'

# thread-pool starvation: 979 RPS vs 35 RPS on identical hardware
dotnet run --project tests/Bank.LoadTests -c Release -- --seconds=10
```

## What it measures

Every number below was produced by running this repo on a 4-core VM.

| Question | Answer | Where |
|---|---|---|
| What does a lost update cost? | 5 approved, ₹400 unaccounted for, **4 silent losers** | Race lab |
| Optimistic vs pessimistic? | 9 attempts / 4 conflicts / **0 ms waiting** vs 5 attempts / 0 conflicts / **244 ms waiting** | Compare |
| Does parallelising help? | Per-item `lock`: **11× slower than one core**. Thread-local sums: **3.68× faster** | Parallelism lab |
| What does `async` buy? | 10 × 200 ms calls: **2005 ms → 204 ms**, on 5 threads, 4 cores | Parallelism lab |
| Is `Task.Run` async? | No — **1,857× slower** than just calling the method | `docs/benchmarks/` |
| Can it scale? | One `.Result` instead of `await`: **979 → 35 RPS**, p99 105 ms → 3531 ms | Scale lab |
| Are retries safe? | Without an idempotency key, 5 retries of one withdrawal charged **5×** | Scale lab |

## The UI

Angular 19, standalone components and signals. Deliberately dumb — it calls the API and
animates what comes back; every verdict is computed server-side.

| Page | What it does |
|---|---|
| **Mental model** | Animated one-teller vs four-tellers, running off one clock |
| **Race lab** | Swimlane per actor, live over SignalR. Trophy on the winner, red strike on the silent losers, and a big expected-vs-actual verdict card |
| **Compare** | Every strategy on identical input, side by side |
| **Parallelism lab** | Six aggregation strategies, and sequential vs `Task.WhenAll` |
| **Scale lab** | The three layers, with the load-test numbers and the deadlock/idempotency demos |
| **Learn** | The lesson map plus flashcards for the night before |

## Repository layout

```
src/Bank.Api/Features/<Area>/<Slice>/  one folder per concurrency lesson
src/Bank.Api/Shared/                   contention recording, persistence, locking
src/Bank.Benchmarks/                   BenchmarkDotNet — micro, in-process
tests/Bank.Api.UnitTests/              deterministic + invariant tests
tests/Bank.Api.IntegrationTests/       real PostgreSQL
tests/Bank.LoadTests/                  the load harness — macro, over HTTP
ui/bank-lab-ui/                        Angular presentation layer
deploy/                                Dockerfiles, compose, nginx
```

Vertical slice, not layered — and [ADR-0001](docs/architecture/adr/0001-vertical-slice-architecture.md)
explains why, including the case against.

## Working on this with Claude Code

The repo ships its own [Claude Code](https://claude.com/claude-code) setup, because a lot
of what makes this codebase work is not derivable from reading it — **parts of it are
broken on purpose and must stay that way.**

| Path | What it does |
|---|---|
| [`CLAUDE.md`](CLAUDE.md) | Loaded every session: the commands, the "do not fix these" list, and the conventions that differ from .NET defaults |
| [`.claude/rules/`](.claude/rules/) | Four topic files, each **path-scoped**, so they load only when Claude touches `src/`, `tests/`, `docs/` or `ui/` and cost nothing otherwise |
| [`.claude/commands/`](.claude/commands/) | `/lab` runs a strategy and explains the verdict · `/adr` scaffolds the next ADR · `/measure` regenerates the benchmarks and refreshes every quoted number |
| [`.claude/skills/add-slice/`](.claude/skills/add-slice/) | The full checklist for adding a concurrency slice — handler, registration, three test tiers, lesson, ADR. Loads only when used |
| [`.claude/agents/`](.claude/agents/) | `concurrency-reviewer`: reviews a diff for *this* repo's hazards — gate placement, an accidentally "fixed" deliberate bug, absolute timing assertions |
| [`.claude/hooks/`](.claude/hooks/) | Warns when an ADR is missing its mandatory *Why not the others* / *Trade-offs accepted* / *Interview angle* sections |
| [`.claude/settings.json`](.claude/settings.json) | Pre-approves the commands this repo runs constantly, and wires the hook |

The rules encode real failures from building this: gating inside a critical section
(deadlock), an absolute timing assertion (flaked under load), and an unread primary
constructor parameter (`TreatWarningsAsErrors` makes CS9113 a build failure).

**Deliberately not included.** `.mcp.json` — there is nothing external to integrate, since
PostgreSQL is reached through EF Core rather than MCP. Generic `code-reviewer` and
`security-auditor` agents — Claude Code already ships `/code-review` and
`/security-review`, and duplicates would compete with them. `CLAUDE.local.md` and
`.claude/settings.local.json` are gitignored personal overrides; create them yourself if
you want machine-specific settings.

## A note on dependencies

This repo deliberately avoids libraries whose licence would stop a reader using the
same approach at work. MediatR, FluentAssertions and NBomber are all commercially
licensed now, so the project uses plain handler classes, Shouldly, and a small in-repo
load harness instead. The reasoning is in
[ADR-0002](docs/architecture/adr/0002-no-mediatr.md).
