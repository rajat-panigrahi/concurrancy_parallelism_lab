# Benchmark results

Committed output from `src/Bank.Benchmarks` (BenchmarkDotNet). Regenerate with:

```bash
dotnet run --project src/Bank.Benchmarks -c Release -- --filter '*'
```

## Read these numbers carefully

They were produced on a **4-core shared cloud VM under a `ShortRun` job**, so the error
bars are wide — some `Error` values are the same order as the `Mean`. That is fine for
the comparisons being made here, which are order-of-magnitude (11× slower, 1,857×
slower), and **not** fine for anything subtler. Two results within 2× of each other on
this hardware should be treated as "about the same".

For publishable numbers: drop `Job.ShortRun` from `Program.cs`, run on a quiet machine
with a fixed CPU frequency, and don't trust a difference you can't reproduce.

This caveat is itself part of the lesson. A benchmark you can't state the conditions
for is a number, not evidence.

| Report | What it answers |
|---|---|
| `*AggregationBenchmarks-report-github.md` | Does parallelising help, and does aggregation strategy matter more? |
| `*SynchronizationBenchmarks-report-github.md` | What does each synchronisation primitive cost, uncontended? |
| `*AsyncOverheadBenchmarks-report-github.md` | What does `async` cost when there's nothing to await? |

Discussion in [`../lessons/07-parallelism-cpu-bound.md`](../lessons/07-parallelism-cpu-bound.md),
[`08-async-io-bound.md`](../lessons/08-async-io-bound.md) and
[`10-benchmarking-vs-load-testing.md`](../lessons/10-benchmarking-vs-load-testing.md).
