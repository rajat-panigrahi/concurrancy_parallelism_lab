using System.Diagnostics;

namespace Bank.LoadTests.Harness;

public sealed record ScenarioSpec
{
    public required string Name { get; init; }
    public required string Url { get; init; }

    /// <summary>How many requests are kept in flight at once.</summary>
    public required int VirtualUsers { get; init; }

    public required TimeSpan Duration { get; init; }
    public TimeSpan WarmUp { get; init; } = TimeSpan.FromSeconds(2);
    public string? Note { get; init; }
}

public sealed record ScenarioResult
{
    public required string Name { get; init; }
    public required int VirtualUsers { get; init; }
    public required int Completed { get; init; }
    public required int Failed { get; init; }
    public required double DurationSeconds { get; init; }
    public required double RequestsPerSecond { get; init; }
    public required double P50Ms { get; init; }
    public required double P95Ms { get; init; }
    public required double P99Ms { get; init; }
    public required double MaxMs { get; init; }
    public string? Note { get; init; }
}

/// <summary>
/// A deliberately small load generator: N virtual users looping against one URL for a
/// fixed duration, reporting throughput and latency percentiles.
/// </summary>
/// <remarks>
/// <para>This exists because the tools that would normally do it are either commercially
/// licensed (NBomber) or cannot be installed in this environment (k6). See ADR-0010. The
/// k6 scripts under <c>loadtests/k6/</c> do the same job on your own machine.</para>
/// <para><b>The harness itself must not be the bottleneck.</b> It is fully async, so N
/// virtual users cost roughly no threads — which is the very property lesson 08 is
/// about. Generating load with blocking calls would measure the generator rather than
/// the API, and would be a nice irony in a repo about thread-pool starvation.</para>
/// </remarks>
public sealed class LoadHarness(HttpClient client)
{
    public async Task<ScenarioResult> RunAsync(ScenarioSpec spec, CancellationToken cancellationToken = default)
    {
        // Warm up first: the first requests pay for JIT, connection setup and EF's model
        // build. Including them would report a cold start as if it were steady state.
        await RunPhaseAsync(spec, spec.WarmUp, measure: false, cancellationToken);

        var latencies = new List<double>(capacity: 8192);
        var failures = 0;
        var clock = Stopwatch.StartNew();

        var (completed, failed, samples) = await RunPhaseAsync(spec, spec.Duration, measure: true, cancellationToken);

        clock.Stop();
        latencies.AddRange(samples);
        failures += failed;

        latencies.Sort();

        return new ScenarioResult
        {
            Name = spec.Name,
            VirtualUsers = spec.VirtualUsers,
            Completed = completed,
            Failed = failures,
            DurationSeconds = Math.Round(clock.Elapsed.TotalSeconds, 2),
            RequestsPerSecond = Math.Round(completed / clock.Elapsed.TotalSeconds, 1),
            P50Ms = Percentile(latencies, 0.50),
            P95Ms = Percentile(latencies, 0.95),
            P99Ms = Percentile(latencies, 0.99),
            MaxMs = latencies.Count == 0 ? 0 : Math.Round(latencies[^1], 1),
            Note = spec.Note,
        };
    }

    private async Task<(int Completed, int Failed, List<double> Latencies)> RunPhaseAsync(
        ScenarioSpec spec,
        TimeSpan duration,
        bool measure,
        CancellationToken cancellationToken)
    {
        if (duration <= TimeSpan.Zero)
        {
            return (0, 0, []);
        }

        using var phase = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        phase.CancelAfter(duration);

        var completed = 0;
        var failed = 0;
        var latencies = new List<double>();
        var latencyLock = new object();

        var users = Enumerable.Range(0, spec.VirtualUsers).Select(async _ =>
        {
            var local = new List<double>();

            while (!phase.IsCancellationRequested)
            {
                var started = Stopwatch.GetTimestamp();

                try
                {
                    using var response = await client.GetAsync(spec.Url, phase.Token);
                    var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

                    if (response.IsSuccessStatusCode)
                    {
                        Interlocked.Increment(ref completed);

                        if (measure)
                        {
                            local.Add(elapsed);
                        }
                    }
                    else
                    {
                        Interlocked.Increment(ref failed);
                    }
                }
                catch (OperationCanceledException) when (phase.IsCancellationRequested)
                {
                    break;
                }
                catch
                {
                    Interlocked.Increment(ref failed);
                }
            }

            // Merge once at the end rather than locking per request — the same
            // partition-then-aggregate rule as lesson 07, applied to the tool itself.
            if (local.Count > 0)
            {
                lock (latencyLock)
                {
                    latencies.AddRange(local);
                }
            }
        });

        await Task.WhenAll(users);

        return (completed, failed, latencies);
    }

    /// <summary>Nearest-rank percentile over a pre-sorted list.</summary>
    private static double Percentile(List<double> sorted, double percentile)
    {
        if (sorted.Count == 0)
        {
            return 0;
        }

        var rank = (int)Math.Ceiling(percentile * sorted.Count) - 1;

        return Math.Round(sorted[Math.Clamp(rank, 0, sorted.Count - 1)], 1);
    }
}
