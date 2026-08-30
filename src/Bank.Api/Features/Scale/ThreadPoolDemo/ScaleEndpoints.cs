using Bank.Api.Shared.Endpoints;

namespace Bank.Api.Features.Scale.ThreadPoolDemo;

public sealed record ThreadPoolSnapshot
{
    public required int ThreadCount { get; init; }
    public required long PendingWorkItems { get; init; }
    public required int AvailableWorkerThreads { get; init; }
    public required int MinWorkerThreads { get; init; }
    public required int MaxWorkerThreads { get; init; }
    public required string InstanceId { get; init; }
}

/// <summary>
/// Two endpoints that do exactly the same work, on exactly the same hardware, and
/// behave completely differently under load.
/// </summary>
/// <remarks>
/// <para>This is the application-layer answer to "can your app scale?". No Docker, no
/// Kubernetes, no extra machines — just how the code waits.</para>
/// <para>Both simulate a 100 ms database call. The async one releases its thread while
/// waiting. The sync-over-async one blocks a thread-pool thread on <c>.Result</c>, and
/// under concurrency the pool runs out of threads. It then grows at roughly one or two
/// threads per second, so latency does not degrade gracefully — it falls off a cliff
/// and stays there.</para>
/// </remarks>
public sealed class ScaleEndpoints : IEndpoint
{
    private static readonly string InstanceId =
        Environment.GetEnvironmentVariable("INSTANCE_ID") ?? Environment.MachineName;

    public void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/scale").WithTags("Scale");

        group.MapGet("/async", async (int? delayMs, CancellationToken cancellationToken) =>
            {
                await SimulateIoAsync(delayMs, cancellationToken);
                return Results.Ok(new { mode = "async", instanceId = InstanceId });
            })
            .WithName("ScaleAsync")
            .WithSummary("Awaits the I/O properly — the thread is released while waiting.");

        group.MapGet("/sync-over-async", (int? delayMs, CancellationToken cancellationToken) =>
            {
                // THE ANTI-PATTERN. .GetAwaiter().GetResult() blocks this thread-pool
                // thread for the whole wait. It is not "slightly less efficient" — under
                // concurrency it starves the pool, and requests that have nothing to do
                // with this endpoint start queueing behind it.
                //
                // On classic ASP.NET this would also deadlock outright, because the
                // continuation needs the SynchronizationContext this thread is holding.
                SimulateIoAsync(delayMs, cancellationToken).GetAwaiter().GetResult();

                return Results.Ok(new { mode = "sync-over-async", instanceId = InstanceId });
            })
            .WithName("ScaleSyncOverAsync")
            .WithSummary("Blocks a thread on .Result — same work, same hardware, and it collapses under load.");

        group.MapGet("/threadpool", () => Results.Ok(Snapshot()))
            .WithName("ThreadPoolSnapshot")
            .WithSummary("Live thread-pool state — watch ThreadCount climb while sync-over-async is under load.");
    }

    private static Task SimulateIoAsync(int? delayMs, CancellationToken cancellationToken) =>
        Task.Delay(Math.Clamp(delayMs ?? 100, 1, 2000), cancellationToken);

    private static ThreadPoolSnapshot Snapshot()
    {
        ThreadPool.GetAvailableThreads(out var availableWorkers, out _);
        ThreadPool.GetMinThreads(out var minWorkers, out _);
        ThreadPool.GetMaxThreads(out var maxWorkers, out _);

        return new ThreadPoolSnapshot
        {
            ThreadCount = ThreadPool.ThreadCount,
            PendingWorkItems = ThreadPool.PendingWorkItemCount,
            AvailableWorkerThreads = availableWorkers,
            MinWorkerThreads = minWorkers,
            MaxWorkerThreads = maxWorkers,
            InstanceId = InstanceId,
        };
    }
}
