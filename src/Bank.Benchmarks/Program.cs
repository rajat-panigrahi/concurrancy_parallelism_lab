using Bank.Benchmarks.Benchmarks;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;

// BenchmarkDotNet measures code IN PROCESS, in nanoseconds, with statistical rigour.
// It is the wrong tool for measuring an HTTP endpoint — that is a load test, and it
// lives in tests/Bank.LoadTests. See docs/lessons/10-benchmarking-vs-load-testing.md.
//
// ShortRun keeps a full pass to a few minutes. Drop it for publishable numbers.
var config = DefaultConfig.Instance
    .AddJob(Job.ShortRun.WithId("short"))
    .WithOptions(ConfigOptions.DisableOptimizationsValidator);

BenchmarkSwitcher
    .FromTypes([
        typeof(AggregationBenchmarks),
        typeof(SynchronizationBenchmarks),
        typeof(AsyncOverheadBenchmarks),
    ])
    .Run(args, config);
