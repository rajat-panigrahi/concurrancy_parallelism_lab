---
description: Run a concurrency strategy end to end and report who won
argument-hint: "[strategy] [actors] [amountEach] [startingBalance]"
---

Run a lab scenario and explain the verdict.

Strategy: `$0` (default `naive`). Actors: `$1` (default 5). Amount each: `$2` (default 100).
Starting balance: `$3` (default 100).

Steps:

1. Make sure PostgreSQL is up (`pg_isready`) and the API is running on
   `http://localhost:5080` (`/api/health`). Start it in the background if not:
   `dotnet run --project src/Bank.Api --no-launch-profile -c Release`. Write the PID to
   the scratchpad so it can be stopped later — do not `pkill -f Bank.Api`, the pattern
   matches your own shell and kills it.
2. `GET /api/lab/strategies` and confirm the requested strategy exists. If it does not,
   list what is available and stop.
3. `POST /api/lab/runs` with the parameters above and `"forceRace": true`.
4. Report, as a small table: final vs expected balance, discrepancy, winners, **silent
   losers**, refusals, attempts, conflicts, total waiting.
5. Say in one or two sentences what the numbers mean — specifically what this strategy
   *paid* for correctness: wasted work (conflicts, no waiting), waiting (no conflicts), or
   nothing because it is wrong.
6. If the caller asked for a comparison, run the other strategies with identical input so
   the only variable is the handler.

Do not modify any source files. This command only observes.
