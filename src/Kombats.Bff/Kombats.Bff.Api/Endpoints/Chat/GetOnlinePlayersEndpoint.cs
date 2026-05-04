using Kombats.Bff.Api.Mapping;
using Kombats.Bff.Api.Models.Responses;
using Kombats.Bff.Application.Clients;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Kombats.Bff.Api.Endpoints.Chat;

public sealed class GetOnlinePlayersEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/chat/presence/online", async (
                IChatClient chat,
                int limit = 100,
                int offset = 0,
                CancellationToken ct = default) =>
            {
                var src = await chat.GetOnlinePlayersAsync(limit, offset, ct);
                if (src is null)
                {
                    return Results.Ok(new OnlinePlayersResponse(Array.Empty<OnlinePlayerResponse>(), 0));
                }
                return Results.Ok(ChatMapper.Map(src));
            })
            .RequireAuthorization()
            .WithTags("Chat")
            .WithSummary("Paginated list of currently online players.")
            .Produces<OnlinePlayersResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized);
    }
}
