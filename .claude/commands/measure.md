---
description: Re-run the benchmarks and load test, then refresh the committed numbers
---

Regenerate this repo's measured figures and update every place that quotes them.

1. **Micro** — `dotnet run --project src/Bank.Benchmarks -c Release -- --filter '*'`.
   Copy the `*-report-github.md` files from `BenchmarkDotNet.Artifacts/results/` into
   `docs/benchmarks/`.
2. **Macro** — start the API, then
   `dotnet run --project tests/Bank.LoadTests -c Release -- --seconds=10`.
   Copy `artifacts/loadtest-threadpool.json` to `docs/benchmarks/`.
3. **Lab figures** — run each strategy through `/api/lab/runs` with identical input
   (5 actors, 100 each, starting balance 100, forced race) and note attempts, conflicts
   and total waiting per strategy.
4. **Update every quoted number.** Grep for the old figures before assuming where they
   live — they appear in `README.md`, several `docs/lessons/*.md`, the trade-off matrix,
   and `ui/bank-lab-ui/src/app/pages/scale/scale.component.ts` (which hardcodes the
   load-test results). A stale number in one file and a fresh one in another is worse
   than either alone.
5. Restate the conditions in `docs/benchmarks/README.md`: core count, whether `ShortRun`
   was used, and how wide the error bars are.

Numbers will differ from the committed ones; ratios should not differ much. If a *ratio*
has changed materially, say so rather than quietly overwriting — that is a finding.
