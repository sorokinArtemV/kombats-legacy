using Microsoft.AspNetCore.Routing;

namespace Kombats.Matchmaking.Api.Endpoints;

public interface IEndpoint
{
    void MapEndpoint(IEndpointRouteBuilder app);
}
