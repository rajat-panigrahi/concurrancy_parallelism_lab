# Concurrency & Parallelism Lab — a .NET interview teaching project

> ## Status: delivered
>
> **All six milestones are built, tested and merged.** This document is kept as the plan
> of record — what was intended, and where the finished work diverged from it. The
> divergences are listed under [What shipped vs what was planned](#what-shipped-vs-what-was-planned);
> everything else below reads as originally approved, in the future tense it was written in.
>
> For what the project *is* rather than what it was going to be, start at
> [`../README.md`](../README.md); for how each concept works, [`lessons/`](lessons/); for
> why it is built this way, [`architecture/`](architecture/).

## Context

You are preparing for .NET interviews and the concurrency topics don't yet have a
mental model you trust. Four things keep coming back:

1. **Concurrency vs parallelism** — you can define them but can't *show* them.
2. **Race conditions** — you want to literally *see* who read what, who wrote,
   who won, and who silently lost.
3. **Optimistic vs pessimistic concurrency** — which one, and when.
4. **"Can your app scale?"** — the one that confuses you most. Is scaling an
   application-layer concern or a Docker/Kubernetes concern? If you have 100 users
   today and 1M tomorrow, what actually has to change in the code?

The repo was empty when this was written — one commit, README + LICENSE only — so this
was a greenfield build.

The outcome: a small banking app where every concurrency concept is a **runnable
experiment with a visible verdict**, backed by tests that prove the behaviour and
short docs written the way a developer talks, not the way a spec is written. Not a
full banking product — accounts, withdrawals and transfers only, because that is
all the topic needs. Saga/retry/microservice breakdowns are deliberately left for
later; the architecture won't block them.

---

## Environment findings (these constrain real choices)

Verified in this session:

| Thing | Status | Consequence |
|---|---|---|
| `dotnet` | **not installed**; `dotnet-sdk-8.0` installable from Ubuntu apt | **Target .NET 8 LTS** |
| `builds.dotnet.microsoft.com` | **blocked by proxy (403)** | `dotnet-install.sh` fails → .NET 9/10 not obtainable here |
| nuget.org, registry.npmjs.org | reachable (200) | NuGet restore + `npm install` work |
| Node | v22.22.2 | Angular 18/19 fine |
| PostgreSQL 16 server | installed locally, currently stopped | run it **natively**, no Docker needed for tests |
| Docker CLI + `dockerd` binary | present, **daemon not running** (no `/var/run/docker.sock`) | Compose/Testcontainers files are authored + config-validated here, but the multi-replica demo runs on *your* machine |
| k6 binary | not present, cannot be downloaded through proxy | commit the k6 script; run the in-repo harness here for real numbers |
| CPU / RAM | 4 cores / 15 GB | enough for a genuine `Parallel.ForEach` speedup demo |

**Target stack:** .NET 8 LTS · ASP.NET Core Minimal APIs · EF Core 8 + Npgsql ·
xUnit + Shouldly · BenchmarkDotNet · SignalR · Angular 19
(standalone components + signals).

**No MediatR.** It went commercially licensed; a plain handler class registered in
DI is fewer moving parts and reads better as teaching code.

---

## The mental model this project teaches

Everything hangs off one banking image, repeated in code, UI and docs:

- **Concurrency** = *one teller juggling many customers.* Serve customer A, while
  A is signing a form turn to B. One thread, many in-flight operations. That's
  `async`/`await` over I/O. Progress is **interleaved**.
- **Parallelism** = *many tellers at many counters.* Genuinely simultaneous, needs
  more cores. That's `Parallel.ForEach` / PLINQ over CPU work.
- **`async` is not parallel. `Task.Run` is not `async`.** The single most common
  interview trap, and slice 8 proves it with numbers.
- A **race condition** is not "two things at once" — it's *read → think → write*
  where someone else wrote in the gap.
- **Optimistic** = "collide rarely, so detect and retry." **Pessimistic** = "collide
  often, so queue up and wait."
- **Scaling** = removing the thing that serialises. Kubernetes only multiplies your
  instances; it cannot fix code that shares mutable state in process memory.

---

## Architecture — vertical slice

Each slice is a self-contained *feature folder* **and** a self-contained *lesson*.
No `Services/`, `Repositories/`, `Models/` layer-cake. A slice owns its request,
handler, endpoint, and its own tests mirrored in the test project.

```
concurrancy_parallelism_lab/
├── ConcurrencyLab.sln
├── src/
│   ├── Bank.Api/
│   │   ├── Features/
│   │   │   ├── Accounts/OpenAccount/          # + GetAccount, SeedAccounts
│   │   │   ├── Withdrawals/NaiveWithdraw/     # slice 2 — the bug
│   │   │   ├── Withdrawals/LockWithdraw/      # slice 3 — in-process lock
│   │   │   ├── Withdrawals/OptimisticWithdraw/# slice 4 — version token + retry
│   │   │   ├── Withdrawals/PessimisticWithdraw/ # slice 5 — SELECT FOR UPDATE
│   │   │   ├── Transfers/DeadlockTransfer/    # slice 6 — deadlock + fix
│   │   │   ├── Interest/CalculateInterest/    # slice 7 — CPU parallelism
│   │   │   ├── Fraud/RunFraudChecks/          # slice 8 — async fan-out
│   │   │   ├── Withdrawals/DistributedLock/   # slice 9 — advisory lock
│   │   │   └── Lab/                           # run orchestrator, timeline, SSE/SignalR
│   │   ├── Shared/
│   │   │   ├── Contention/                    # ContentionEvent, IContentionRecorder, RunSummary
│   │   │   ├── Interleaving/                  # IInterleaveGate — makes races deterministic
│   │   │   ├── Persistence/                   # BankDbContext, InMemoryAccountStore
│   │   │   ├── Locking/                       # IAccountLock: InProcess | PostgresAdvisory
│   │   │   └── Endpoints/                     # IEndpoint + MapEndpoints() convention
│   │   └── Program.cs
│   └── Bank.Benchmarks/                       # BenchmarkDotNet console
├── tests/
│   ├── Bank.Api.UnitTests/Features/…          # mirrors slice folders
│   ├── Bank.Api.IntegrationTests/             # WebApplicationFactory + real Postgres
│   └── Bank.LoadTests/                        # in-repo load harness
├── ui/bank-lab-ui/                            # Angular 19
├── loadtests/k6/                              # committed scripts for your machine
├── deploy/                                    # Dockerfiles, compose (1 vs 3 replicas), nginx
├── docs/
│   ├── PLAN.md                                # this plan, committed & kept current
│   ├── architecture/
│   │   ├── README.md                          # index + decision map
│   │   ├── system-overview.md                 # C4-ish context/container, mermaid
│   │   ├── trade-off-matrix.md                # every decision on one page
│   │   └── adr/0001…0016-*.md                 # Architecture Decision Records
│   └── lessons/                               # 00–11, the teaching narrative
└── .github/workflows/ci.yml
```

---

## The slices

Each ships: failing test → implementation → doc page → UI experiment.

| # | Slice | Concept | Verdict it produces |
|---|---|---|---|
| 1 | `OpenAccount` / `GetAccount` / `SeedAccounts` | baseline | plumbing only |
| 2 | `NaiveWithdraw` | **lost update** | 5 actors × ₹100 on a ₹100 account → all 5 succeed, balance = −₹400 |
| 3 | `LockWithdraw` | `lock` / `SemaphoreSlim` per account | correct on 1 instance, **breaks on 3** — the scaling hinge |
| 4 | `OptimisticWithdraw` | `xmin` concurrency token, `DbUpdateConcurrencyException`, bounded retry | 1 winner, 4 conflicts, N retries, no waiting |
| 5 | `PessimisticWithdraw` | `SELECT … FOR UPDATE` inside a transaction | 1 winner, 4 blocked-then-served, measured wait ms |
| 6 | `DeadlockTransfer` | A→B and B→A locking in opposite order | real deadlock, then fixed by **ordered acquisition** |
| 7 | `CalculateInterest` | **parallelism**: sequential vs `Parallel.ForEach` vs PLINQ over 200k accounts | ~3–4× speedup on 4 cores; `Interlocked` vs `lock` for the running total |
| 8 | `RunFraudChecks` | **concurrency**: sequential `await` vs `Task.WhenAll`, `SemaphoreSlim` throttle, `CancellationToken` | 10 × 200ms calls: 2000ms → ~220ms, **on one thread** |
| 9 | `DistributedLock` | Postgres advisory lock | the answer to "how do I lock across 3 pods?" |
| 10 | `IdempotentTransfer` | idempotency key | why retries at scale duplicate money |

### Slice 2 is the centrepiece — "who wins"

Every write path calls `IContentionRecorder.Record(...)` at each step, emitting:

```
runId, actorId, accountId, seq, phase, balanceSeen, versionSeen, versionWritten,
amount, waitedMs, elapsedMicros, outcome, note
```

`phase` ∈ `Started · Read · Gate · LockWait · LockAcquired · Write · Committed ·
Conflict · Retry · Won · LostUpdate · Rejected`.

Events go into a bounded per-run ring buffer **and** broadcast to SignalR group
`run-{runId}`. When the run finishes the server computes a `RunSummary`:

```
expectedBalance, actualBalance, moneyLostOrCreated,
winners[], silentLosers[], conflicts, retries, avgWaitMs, totalMs
```

`silentLosers` is the punchline: actors whose write was overwritten *and who were
told they succeeded*. That is the thing you want to be able to point at in an
interview.

---

## TDD approach — how to test a race without flaky tests

This is the part most concurrency demos get wrong, and it's worth knowing for
interviews on its own.

**Problem:** a race test that spawns 5 tasks and hopes they collide is
non-deterministic. It passes on your laptop and fails in CI, or vice versa.

**Solution — an injected interleaving seam:**

```csharp
public interface IInterleaveGate { Task ReachAsync(string checkpoint, CancellationToken ct); }
```

- Production registration: `NoOpInterleaveGate` — zero cost, no test code in prod paths.
- Test registration: `ScriptedInterleaveGate` — a `Barrier`/`TaskCompletionSource`
  that holds every actor at `"after-read"` until *all* have read, then releases
  them. The lost update becomes **100% reproducible, every run**.

Handlers contain exactly one extra line: `await _gate.ReachAsync("after-read", ct);`

**Three test tiers:**

1. **Deterministic** (unit, in-memory, forced interleaving) — proves the exact bug.
   `NaiveWithdraw_WhenAllActorsReadBeforeAnyWrites_LosesUpdates()` asserts the race
   *happens*. It stays green forever; it documents the defect.
2. **Invariant / chaos** (100 concurrent tasks, free-running) — never asserts an
   interleaving, only invariants: *balance never negative*, *money is conserved*,
   *sum of successful withdrawals ≤ starting balance*. Run each N times.
3. **Integration** (`WebApplicationFactory` + real Postgres, `Respawn` between
   tests) — the only place optimistic/pessimistic behaviour is real. Asserts
   `DbUpdateConcurrencyException` is actually raised, and that `FOR UPDATE` really
   serialises.

**Red → green per slice, committed in that order** so the git history itself is a
teaching artifact: one commit adds the failing test, the next makes it pass.

---

## Answering "can it scale?" — the part you asked about most

Get a dedicated doc (`docs/lessons/09-scaling.md`) **and** a runnable Scale Lab, because the
honest answer is "it's three different questions and interviewers rarely say which."

**Layer 1 — application code (this is where most candidates lose the point).**
Scaling starts *before* any infrastructure. Demo: a `/sync-over-async` endpoint that
calls `.Result` next to an identical `async` one. Under sustained load, the sync one
hits **thread pool starvation** — throughput collapses and p99 latency goes off a
cliff, on identical hardware. Same CPU, same DB, 10× the throughput, purely from how
the code waits. *This* is the app-layer answer.

**Layer 2 — instances (Docker/Kubernetes).** Run 3 replicas behind nginx via
compose. Now the slice-3 in-memory `lock` **visibly breaks** — because each replica
has its own lock object. Optimistic and pessimistic slices keep working, because
their coordination point is the database. Soundbite for the interview:

> Kubernetes multiplies instances. It doesn't make un-scalable code scalable.
> Horizontal scale only works if the code is stateless and coordinates through
> shared state, not process memory.

**Layer 3 — data.** Once instances are cheap, the DB is the wall. Demo: the load harness
hammering **one hot account** vs the same load spread over 10,000 accounts. Same
RPS offered, wildly different results — because the limit is *row contention*, not
CPU. Leads to: partitioning, read replicas, queue + outbox, idempotency, and *why
optimistic concurrency degrades under high contention while pessimistic degrades
under long transactions*.

Result: a decision table you can recite.

| Situation | Use | Why |
|---|---|---|
| Conflicts rare, short transactions, web/REST/stateless | **Optimistic** | no locks held across requests, scales horizontally, cost is a retry |
| Conflicts common, hot rows, money must not be re-read | **Pessimistic** | one waiter at a time, no wasted work, cost is blocking |
| Long user "think time" (edit form open 5 min) | **Optimistic only** | never hold a DB lock across a user's coffee break |
| Single process, in-memory cache | `lock` / `SemaphoreSlim` | cheapest — but dies the moment you add a second instance |
| Multiple instances, must serialise | **Distributed lock** or push it into the DB | process memory isn't shared |

---

## Benchmarking vs load testing — your NuGet question, answered

Yes, BenchmarkDotNet is good — **for the right job**. They measure different things
and confusing them is itself an interview tell.

- **BenchmarkDotNet** (`Bank.Benchmarks`) — *micro*, in-process, nanoseconds,
  statistically rigorous, warms up the JIT. Use it for: `lock` vs `Interlocked` vs
  `SemaphoreSlim` vs `ReaderWriterLockSlim`; sequential vs `Parallel.ForEach` vs
  PLINQ; `async` state-machine overhead; `ConcurrentDictionary` vs `Dictionary`+lock.
  **Never** point it at an HTTP endpoint.
- **In-repo load harness** (`tests/Bank.LoadTests`, ~200 lines, no dependencies) — *macro*,
  over the network, RPS / p50 / p95 / p99, concurrent virtual users, sustained.
  Use it for: 1 vs 3 instances, sync-vs-async starvation, hot-row contention.
- **k6** — same job as the harness, industry standard; scripts committed under
  `loadtests/k6/` for you to run locally (its binary can't be fetched through this
  proxy).

Both write JSON into `artifacts/` which the Angular Scale Lab renders as charts.

---

## Angular UI — dumb, animated, presentational

Angular 19, standalone components, signals, `@angular/animations`. No NgRx, no
business logic — it calls the API and animates what comes back.

1. **Home — Mental Model.** Animated split screen: one teller interleaving 5
   customers (concurrency) vs 4 counters serving 4 at once (parallelism). Scrub bar.
2. **Race Lab.** The main event. Pick strategy, actor count, amount, starting
   balance, forced-race vs free-run. Hit **Run**. A **swimlane timeline** draws
   live over SignalR — one lane per actor, coloured segments for read / think /
   lock-wait / write / commit, a trophy on the winner, a red strike on silent
   losers, arrows showing which write clobbered which. Beneath: a big
   **Expected ₹X vs Actual ₹Y** verdict card that goes red when money vanishes.
3. **Strategy Compare.** Run all strategies on identical input; table of
   correctness / duration / retries / avg wait / verdict, plus "when to pick this".
4. **Parallelism Lab.** Interest run with animated per-core bars, sequential vs
   parallel timing, plus the BenchmarkDotNet table.
5. **Scale Lab.** Load-harness/k6 results as charts — sync vs async, 1 vs 3 instances,
   hot row vs spread. The thread-pool starvation cliff rendered as a line chart.
6. **Learn.** The `docs/` lessons rendered, with flip-card interview Q&A.

---

## Documentation — architect style, not tutorial style

Two distinct doc sets, because they serve two different moments. `docs/lessons/`
is *how the thing works* (read once, to learn). `docs/architecture/` is *why it was
built this way* (reread before an interview, and the format you'll be expected to
produce as a senior dev).

**This plan itself is committed** to `docs/PLAN.md` in M0 and kept current as
milestones land, so the repo explains its own roadmap without you needing this chat.

### Architecture Decision Records

Every non-obvious choice becomes a numbered ADR under `docs/architecture/adr/`,
all using the same template — the point is that **"why not" gets as much space as
"why"**, because that's the half interviewers actually probe:

```
# ADR-000N — <decision>
Status · Date
## Context            — the forces: what problem, what constraints, what we knew
## Options considered — each with an honest case FOR it
## Decision           — what we chose
## Why not the others — the specific disqualifier for each rejected option
## Trade-offs accepted— what this decision makes WORSE, stated plainly
## Consequences       — what this now forces or forbids downstream
## Interview angle    — how to defend this in 60 seconds, and the follow-up
                        question a good interviewer asks next
```

Planned ADRs:

| # | Decision | The interesting tension |
|---|---|---|
| 0001 | Vertical slice over Clean/Onion/layered | cohesion & teachability vs the layered architecture most .NET shops expect |
| 0002 | Plain handler classes, no MediatR | MediatR went commercially licensed; also one less indirection between you and the race |
| 0003 | .NET 8 LTS | forced by proxy blocking the .NET CDN — documents a real-world constraint-driven decision |
| 0004 | Hybrid persistence (in-memory + Postgres) | pedagogical clarity vs realism; why the naive race is *clearer* without EF in the way |
| 0005 | PostgreSQL over SQL Server / SQLite | SQLite can't demo real row locks; SQL Server's `rowversion` is the thing most .NET devs know — so we cover both in 0006 |
| 0006 | `xmin` as concurrency token vs `rowversion`/`byte[]` | the portability trade-off, and what changes if you move to SQL Server |
| 0007 | Optimistic as default, pessimistic as exception | contention rate is the deciding variable, not preference |
| 0008 | `IInterleaveGate` seam lives in production code | **we deliberately put a testability hook on the hot path** — cost is one interface + a no-op call; benefit is non-flaky race tests. The most debatable decision here, so it gets the fullest treatment |
| 0009 | SignalR over SSE / raw WebSocket / polling | bidirectional future-proofing and .NET-idiomatic vs SSE's radical simplicity; also: SignalR + multiple replicas needs a backplane, which is itself a scaling lesson |
| 0010 | Three perf tools, three jobs | why BenchmarkDotNet at an HTTP endpoint is a category error |
| 0011 | Angular standalone + signals, no NgRx | the UI is a presentation surface; state libraries would add ceremony that teaches nothing about concurrency |
| 0012 | Postgres advisory lock over Redis/ZooKeeper/etcd | no new infrastructure vs Redlock's well-known correctness debate; when you'd actually reach for Redis |
| 0013 | In-memory ring buffer for contention events, not persisted | observability of the lab vs durability; why a bounded buffer (and what unbounded would cost under load) |
| 0014 | Compose replicas + nginx, Kubernetes deliberately excluded | K8s would add operational surface without adding a single concurrency insight — the lesson is statelessness, and compose proves it in 30 seconds |
| 0015 | Local Postgres + Respawn over Testcontainers | forced by no Docker daemon here; documents the fallback and when Testcontainers is genuinely better |
| 0016 | Three-tier test strategy (deterministic / invariant / integration) | why you can't assert on interleavings, and what you assert instead |

### Supporting architecture docs

- **`system-overview.md`** — mermaid C4-ish diagrams: context (you → UI → API →
  Postgres), container (1 instance vs 3 behind nginx, showing exactly where shared
  state lives), and a sequence diagram of a contended withdrawal under each strategy.
- **`trade-off-matrix.md`** — every decision on one scannable page: *Decision ·
  Chosen · Alternative · We gain · We give up · Breaks down when…* The "breaks down
  when" column is the one to memorise; senior interviews are mostly about knowing
  the limits of your own choices.

### Lesson docs (`docs/lessons/`)

Same narrative voice as before — plain-developer explanations, banking analogies —
but each now ends with a **Trade-offs** and a **What an interviewer asks next**
section, so the two doc sets reinforce each other rather than duplicate.

`00-mental-model` · `01-race-conditions` · `02-in-process-locking` ·
`03-optimistic` · `04-pessimistic` · `05-choosing-between-them` · `06-deadlocks` ·
`07-parallelism-cpu` · `08-async-io` · `09-scaling` · `10-benchmarking-vs-load` ·
`11-interview-questions`

---

## Milestones — all delivered

Docs are written *with* the code that motivates them, never bolted on at the end —
an ADR whose consequences you haven't felt yet is a guess.

| Milestone | Status | Landed in |
|---|---|---|
| M0 Foundation | ✅ | `0285b8b` |
| M1 The race is visible | ✅ | `6aa3509` |
| M2 Optimistic vs pessimistic | ✅ | `9a21f36` |
| M3 Parallelism & async | ✅ | `65919a1` |
| M5 Scale | ✅ | `89a56ed` — built **before** M4, see below |
| M4 Angular UI | ✅ | `80dcf7d` |
| *(unplanned)* Claude Code scaffolding | ✅ | `6b8d876` |

- **M0 — Foundation.** Install .NET 8 SDK, solution + projects, `IEndpoint`
  convention, in-memory store, EF Core + Npgsql, start local Postgres, first
  migration, xUnit wiring, CI workflow, `README.md` with the mental model.
  **Docs:** commit `docs/PLAN.md`, `architecture/README.md`, `system-overview.md`,
  ADRs 0001–0005, 0015.
- **M1 — The race is visible.** Slices 1–3, `IInterleaveGate`, `IContentionRecorder`,
  Lab run orchestrator, SignalR hub, `RunSummary`. Deterministic lost-update test +
  invariant tests. **Docs:** lessons 00–02, ADRs 0008, 0009, 0013, 0016.
- **M2 — Optimistic vs pessimistic.** Slices 4–6 on real Postgres, integration
  tests with `Respawn`, deadlock reproduction + ordered-acquisition fix.
  **Docs:** lessons 03–06, ADRs 0006, 0007.
- **M3 — Parallelism & async.** Slices 7–8, `Bank.Benchmarks` with results committed
  to `docs/benchmarks/`. **Docs:** lessons 07–08, 10, ADR 0010.
- **M4 — Angular UI.** All six pages, SignalR client, animations. `npm run build`
  green. **Docs:** ADR 0011.
- **M5 — Scale.** Slices 9–10, sync-vs-async endpoints, Dockerfiles + compose (1 and
  3 replicas) + nginx, load-harness scenarios, k6 scripts, Scale Lab charts.
  **Docs:** lessons 09, 11, ADRs 0012, 0014, and `trade-off-matrix.md` assembled
  from every ADR now that all consequences are known.

---

## What shipped vs what was planned

Kept deliberately, because the gap between intent and outcome is the interesting part of
any plan — and because two of these changes are themselves interview material.

**Three dependencies were dropped on licence grounds, after reading the licences.**
The plan named FluentAssertions and NBomber, and MediatR had already been ruled out.
FluentAssertions is commercial from v8; **NBomber ships a commercial subscription
agreement (licence v3.0, September 2025)** — its NuGet package sets
`requireLicenseAcceptance` and carries a licence *file* rather than an SPDX expression.
Replaced by **Shouldly** (MIT) and a ~200-line in-repo load harness. The stance is in
[ADR-0002](architecture/adr/0002-no-mediatr.md): a public teaching repo should not hand a
reader a dependency they cannot use at work.

**M5 was built before M4.** The Scale Lab page renders load-test results, so producing
the measurements first meant the UI could display real numbers instead of placeholders.

**A seventh, unplanned milestone: Claude Code scaffolding.** `CLAUDE.md` plus `.claude/`
— path-scoped rules, slash commands, an `add-slice` skill, a `concurrency-reviewer`
agent, and a hook that warns when an ADR is missing its mandatory sections. Prompted by a
question about project structure, and it earned its place: parts of this codebase are
broken on purpose and a fresh session would otherwise "fix" them.

**A flake was found and removed rather than widened.** Two unit tests asserted a
*performance ratio*; one failed under CPU load, reproduced deliberately with four busy
loops. A wall-clock ratio on contended hardware is a property of the machine, not the
code, so the assertions were deleted — the claim lives in `docs/benchmarks/` where it is
measured with warmup and error bars. Recorded in
[ADR-0016](architecture/adr/0016-three-tier-test-strategy.md).

**Two bugs surfaced only by driving the UI in a real browser**, not by the build passing:
CORS allowed `localhost:4200` but not `127.0.0.1:4200` (different origins to a browser),
and a SignalR client cannot join a run's group before the server has generated its id.

**Still not executed here:** the three-replica demo. There is no Docker daemon in this
environment, so `deploy/docker-compose.scale.yml` is `docker compose config`-validated
only. Everything else in this repo was run and measured.

---

## Files that matter most

- `src/Bank.Api/Shared/Interleaving/IInterleaveGate.cs` — the seam that makes race
  tests deterministic. Everything in M1–M2 depends on it.
- `src/Bank.Api/Shared/Contention/ContentionRecorder.cs` — every "who wins" answer
  comes from here.
- `src/Bank.Api/Features/Lab/StartRun/` — orchestrates N concurrent actors against a
  chosen strategy; the single entry point the UI drives.
- `src/Bank.Api/Shared/Locking/IAccountLock.cs` — one interface, two
  implementations (in-process vs Postgres advisory), which is precisely the
  1-instance vs N-instance lesson expressed as a DI registration.
- `deploy/docker-compose.scale.yml` + `deploy/nginx.conf` — makes slice 3 break on
  demand.
- `docs/lessons/09-scaling.md` and `docs/architecture/trade-off-matrix.md` — the two
  you'll reread before every interview.
- `docs/architecture/adr/0008-interleave-gate-in-production-code.md` — the most
  arguable decision in the repo, and therefore the best one to be able to defend.

---

## Verification

- `dotnet test` — full suite green, including the deterministic race tests and the
  chaos/invariant tests. Integration tests run against the local Postgres 16
  instance started natively (Docker daemon is unavailable in this session).
- `dotnet run --project src/Bank.Benchmarks -c Release` — BenchmarkDotNet tables
  produced and committed under `docs/benchmarks/`.
- `dotnet run --project tests/Bank.LoadTests` — the load harness against the locally running
  API; real RPS/p95/p99 numbers for the sync-vs-async and hot-row scenarios.
- `curl` walkthrough per slice: seed an account, `POST /api/lab/runs` with
  `strategy=naive`, then `GET /api/lab/runs/{id}` and confirm the summary shows
  money destroyed; repeat with `optimistic` and confirm it doesn't.
- `npm ci && npm run build` in `ui/bank-lab-ui`, plus `npm start` against the API to
  confirm the SignalR timeline renders.
- `docker compose -f deploy/docker-compose.scale.yml config` validates here;
  **you** run `up --scale api=3` on your machine to watch the in-memory lock slice
  fail while the optimistic slice holds.
- **Docs review pass:** every ADR has a non-empty *Why not the others* and
  *Trade-offs accepted* section (an ADR with no stated downside is a rationalisation,
  not a decision); every mermaid diagram renders on GitHub; every claim about
  measured behaviour in the lessons cites a number produced by an actual run in this
  repo, not an assertion from memory.

## Explicitly out of scope (noted so it isn't a surprise)

Saga, outbox implementation, retry policies (Polly), microservice split, auth, and
real money movement. `docs/lessons/09-scaling.md` will name where each would attach, so the
follow-up work you mentioned drops in without rework.
