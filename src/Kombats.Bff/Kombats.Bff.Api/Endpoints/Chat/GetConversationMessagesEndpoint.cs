using Kombats.Bff.Api.Mapping;
using Kombats.Bff.Api.Models.Responses;
using Kombats.Bff.Application.Clients;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Kombats.Bff.Api.Endpoints.Chat;

public sealed class GetConversationMessagesEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/chat/conversations/{conversationId:guid}/messages", async (
                Guid conversationId,
                IChatClient chat,
                DateTimeOffset? before,
                int limit = 50,
                CancellationToken ct = default) =>
            {
                var src = await chat.GetMessagesAsync(conversationId, before, limit, ct);
                if (src is null)
                {
                    return Results.NotFound();
                }
                return Results.Ok(ChatMapper.Map(src));
            })
            .RequireAuthorization()
            .WithTags("Chat")
            .WithSummary("Paginated message history by conversation.")
            .Produces<MessageListResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
