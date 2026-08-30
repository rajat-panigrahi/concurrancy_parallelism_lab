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
| [03](03-optimistic-concurrency.md) | Optimistic concurrency | Nobody waits; the cost is wasted work |
| [04](04-pessimistic-concurrency.md) | Pessimistic concurrency | Nobody redoes work; the cost is waiting |
| [05](05-choosing-between-them.md) | Choosing between them | The deciding variable is the conflict rate |
| [06](06-deadlocks.md) | Deadlocks | Two correct operations, one cycle — fixed by ordering locks |
| [07](07-parallelism-cpu-bound.md) | Parallelism (CPU-bound) | How you aggregate matters more than whether you parallelise |
| [08](08-async-io-bound.md) | Async fan-out (I/O-bound) | 2005ms to 204ms on the same cores — waiting overlapped |
| 09 | Scaling | *M5* |
| [10](10-benchmarking-vs-load-testing.md) | Benchmarking vs load testing | Benchmarking finds slow code; load testing finds slow systems |
| 11 | Interview questions | *M5* |

## Suggested order

Read 00 first — everything else assumes its vocabulary. Then 01 and 02 as a pair: the
same scenario broken, then fixed, then the fix shown to be fragile at scale. That
fragility is what makes 03 and 04 necessary rather than academic.

Then 03 and 04 as their own pair — the same problem solved two ways, paying in two
different currencies — and 05 to turn that into a decision you can defend. 06 is the
bill that comes with pessimistic locking.

## Running the experiments

```bash
dotnet run --project src/Bank.Api          # http://localhost:5080

curl -X POST localhost:5080/api/lab/runs -H 'Content-Type: application/json' \
  -d '{"strategy":"naive","actors":5,"amountEach":100,"startingBalance":100,"forceRace":true}'
```

Swap `"strategy"` for any id from `GET /api/lab/strategies`. Set `"forceRace": false` to
see how often the bug shows up by luck — which is the reason it survives testing and
reaches production.
