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

    public Task WatchRun(Guid runId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, GroupFor(runId));

    public Task StopWatchingRun(Guid runId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupFor(runId));
}
