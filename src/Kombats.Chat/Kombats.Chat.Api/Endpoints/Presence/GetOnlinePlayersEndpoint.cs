using Kombats.Abstractions;
using Kombats.Chat.Api.Extensions;
using Kombats.Chat.Application.UseCases.GetOnlinePlayers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Kombats.Chat.Api.Endpoints.Presence;

internal sealed class GetOnlinePlayersEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("api/internal/presence/online", async (
                IQueryHandler<GetOnlinePlayersQuery, GetOnlinePlayersResponse> handler,
                int limit = 100,
                int offset = 0,
                CancellationToken ct = default) =>
            {
                var query = new GetOnlinePlayersQuery(limit, offset);
                var result = await handler.HandleAsync(query, ct);

                return result.Match(
                    value => Results.Ok(value),
                    failure => result.ToProblem());
            })
            .RequireAuthorization()
            .WithTags(Tags.Chat)
            .WithSummary("Get online players")
            .WithDescription("Returns a paginated list of online players with their display names.")
            .Produces<GetOnlinePlayersResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized);
    }
}
