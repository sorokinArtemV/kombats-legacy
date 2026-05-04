namespace Kombats.Players.Api.Endpoints;

/// <summary>
/// Defines a contract for mapping API endpoints.
/// </summary>
public interface IEndpoint
{
    /// <summary>
    /// Maps the endpoint to the specified route builder.
    /// </summary>
    /// <param name="app">The endpoint route builder to map the endpoint to.</param>
    void MapEndpoint(IEndpointRouteBuilder app);
}


