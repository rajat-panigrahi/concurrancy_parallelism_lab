using Bank.Api.Shared.Contention;
using Bank.Api.Shared.Endpoints;

namespace Bank.Api.Features.Lab.GetRun;

public sealed class GetRunEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/lab/runs/{runId:guid}", (Guid runId, LabRunStore runs, ContentionRecorder recorder) =>
            {
                var summary = runs.Find(runId);

                return summary is null
                    ? Results.NotFound(new { error = $"Run {runId} not found or evicted." })
                    : Results.Ok(new { summary, timeline = recorder.EventsFor(runId) });
            })
            .WithName("GetLabRun")
            .WithTags("Lab")
            .WithSummary("The verdict and full timeline for one run.");

        app.MapGet("/api/lab/runs", (LabRunStore runs) => Results.Ok(runs.Recent()))
            .WithName("ListLabRuns")
            .WithTags("Lab")
            .WithSummary("Recent runs, newest first.");
    }
}
