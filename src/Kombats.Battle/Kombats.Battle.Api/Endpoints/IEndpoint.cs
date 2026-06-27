using Microsoft.AspNetCore.Routing;

namespace Kombats.Battle.Api.Endpoints;

public interface IEndpoint
{
    void MapEndpoint(IEndpointRouteBuilder app);
}
