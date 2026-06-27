using Kombats.Abstractions;
using Kombats.Abstractions.Auth;
using Kombats.Chat.Api.Extensions;
using Kombats.Chat.Application.UseCases.GetConversations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Kombats.Chat.Api.Endpoints.Conversations;

internal sealed class GetConversationsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("api/internal/conversations", async (
                HttpContext httpContext,
                IQueryHandler<GetConversationsQuery, GetConversationsResponse> handler,
                CancellationToken ct) =>
            {
                Guid? identityId = httpContext.User.GetIdentityId();
                if (identityId is null)
                    return Results.Unauthorized();

                var query = new GetConversationsQuery(identityId.Value);
                var result = await handler.HandleAsync(query, ct);

                return result.Match(
                    value => Results.Ok(value),
                    failure => result.ToProblem());
            })
            .RequireAuthorization()
            .WithTags(Tags.Chat)
            .WithSummary("Get conversations for authenticated user")
            .WithDescription("Returns all conversations (global + direct) for the authenticated user, ordered by last message time.")
            .Produces<GetConversationsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized);
    }
}
