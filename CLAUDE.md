# Concurrency & Parallelism Lab

A teaching repo for .NET interview prep. Every concurrency concept is a runnable
experiment with a measured verdict.

**The unusual thing about this codebase: some of it is broken on purpose, and must stay
that way.** Read "Do not fix" below before changing anything under `Features/Withdrawals`.

## Commands

```bash
# PostgreSQL must be running — the integration tests talk to a real database
pg_isready
sudo -u postgres psql -c "CREATE ROLE banklab LOGIN PASSWORD 'banklab' CREATEDB;"
sudo -u postgres createdb -O banklab banklab && sudo -u postgres createdb -O banklab banklab_test
dotnet ef database update --project src/Bank.Api

dotnet build                                   # warnings are errors; see below
dotnet test                                    # 22 unit + 26 integration
dotnet run --project src/Bank.Api              # http://localhost:5080, Swagger at /swagger
cd ui/bank-lab-ui && npm ci && npm start       # http://localhost:4200

dotnet run --project src/Bank.Benchmarks -c Release -- --filter '*'   # micro
dotnet run --project tests/Bank.LoadTests -c Release -- --seconds=10  # macro
```

Override the test database with `BANKLAB_TEST_DB`. Tests fail with a connection error,
not a helpful message, if PostgreSQL is not running.

## Do not "fix" these — they are the lesson

- **`NaiveWithdrawHandler`** performs an unguarded read-decide-write. It is supposed to
  lose updates. Tests assert that it does.
- **`InMemoryAccountStore`** is deliberately not thread-safe at the account level, and is
  registered as a **singleton**. Making it safe, or scoping it per-request, silently
  deletes the bug the whole repo is built to show.
- **`ScaleEndpoints`** has a `sync-over-async` endpoint calling `.GetAwaiter().GetResult()`
  on purpose, to demonstrate thread-pool starvation under load.
- **`InterestStrategies.ParallelWithLock` / `ParallelWithConcurrentQueue`** are slow on
  purpose. They are the "I parallelised it and it got slower" result.

If one of these looks like a bug you should fix, it is the bug you should explain.

## Conventions that differ from defaults

- **Warnings are errors** (`Directory.Build.props`). Most common trap: an **unread primary
  constructor parameter is CS9113 and fails the build**. Either use the parameter or drop
  it — do not leave it "for later".
- **No commercially-licensed dependencies.** MediatR, FluentAssertions and NBomber were
  all rejected on licence grounds ([ADR-0002](docs/architecture/adr/0002-no-mediatr.md)).
  Use **Shouldly** for assertions and plain handler classes instead of a mediator.
- **Vertical slice, not layered.** A feature owns its request, handler and endpoint in one
  folder ([ADR-0001](docs/architecture/adr/0001-vertical-slice-architecture.md)). Do not
  add a `Services/` or `Repositories/` layer.
- **EF Core packages stay aligned on 8.0.11.** Mixing 8.0.11 and 8.0.13 produces a CS1705
  assembly-version build break.
- **Target is `net8.0`,** pinned once in `Directory.Build.props`. The build environment
  cannot install .NET 9/10 (the Microsoft CDN is blocked by the proxy).
- **Money is `decimal`**, never `double`. Cents are `long` where atomics are needed.

## Concurrency rules with teeth

These are real failures from building this repo, not style preferences:

- **Gate before acquiring a lock, never inside one.** Handlers that take a lock call
  `gate.ReachAsync(..., InterleaveCheckpoints.BeforeAcquire, ...)`; lock-free handlers use
  `AfterRead`. Gating inside a critical section deadlocks: the lock holder waits at a
  barrier for actors who cannot enter until it releases.
- **Only the first attempt joins the barrier.** In retry loops, gate on `attempt == 1`.
  Retrying actors that re-join a barrier stall the run until its 5s timeout, because
  actors that already finished never arrive.
- **A fresh `DbContext` per retry attempt.** Reusing one keeps the stale entity in the
  change tracker, so the retry re-sends the same doomed UPDATE forever.
- **`xmin` is a PostgreSQL system column.** It is not included in `SELECT *`, so raw SQL
  must say `SELECT *, xmin FROM "Accounts" ...` or EF fails to materialise the entity.
- **`DbContext` is not thread-safe.** Concurrent actors each need their own, created from
  `IDbContextFactory<BankDbContext>`.

## Testing

Three tiers, and the rule that keeps them honest: **never assert an ordering you did not
force.** `Winners.Count.ShouldBe(1)` is legitimate; `winners[0].ActorId.ShouldBe("actor-3")`
is asserting the scheduler's mood.

**Timing assertions must be relative, never absolute.** `whenAll < sequential / 2` survives
a loaded machine; `whenAll < 400ms` does not. Absolute *lower* bounds derived from the work
itself are fine. Performance *ratios* belong in BenchmarkDotNet, not in the test suite —
two such assertions were removed in M5 after flaking under CPU load.

Classes whose assertions involve elapsed time join `TimingSensitiveCollection`, which
disables parallelisation for them.

## Documentation

Two sets, and they must not duplicate each other:

- `docs/lessons/` — **how** it works, plain narrative. Every measured claim must cite a
  number produced by an actual run in this repo.
- `docs/architecture/` — **why** it is built this way. Every ADR requires
  **Why not the others**, **Trade-offs accepted** and **Interview angle**. An ADR with no
  stated downside is a rationalisation, not a decision. A hook warns if these are missing.

## Environment limits

The 3-replica demo (`deploy/docker-compose.scale.yml`) has never been executed here —
there is no Docker daemon in this environment, so the compose files are only
`docker compose config`-validated. Do not claim it was run.
