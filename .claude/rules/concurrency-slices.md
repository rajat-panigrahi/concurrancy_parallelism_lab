---
paths:
  - "src/Bank.Api/Features/**"
  - "src/Bank.Api/Shared/**"
---

# Working on a concurrency slice

## Before changing a handler

Check whether it is deliberately broken. `NaiveWithdrawHandler` loses updates on purpose
and `InMemoryAccountStore` is unguarded on purpose — tests assert both. Fixing them
destroys the repo's teaching value.

## Adding a withdrawal strategy

1. Implement `IWithdrawStrategy` in `Features/Withdrawals/<Name>/`. Supply `Name`,
   `Storage` and a one-line `Summary`; the UI renders all three.
2. Register it in `Program.cs`: `AddSingleton<IWithdrawStrategy, YourHandler>()`. An
   unregistered strategy silently never appears in `/api/lab/strategies`.
3. Record every phase through `IContentionRecorder` — `Started`, `Read`, `Gate`,
   `LockWait`/`LockAcquired`, `Write`, `Committed`, and `Conflict`/`Retry` where they
   apply. The run summary and the UI timeline are derived entirely from these events; a
   missing `Committed` event makes the actor look like it never wrote.
4. Always pass `versionSeen` and `versionWritten` on `Committed`. `RunSummaryCalculator`
   replays writes by version to decide who won — without them it has to synthesise a
   version and the "who won" answer degrades.

## Gate placement

| Handler takes a lock? | Checkpoint |
|---|---|
| Yes (`lock`, `SELECT … FOR UPDATE`, advisory lock) | `InterleaveCheckpoints.BeforeAcquire` |
| No (naive, optimistic) | `InterleaveCheckpoints.AfterRead` |

Gating **inside** a critical section deadlocks by construction: the actor holding the
lock waits at the barrier for actors who cannot enter until it releases. The gate has a
5-second cap so this degrades rather than hangs, but the cap is a safety net, not a
licence to gate in the wrong place.

In a retry loop, gate only when `attempt == 1`.

## Recording must not perturb what it measures

`IContentionRecorder.Record` is called from inside the code under observation. It must
never block, never do I/O and never throw. Use `Interlocked` for counters rather than a
lock — a counter needs atomicity, not mutual exclusion. Slow work (SignalR fan-out)
belongs on the background `ContentionBroadcaster` draining the channel.

## Persistence

- In-memory slices use `InMemoryAccountStore` (singleton, deliberately unguarded).
- Database slices take `IDbContextFactory<BankDbContext>` and create their own context.
  `DbContext` is not thread-safe, and concurrent actors sharing one throws.
- Raw SQL must select the concurrency token explicitly: `SELECT *, xmin FROM "Accounts"`.
  `xmin` is a system column and is not in `SELECT *`.

## Locking

`IAccountLock` has two implementations and swapping the DI registration is the entire
scale-out lesson: `InProcessAccountLock` is correct on one instance only;
`PostgresAdvisoryAccountLock` survives any number. Keep both working, and keep
`WorksAcrossInstances` honest — the UI labels strategies with it.

Use `SemaphoreSlim`, never `lock`, anywhere an `await` is involved. Release in a
`finally`, and guard against double release.
