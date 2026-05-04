using Kombats.Abstractions;
using Kombats.Players.Api.Extensions;
using Kombats.Players.Application.UseCases.GetPlayerProfile;

namespace Kombats.Players.Api.Endpoints.PlayerProfile;

internal sealed class GetPlayerProfileEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/players/{identityId:guid}/profile", async (
                Guid identityId,
                IQueryHandler<GetPlayerProfileQuery, GetPlayerProfileQueryResponse> handler,
                CancellationToken ct) =>
            {
                var result = await handler.HandleAsync(new GetPlayerProfileQuery(identityId), ct);

                return result.Match(
                    value => Results.Ok(value),
                    failure => result.ToProblem());
            })
            .RequireAuthorization()
            .WithTags(Tags.PlayersCharacter)
            .WithSummary("Get player public profile")
            .WithDescription("Returns the public profile of any player by their identity ID. Any authenticated user can query any player's profile.")
            .Produces<GetPlayerProfileQueryResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
