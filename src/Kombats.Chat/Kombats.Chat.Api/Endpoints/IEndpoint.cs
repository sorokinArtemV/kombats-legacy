using Microsoft.AspNetCore.Routing;

namespace Kombats.Chat.Api.Endpoints;

public interface IEndpoint
{
    void MapEndpoint(IEndpointRouteBuilder app);
}
