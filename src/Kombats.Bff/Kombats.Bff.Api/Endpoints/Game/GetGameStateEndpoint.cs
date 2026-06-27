using Kombats.Bff.Api.Mapping;
using Kombats.Bff.Api.Models.Responses;
using Kombats.Bff.Application.Composition;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Kombats.Bff.Api.Endpoints.Game;

public sealed class GetGameStateEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/game/state", async (
                GameStateComposer composer,
                CancellationToken cancellationToken) =>
            {
                GameStateResult result = await composer.ComposeAsync(cancellationToken);

                if (result.IsBothUnavailable)
                {
                    return Results.StatusCode(503);
                }

                CharacterResponse? character = result.Character is not null
                    ? CharacterMapper.ToCharacterResponse(result.Character)
                    : null;

                QueueStatusResponse? queueStatus = result.QueueStatus is not null
                    ? new QueueStatusResponse(
                        result.QueueStatus.Status,
                        result.QueueStatus.MatchId,
                        result.QueueStatus.BattleId,
                        result.QueueStatus.MatchState)
                    : null;

                var response = new GameStateResponse(
                    Character: character,
                    QueueStatus: queueStatus,
                    IsCharacterCreated: result.IsCharacterCreated,
                    DegradedServices: result.DegradedServices);

                return Results.Ok(response);
            })
            .RequireAuthorization()
            .WithTags("Game")
            .Produces<GameStateResponse>()
            .ProducesProblem(401)
            .ProducesProblem(503);
    }
}
