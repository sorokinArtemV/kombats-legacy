using Microsoft.AspNetCore.Routing;

namespace Kombats.Bff.Api.Endpoints;

public interface IEndpoint
{
    void MapEndpoint(IEndpointRouteBuilder app);
}
