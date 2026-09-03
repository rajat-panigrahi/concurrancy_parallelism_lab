---
name: add-slice
description: Add a new concurrency strategy slice to the lab — handler, DI registration, all three test tiers, lesson and ADR. Use when adding a withdrawal strategy or any new concurrency technique to demonstrate.
---

# Adding a concurrency slice

A slice is a feature folder **and** a lesson. It is not done when the code compiles; it is
done when someone can run it and see a verdict.

Read `slice-template.md` in this directory for the handler skeleton with the recording
calls and gate placement already in the right places.

## Checklist

**1. Decide what it demonstrates.** One sentence, and it must be something the existing
slices do not already show. If you cannot say what the run's *verdict* will look like —
what a reader will point at — the slice is not ready to write.

**2. Handler** — `src/Bank.Api/Features/Withdrawals/<Name>/<Name>Handler.cs`, implementing
`IWithdrawStrategy`. Copy the structure from the closest existing slice: `Optimistic` for
retry-based, `Pessimistic` for lock-based.

**3. Register it** in `Program.cs`:
`builder.Services.AddSingleton<IWithdrawStrategy, YourHandler>();`
An unregistered strategy compiles fine and silently never appears in the UI.

**4. Gate in the right place.** Lock-taking handlers gate at
`InterleaveCheckpoints.BeforeAcquire`; lock-free handlers at `AfterRead`. Gating inside a
critical section deadlocks. In a retry loop, gate only on `attempt == 1`.

**5. Record every phase.** `Started`, `Read`, `Gate`, `LockWait`/`LockAcquired`, `Write`,
`Committed`, plus `Conflict`/`Retry` where applicable. Pass `versionSeen` and
`versionWritten` on `Committed` — the "who won" replay depends on them.

**6. Tests, all three tiers:**
- deterministic, `forceRace: true`, asserting the specific consequence;
- invariant, free-running, asserting only money conservation and no overdraft;
- integration against real PostgreSQL if the slice touches the database.

Never assert an ordering you did not force. Timing assertions relative only.

**7. Run it and capture the real numbers:**
```bash
curl -X POST localhost:5080/api/lab/runs -H 'Content-Type: application/json' \
  -d '{"strategy":"<name>","actors":5,"amountEach":100,"startingBalance":100,"forceRace":true}'
```

**8. Lesson** — `docs/lessons/NN-<name>.md`, using the real numbers from step 7. Ends with
**Trade-offs** and **What an interviewer asks next**.

**9. ADR** if the slice embodies a decision worth defending — use `/adr`.

**10. Update** `docs/lessons/README.md`, `docs/architecture/trade-off-matrix.md`, and the
strategy table in `README.md`.

## Done when

`dotnet test` is green, the strategy appears in `GET /api/lab/strategies` with an honest
`scalesAcrossInstances`, a run produces the verdict you predicted in step 1, and the
lesson quotes numbers you actually measured.
