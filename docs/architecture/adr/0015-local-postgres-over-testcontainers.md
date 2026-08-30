# ADR-0015 — Local PostgreSQL + Respawn, not Testcontainers

- **Status:** Accepted
- **Date:** 2026-08-30

## Context

The database slices need integration tests against real PostgreSQL (ADR-0004,
ADR-0005). The question is how a test run gets a database.

Testcontainers is the modern default: the test suite starts a throwaway PostgreSQL
container, runs against it, and disposes it. Every run begins from a guaranteed-clean,
correctly-versioned database, and nothing is required of the developer's machine
beyond Docker.

The constraint here: **the Docker daemon is not available in this build environment.**
The `docker` CLI and `dockerd` binary are installed, but there is no
`/var/run/docker.sock` — nothing to talk to. PostgreSQL 16, meanwhile, is installed
natively and starts with one command.

## Options considered

- **Testcontainers.** Hermetic, version-pinned, no shared state between runs, no setup
  instructions beyond "have Docker".
- **A local PostgreSQL instance** plus [Respawn](https://github.com/jbogard/Respawn) to
  reset data between tests.
- **EF Core in-memory provider.** No database at all.

## Decision

A local PostgreSQL instance, with a dedicated `banklab_test` database, migrations
applied by the test fixture, and Respawn resetting data between tests.

## Why not the others

- **Testcontainers cannot run here at all** — there is no Docker daemon to connect to,
  so the test suite would fail at fixture start. This is an environment constraint, not
  a judgement about the library. Where Docker *is* available, Testcontainers is the
  better default and this ADR should be revisited.
- **The EF Core in-memory provider** is not a database and is disqualified on the same
  grounds as in ADR-0004: it implements neither concurrency tokens nor row locks. Tests
  would pass green against a system where optimistic concurrency does not work. A test
  that cannot fail when the code is wrong is worse than no test, because it produces
  false confidence.

## Trade-offs accepted

- **The test suite has an external prerequisite.** `dotnet test` fails on a machine
  with no PostgreSQL running, with a connection error rather than a helpful message.
  Mitigated by documenting it in the README and by `BANKLAB_TEST_DB` allowing an
  override.
- **State leaks between runs are possible.** A container is destroyed; a database is
  not. Respawn reduces this to data (schema drift still needs a migration run).
- **Version drift.** The developer's local PostgreSQL may not be 16. Concurrency
  semantics used here are stable across supported versions, so the risk is low but
  non-zero.
- **Tests cannot run fully in parallel** against one shared database without careful
  isolation. Handled by giving each test its own account rows rather than resetting the
  whole database mid-suite.

## Consequences

- `PostgresFixture` owns the connection string (overridable via `BANKLAB_TEST_DB`) and
  applies migrations once per run in `InitializeAsync`.
- Tests share the fixture through an xUnit collection, so migrations run once rather
  than per test class.
- Integration tests create uniquely-numbered accounts per test rather than assuming an
  empty table — which makes them robust to leftover data and to being run in parallel.
- CI installs and starts PostgreSQL as a service step instead of relying on
  Testcontainers.

## Interview angle

**The 60-second version:** "I'd normally reach for Testcontainers — a disposable
database per run is the cleanest isolation you can get. It wasn't available in this
environment, so I used a local Postgres with Respawn for data reset. The important part
is what I didn't do: use the EF in-memory provider. It doesn't implement concurrency
tokens or row locks, so the tests would go green while optimistic concurrency was
completely broken."

**What a good interviewer asks next:** *"How do you keep integration tests isolated
when they share a database?"* Three levers worth naming: unique data per test (what
this repo does), a transaction rolled back per test (clean, but breaks the moment the
code under test manages its own transactions — which is exactly the case for the
pessimistic slice), and full reset between tests via Respawn (safest, slowest). The
second one is the trap: it's the standard trick, and it silently doesn't work for the
code you most want to test here.
