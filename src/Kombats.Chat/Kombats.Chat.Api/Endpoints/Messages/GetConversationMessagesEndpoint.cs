using Kombats.Abstractions;
using Kombats.Abstractions.Auth;
using Kombats.Chat.Api.Extensions;
using Kombats.Chat.Application.UseCases.GetConversationMessages;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Kombats.Chat.Api.Endpoints.Messages;

internal sealed class GetConversationMessagesEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("api/internal/conversations/{id:guid}/messages", async (
                Guid id,
                HttpContext httpContext,
                IQueryHandler<GetConversationMessagesQuery, GetConversationMessagesResponse> handler,
                DateTimeOffset? before,
                int limit = 50,
                CancellationToken ct = default) =>
            {
                Guid? identityId = httpContext.User.GetIdentityId();
                if (identityId is null)
                    return Results.Unauthorized();

                var query = new GetConversationMessagesQuery(id, identityId.Value, before, limit);
                var result = await handler.HandleAsync(query, ct);

                return result.Match(
                    value => Results.Ok(value),
                    failure => result.ToProblem());
            })
            .RequireAuthorization()
            .WithTags(Tags.Chat)
            .WithSummary("Get messages for a conversation")
            .WithDescription("Returns paginated messages for the specified conversation. Cursor-based pagination using 'before' parameter.")
            .Produces<GetConversationMessagesResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
