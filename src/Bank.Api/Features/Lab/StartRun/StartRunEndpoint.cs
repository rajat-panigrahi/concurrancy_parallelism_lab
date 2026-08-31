using Bank.Api.Shared.Endpoints;

namespace Bank.Api.Features.Lab.StartRun;

public sealed class StartRunEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/lab/runs", async (
                StartRunRequest request,
                StartRunHandler handler,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    return Results.Ok(await handler.HandleAsync(request, cancellationToken));
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
            })
            .WithName("StartLabRun")
            .WithTags("Lab")
            .WithSummary("Runs N actors against one account and reports who won.");

        app.MapGet("/api/lab/strategies", (StartRunHandler handler) => Results.Ok(handler.Strategies))
            .WithName("ListStrategies")
            .WithTags("Lab")
            .WithSummary("The strategies available to run, and whether each survives scale-out.");
    }
}
