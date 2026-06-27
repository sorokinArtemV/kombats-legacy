using Kombats.Bff.Api.Mapping;
using Kombats.Bff.Api.Models.Responses;
using Kombats.Bff.Application.Clients;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Kombats.Bff.Api.Endpoints.Chat;

public sealed class GetDirectMessagesEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/chat/direct/{otherPlayerId:guid}/messages", async (
                Guid otherPlayerId,
                IChatClient chat,
                DateTimeOffset? before,
                int limit = 50,
                CancellationToken ct = default) =>
            {
                var src = await chat.GetDirectMessagesAsync(otherPlayerId, before, limit, ct);
                if (src is null)
                {
                    return Results.NotFound();
                }
                return Results.Ok(ChatMapper.Map(src));
            })
            .RequireAuthorization()
            .WithTags("Chat")
            .WithSummary("Paginated direct-message history with another player.")
            .Produces<MessageListResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized);
    }
}
