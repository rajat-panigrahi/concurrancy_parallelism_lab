# Lessons

The teaching narrative: **how** each concept works, in plain developer language.
For **why the project is built the way it is**, see [`../architecture/`](../architecture/).

Each lesson pairs with runnable code and a test file, and ends with the trade-offs and
the follow-up question an interviewer is likely to ask.

| # | Lesson | The one thing to take away |
|---|---|---|
| [00](00-mental-model.md) | The mental model | Concurrency is one teller juggling; parallelism is many tellers |
| [01](01-race-conditions.md) | Race conditions | The bug is the gap between read and write, not simultaneity |
| [02](02-in-process-locking.md) | Locking in one process | Correct on one instance, silently broken on three |
| 03 | Optimistic concurrency | *M2* |
| 04 | Pessimistic concurrency | *M2* |
| 05 | Choosing between them | *M2* |
| 06 | Deadlocks | *M2* |
| 07 | Parallelism (CPU-bound) | *M3* |
| 08 | Async fan-out (I/O-bound) | *M3* |
| 09 | Scaling | *M5* |
| 10 | Benchmarking vs load testing | *M3* |
| 11 | Interview questions | *M5* |

## Suggested order

Read 00 first — everything else assumes its vocabulary. Then 01 and 02 as a pair: the
same scenario broken, then fixed, then the fix shown to be fragile at scale. That
fragility is what makes 03 and 04 necessary rather than academic.

## Running the experiments

```bash
dotnet run --project src/Bank.Api          # http://localhost:5080

curl -X POST localhost:5080/api/lab/runs -H 'Content-Type: application/json' \
  -d '{"strategy":"naive","actors":5,"amountEach":100,"startingBalance":100,"forceRace":true}'
```

Swap `"strategy"` for any id from `GET /api/lab/strategies`. Set `"forceRace": false` to
see how often the bug shows up by luck — which is the reason it survives testing and
reaches production.
