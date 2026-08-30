using System.Diagnostics;
using Bank.Api.Features.Fraud.RunFraudChecks;
using Shouldly;

namespace Bank.Api.UnitTests.Features.Fraud;

public class FraudFanOutTests
{
    private static readonly TimeSpan Latency = TimeSpan.FromMilliseconds(100);

    [Fact]
    public async Task SequentialAwait_TakesTheSumOfTheLatencies()
    {
        var handler = NewHandler();

        var response = await handler.HandleAsync(
            new FraudRequest { Checks = 6, LatencyMs = 100, MaxConcurrency = 3 },
            CancellationToken.None);

        var sequential = response.Timings.Single(t => t.Name == "sequential await");

        // 6 x 100ms, one after another. Awaiting inside a loop serialises independent work.
        sequential.DurationMs.ShouldBeGreaterThan(550d);
    }

    [Fact]
    public async Task TaskWhenAll_TakesTheSlowestLatency_NotTheSum()
    {
        var handler = NewHandler();

        var response = await handler.HandleAsync(
            new FraudRequest { Checks = 6, LatencyMs = 100, MaxConcurrency = 3 },
            CancellationToken.None);

        var sequential = response.Timings.Single(t => t.Name == "sequential await");
        var whenAll = response.Timings.Single(t => t.Name == "Task.WhenAll");

        whenAll.DurationMs.ShouldBeLessThan(300d, "six overlapping 100 ms waits should finish in about 100 ms");
        whenAll.DurationMs.ShouldBeLessThan(sequential.DurationMs / 2);
    }

    [Fact]
    public async Task TheSpeedupComesFromOverlappingWaits_NotFromMoreThreads()
    {
        var handler = NewHandler();

        var response = await handler.HandleAsync(
            new FraudRequest { Checks = 12, LatencyMs = 100, MaxConcurrency = 4 },
            CancellationToken.None);

        var whenAll = response.Timings.Single(t => t.Name == "Task.WhenAll");

        // This is the assertion that separates concurrency from parallelism. Twelve
        // calls run at once on a handful of threads, because none of them is using a
        // CPU — they are all just waiting, and a thread is not needed to wait.
        whenAll.DistinctThreads.ShouldBeLessThan(12,
            "if async needed a thread per in-flight operation it would be no better than blocking");
    }

    [Fact]
    public async Task Throttling_TradesSpeedForProtectingTheDependency()
    {
        var handler = NewHandler();

        var response = await handler.HandleAsync(
            new FraudRequest { Checks = 8, LatencyMs = 100, MaxConcurrency = 2 },
            CancellationToken.None);

        var whenAll = response.Timings.Single(t => t.Name == "Task.WhenAll");
        var throttled = response.Timings.Single(t => t.Name == "throttled to 2");

        // 8 checks at concurrency 2 = 4 batches ~= 400ms. Slower than WhenAll on
        // purpose: unbounded fan-out is how you take down your own dependency.
        throttled.DurationMs.ShouldBeGreaterThan(whenAll.DurationMs);
        throttled.DurationMs.ShouldBeGreaterThan(350d);
        throttled.DurationMs.ShouldBeLessThan(700d);
    }

    [Fact]
    public async Task CancellationPropagates_AndStopsTheWorkPromptly()
    {
        var service = new FraudCheckService();
        using var cts = new CancellationTokenSource();

        var clock = Stopwatch.StartNew();
        var work = Task.WhenAll(Enumerable.Range(0, 10)
            .Select(i => service.CheckAsync(i, TimeSpan.FromSeconds(30), cts.Token)));

        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () => await work);
        clock.Stop();

        // The point of threading a CancellationToken all the way down: the caller gets
        // control back immediately instead of waiting out work nobody wants any more.
        clock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
    }

    private static RunFraudChecksHandler NewHandler() => new(new FraudCheckService());
}
