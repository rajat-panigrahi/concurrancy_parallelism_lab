using Bank.Api.Shared.Contention;
using Microsoft.AspNetCore.SignalR;

namespace Bank.Api.Features.Lab.Streaming;

/// <summary>
/// Drains the recorder's channel and pushes each event to the watching clients.
/// </summary>
/// <remarks>
/// <para>This is the consumer half of a producer/consumer split, and it exists for a
/// concurrency reason rather than a tidiness one. Pushing to SignalR inline from
/// <c>Record</c> would put a network call in the middle of the critical section we are
/// measuring — the observer would change the timings it reports, and a slow client
/// would slow down the bank.</para>
/// <para>Instead the handler does an in-memory enqueue that cannot block, and one
/// background reader does the slow work. <see cref="System.Threading.Channels"/> is the
/// modern .NET way to write this; the older answer is <c>BlockingCollection</c>, which
/// blocks threads instead of awaiting.</para>
/// </remarks>
public sealed class ContentionBroadcaster(
    ContentionRecorder recorder,
    IHubContext<ContentionHub> hub,
    ILogger<ContentionBroadcaster> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var contentionEvent in recorder.Live.ReadAllAsync(stoppingToken))
        {
            try
            {
                await hub.Clients
                    .Groups(ContentionHub.GroupFor(contentionEvent.RunId), ContentionHub.AllRunsGroup)
                    .SendAsync("contention", contentionEvent, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // A broken client must never take down the run it is watching. The
                // timeline is still recorded and still fetchable over REST.
                logger.LogWarning(ex, "Failed to broadcast contention event for run {RunId}.", contentionEvent.RunId);
            }
        }
    }
}
