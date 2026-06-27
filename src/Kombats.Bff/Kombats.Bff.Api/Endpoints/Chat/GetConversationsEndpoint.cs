using Kombats.Bff.Api.Mapping;
using Kombats.Bff.Api.Models.Responses;
using Kombats.Bff.Application.Clients;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Kombats.Bff.Api.Endpoints.Chat;

public sealed class GetConversationsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/chat/conversations", async (
                IChatClient chat,
                CancellationToken ct) =>
            {
                var src = await chat.GetConversationsAsync(ct);
                if (src is null)
                {
                    return Results.Ok(new ConversationListResponse(Array.Empty<ChatConversationResponse>()));
                }
                return Results.Ok(ChatMapper.Map(src));
            })
            .RequireAuthorization()
            .WithTags("Chat")
            .WithSummary("List authenticated user's chat conversations.")
            .Produces<ConversationListResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized);
    }
}
