# 01 — Race conditions: who wins, and who loses silently

**Code:** `src/Bank.Api/Features/Withdrawals/NaiveWithdraw/`
**Tests:** `tests/Bank.Api.UnitTests/Features/Withdrawals/NaiveWithdrawTests.cs`

## Run it

```bash
curl -X POST localhost:5080/api/lab/runs -H 'Content-Type: application/json' \
  -d '{"strategy":"naive","actors":5,"amountEach":100,"startingBalance":100,"forceRace":true}'
```

Five actors, ₹100 each, one account holding ₹100. Exactly one should succeed.

```
final         : 0
expected      : -400
discrepancy   : -400
conserved     : False
winners       : ['actor-3']
SILENT LOSERS : ['actor-1', 'actor-2', 'actor-4', 'actor-5']
verdict       : 4 actor(s) were told 'approved' but their money never left the
                books — 400.00 unaccounted for.
```

All five were approved. The balance says ₹0. ₹500 actually left the bank.

## The code that did it

```csharp
var snapshot = store.Read(command.AccountId);          // READ

if (snapshot.Balance < command.Amount)                 // THINK
    return WithdrawResult.Rejected(...);

var newBalance = snapshot.Balance - command.Amount;    // still THINK
store.Write(command.AccountId, newBalance);            // WRITE
```

Every line is defensible. There is no missing `null` check, no off-by-one, no bad
algorithm. Reviewers approve this code all the time.

## Reading the timeline

The lab records every step. This is a 3-actor run at ₹40 each from ₹100:

```
seq actor    phase           ms  sawBal  sawVer wroteVer  note
  1 actor-1  Started       0.08
  2 actor-1  Read          0.12     100       1
  4 actor-2  Started       0.19
  5 actor-2  Read          0.20     100       1
  7 actor-3  Started       0.21
  8 actor-3  Read          0.21     100       1
 10 actor-3  Write         0.30     100       1            Blind write of 60.00, computed from a read of 100.00.
 11 actor-3  Committed     0.31      60       1        2
 12 actor-1  Write         0.37     100       1            Blind write of 60.00, computed from a read of 100.00.
 13 actor-2  Write         0.37     100       1            Blind write of 60.00, computed from a read of 100.00.
 14 actor-2  Committed     0.37      60       1        3
 15 actor-1  Committed     0.38      60       1        4
```

**Look at the `sawVer` column. Every actor read version 1.** That's the whole bug in
one column. Three actors each computed `100 - 40 = 60` from the same stale snapshot,
and each wrote 60. Three withdrawals of ₹40 happened; the books recorded one.

Version 4 is the final state, and it descends from version 1 — so versions 2 and 3,
and the withdrawals they represented, were erased with no error anywhere.

### How "who won" is decided

Not by comparing balances (two actors can write the same value). The lab replays the
writes using the version each actor *read*:

- A write carries forward the deductions already reflected in the version it read,
  plus its own.
- Read a stale version and your write carries forward a stale set — silently erasing
  everything that happened in between.
- Anyone missing from the final set who was told "approved" is a **silent loser**.

The code is `RunSummaryCalculator`, and it works unchanged for the in-memory counter
and for Postgres `xmin`, because it only needs versions to be unique, never ordered.

## Why this one is genuinely dangerous

Compare it to a normal bug:

|  | Ordinary bug | Lost update |
|---|---|---|
| Symptom | Exception, wrong output | **Success response** |
| Logs | Stack trace | Nothing |
| Reproduces | Every time | Under concurrency, sometimes |
| Found by | Tests | An auditor, months later |

Nothing throws. Nothing logs. Every customer gets `200 OK`. The failure is invisible
until someone reconciles the books, and by then the timeline is gone.

## Why your tests don't catch it

Because tests are usually the only user. Watch:

```csharp
[Fact]
public async Task WithoutContention_TheNaiveVersionIsPerfectlyCorrect()
{
    var summary = await new LabHarness().RunAsync(
        strategyName: "naive", actors: 1, ..., forceRace: false);

    summary.MoneyIsConserved.ShouldBeTrue();   // passes
}
```

That test passes. So does the integration test. So does QA. The bug needs a second
actor to arrive during the gap, and in testing there is rarely a second actor.

Try it yourself with `"forceRace": false` and 5 actors — sometimes you'll see the
corruption, sometimes not. That's what makes it a *race*: the outcome depends on
timing you don't control.

## Making it reproduce every time

A test that catches a bug one run in twenty is not a test — it's a flaky test, and
flaky tests get muted.

This repo makes the interleaving a *parameter* instead of an accident:

```csharp
await gate.ReachAsync(command.RunId, InterleaveCheckpoints.AfterRead, cancellationToken);
```

In production this is a no-op — one dictionary lookup on an unregistered run id. In a
forced-race run it's a barrier that holds every actor until all of them have read.
The lost update then happens **100% of the time**, and there's a test asserting exactly
that across 25 consecutive runs.

This is worth being able to describe in an interview. "How would you test a race
condition?" is a question most candidates answer with "run it a lot of times and see" —
which is guessing. Controlling the interleaving is the real answer. The design
trade-off (a test hook in production code) is argued in
[ADR-0008](../architecture/adr/0008-interleave-gate-in-production-code.md).

## The fixes, in order of what they cost you

1. **Make the operation atomic.** `UPDATE accounts SET balance = balance - 40 WHERE id = ? AND balance >= 40`.
   The database does read-decide-write in one statement, so there's no gap. Cheapest
   and best — when your logic fits in one statement. Often it doesn't.
2. **Lock** so nobody else can write during your gap → lesson 02 (in-process) and
   lesson 04 (in the database).
3. **Version** so you find out someone did, and retry → lesson 03.

What is *not* on the list: more cores, `async`, `ConcurrentDictionary`, or a faster
machine. Every one of those is a common wrong answer, and each makes the race *more*
likely by increasing how much runs at once.

## Trade-offs

The naive code isn't stupid — it's the simplest thing that works, and it is genuinely
correct for a single writer. It's also the fastest: no locks, no retries, no
round-trips. If an account can only ever be written by one process in one place (a
single-consumer queue, say), this code is right and adding coordination would be
waste. The failure mode is what makes it unacceptable for money: it fails *silently*,
and silent corruption is worse than a crash.

## What an interviewer asks next

*"How would you detect this in production?"* — Not from logs; there's nothing to log.
You detect it with **invariants**: a reconciliation job asserting that the sum of
transactions equals the balance delta. If you can't state your invariant, you can't
detect its violation.

*"Would making the method `async` fix it?"* — No, and it usually makes it worse, since
more requests are now in flight during the gap.

*"Is this a threading problem?"* — Not really. It's a *shared mutable state* problem.
It shows up identically across two processes, two servers, or two database
transactions, where there's no shared thread at all.
