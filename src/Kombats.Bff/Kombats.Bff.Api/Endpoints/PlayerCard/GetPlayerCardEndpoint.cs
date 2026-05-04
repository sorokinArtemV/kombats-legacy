using Kombats.Bff.Api.Mapping;
using Kombats.Bff.Api.Models.Responses;
using Kombats.Bff.Application.Clients;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Kombats.Bff.Api.Endpoints.PlayerCard;

/// <summary>
/// Player card composition endpoint. The BFF fetches the public profile from
/// Players (<c>GET /api/v1/players/{identityId}/profile</c>) and projects it
/// to <see cref="PlayerCardResponse"/>. Player card data is owned by Players,
/// not by Chat — this endpoint lives in the BFF as a general composition.
/// No caching at v1 (per architecture spec §10).
/// </summary>
public sealed class GetPlayerCardEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/players/{playerId:guid}/card", async (
                Guid playerId,
                IPlayersClient players,
                CancellationToken ct) =>
            {
                var profile = await players.GetProfileAsync(playerId, ct);
                if (profile is null)
                {
                    return Results.NotFound();
                }
                return Results.Ok(ChatMapper.MapCard(profile));
            })
            .RequireAuthorization()
            .WithTags("Players")
            .WithSummary("Player card (BFF composes from Players profile).")
            .Produces<PlayerCardResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
