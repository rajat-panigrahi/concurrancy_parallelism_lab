using Microsoft.AspNetCore.SignalR;

namespace Bank.Api.Features.Lab.Streaming;

/// <summary>
/// The UI's live feed. A client joins the group for a run id and receives every
/// contention event as it is recorded.
/// </summary>
/// <remarks>
/// Groups rather than a broadcast to everyone: two people can watch two different runs
/// without seeing each other's events. Note that SignalR groups are <i>per server</i> —
/// with multiple replicas you need a backplane (Redis) or clients connected to
/// different instances silently miss events. That is the same lesson as the in-process
/// lock, in a different costume. See ADR-0009.
/// </remarks>
public sealed class ContentionHub : Hub
{
    public static string GroupFor(Guid runId) => $"run-{runId}";

    /// <summary>
    /// Every event, regardless of run.
    /// </summary>
    /// <remarks>
    /// A client cannot join a run's group before the run exists, because the server
    /// generates the id. Rather than have the client invent one (and make the API trust
    /// it), a watcher can subscribe to everything and filter locally. Fine for a
    /// single-user lab; in a multi-tenant system this would be a data leak, which is why
    /// the per-run group exists alongside it.
    /// </remarks>
    public const string AllRunsGroup = "all-runs";

    public Task WatchRun(Guid runId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, GroupFor(runId));

    public Task StopWatchingRun(Guid runId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupFor(runId));

    public Task WatchAllRuns() =>
        Groups.AddToGroupAsync(Context.ConnectionId, AllRunsGroup);

    public Task StopWatchingAllRuns() =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, AllRunsGroup);
}
