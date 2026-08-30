using System.Reflection;

namespace Bank.Api.Shared.Endpoints;

public static class EndpointExtensions
{
    /// <summary>
    /// Finds every <see cref="IEndpoint"/> in the assembly and maps it. This is the
    /// only reflection in the project: it buys us slices that are genuinely
    /// self-contained — a feature folder can be deleted and nothing else changes.
    /// </summary>
    public static IEndpointRouteBuilder MapSliceEndpoints(this IEndpointRouteBuilder app)
    {
        var endpointTypes = Assembly.GetExecutingAssembly()
            .GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false } && typeof(IEndpoint).IsAssignableFrom(t))
            .OrderBy(t => t.FullName, StringComparer.Ordinal);

        foreach (var type in endpointTypes)
        {
            var endpoint = (IEndpoint)Activator.CreateInstance(type)!;
            endpoint.Map(app);
        }

        return app;
    }
}
