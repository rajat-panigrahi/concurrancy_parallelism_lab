namespace Bank.Api.Shared.Endpoints;

/// <summary>
/// A vertical slice owns its own HTTP surface. Each slice supplies one
/// <see cref="IEndpoint"/> that maps its routes, so adding a feature never means
/// editing a shared controller or a central route table.
/// </summary>
public interface IEndpoint
{
    void Map(IEndpointRouteBuilder app);
}
