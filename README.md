# Concurrency & Parallelism Lab

A small banking app where every concurrency concept is a **runnable experiment with a
visible verdict** — built to prepare for .NET interviews, and to fix the mental model
rather than memorise definitions.

You start five withdrawals against an account holding ₹100 and watch, event by event,
who read what, who wrote, **who won, and who silently lost**.

> **Status:** in progress. See [`docs/PLAN.md`](docs/PLAN.md) for the roadmap and
> which milestone is landed.

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
| [`docs/PLAN.md`](docs/PLAN.md) | The roadmap |

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
```

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

## A note on dependencies

This repo deliberately avoids libraries whose licence would stop a reader using the
same approach at work. MediatR, FluentAssertions and NBomber are all commercially
licensed now, so the project uses plain handler classes, Shouldly, and a small in-repo
load harness instead. The reasoning is in
[ADR-0002](docs/architecture/adr/0002-no-mediatr.md).
