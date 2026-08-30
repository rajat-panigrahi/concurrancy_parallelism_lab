using System.Text.Json;
using Bank.LoadTests.Harness;

// Macro measurement: throughput and latency percentiles over HTTP, under concurrency.
// The opposite number to src/Bank.Benchmarks, which measures methods in-process in
// microseconds. See docs/lessons/10-benchmarking-vs-load-testing.md.

var baseUrl = args.FirstOrDefault(a => a.StartsWith("--url=", StringComparison.Ordinal))?["--url=".Length..]
              ?? Environment.GetEnvironmentVariable("BANKLAB_URL")
              ?? "http://127.0.0.1:5080";

var seconds = int.TryParse(
    args.FirstOrDefault(a => a.StartsWith("--seconds=", StringComparison.Ordinal))?["--seconds=".Length..],
    out var parsed)
    ? parsed
    : 8;

var duration = TimeSpan.FromSeconds(Math.Clamp(seconds, 2, 120));

var handler = new SocketsHttpHandler
{
    // Without a generous connection limit the harness throttles itself and reports its
    // own queueing as the server's latency.
    MaxConnectionsPerServer = 512,
    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
};

using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
var harness = new LoadHarness(client);

var scenarios = new[]
{
    new ScenarioSpec
    {
        Name = "async (proper await)",
        Url = $"{baseUrl}/api/scale/async?delayMs=100",
        VirtualUsers = 100,
        Duration = duration,
        Note = "The thread is released while waiting, so 100 in-flight requests cost almost no threads.",
    },
    new ScenarioSpec
    {
        Name = "sync-over-async (.Result)",
        Url = $"{baseUrl}/api/scale/sync-over-async?delayMs=100",
        VirtualUsers = 100,
        Duration = duration,
        Note = "Identical work, identical hardware. Each request blocks a thread-pool thread.",
    },
};

Console.WriteLine($"Target : {baseUrl}");
Console.WriteLine($"Duration: {duration.TotalSeconds:0}s per scenario, after a 2s warm-up");
Console.WriteLine();

var results = new List<ScenarioResult>();

foreach (var scenario in scenarios)
{
    Console.WriteLine($"running: {scenario.Name} ({scenario.VirtualUsers} virtual users)...");
    results.Add(await harness.RunAsync(scenario));

    // Let the thread pool settle so the next scenario starts from a comparable state.
    await Task.Delay(TimeSpan.FromSeconds(3));
}

Console.WriteLine();
Console.WriteLine($"{"scenario",-28} {"VUs",4} {"RPS",9} {"p50",8} {"p95",8} {"p99",8} {"max",9} {"fail",6}");
Console.WriteLine(new string('-', 92));

foreach (var r in results)
{
    Console.WriteLine(
        $"{r.Name,-28} {r.VirtualUsers,4} {r.RequestsPerSecond,9:0.0} {r.P50Ms,7:0.0}ms {r.P95Ms,7:0.0}ms "
        + $"{r.P99Ms,7:0.0}ms {r.MaxMs,8:0.0}ms {r.Failed,6}");
}

if (results.Count == 2 && results[1].RequestsPerSecond > 0)
{
    var ratio = results[0].RequestsPerSecond / results[1].RequestsPerSecond;

    Console.WriteLine();
    Console.WriteLine(
        $"The async endpoint sustained {ratio:0.0}x the throughput of the sync-over-async one, "
        + $"and its p99 was {results[1].P99Ms / Math.Max(results[0].P99Ms, 0.1):0.0}x lower. "
        + "Same machine, same cores, same 100 ms wait. The only difference is whether the code "
        + "holds a thread while it waits.");
}

var outputDirectory = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "artifacts");
Directory.CreateDirectory(outputDirectory);

var outputPath = Path.Combine(outputDirectory, "loadtest-threadpool.json");
await File.WriteAllTextAsync(
    outputPath,
    JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));

Console.WriteLine();
Console.WriteLine($"results written to {Path.GetFullPath(outputPath)}");
