using Kombats.Abstractions;
using Kombats.Abstractions.Auth;
using Kombats.Chat.Api.Extensions;
using Kombats.Chat.Application.UseCases.GetConversationMessages;
using Kombats.Chat.Application.UseCases.GetDirectMessages;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Kombats.Chat.Api.Endpoints.Messages;

internal sealed class GetDirectMessagesEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("api/internal/direct/{otherIdentityId:guid}/messages", async (
                Guid otherIdentityId,
                HttpContext httpContext,
                IQueryHandler<GetDirectMessagesQuery, GetConversationMessagesResponse> handler,
                DateTimeOffset? before,
                int limit = 50,
                CancellationToken ct = default) =>
            {
                Guid? identityId = httpContext.User.GetIdentityId();
                if (identityId is null)
                    return Results.Unauthorized();

                var query = new GetDirectMessagesQuery(identityId.Value, otherIdentityId, before, limit);
                var result = await handler.HandleAsync(query, ct);

                return result.Match(
                    value => Results.Ok(value),
                    failure => result.ToProblem());
            })
            .RequireAuthorization()
            .WithTags(Tags.Chat)
            .WithSummary("Get direct messages with another player")
            .WithDescription("Returns paginated direct message history with the specified player. Creates the conversation lazily if needed.")
            .Produces<GetConversationMessagesResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized);
    }
}
