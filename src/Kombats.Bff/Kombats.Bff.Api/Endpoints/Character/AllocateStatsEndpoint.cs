using Kombats.Bff.Api.Models.Requests;
using Kombats.Bff.Api.Models.Responses;
using Kombats.Bff.Api.Validation;
using Kombats.Bff.Application.Clients;
using Kombats.Bff.Application.Models.Internal;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Kombats.Bff.Api.Endpoints.Character;

public sealed class AllocateStatsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/character/stats", async (
                AllocateStatsRequest request,
                IPlayersClient playersClient,
                CancellationToken cancellationToken) =>
            {
                var result = await playersClient.AllocateStatsAsync(
                    request.ExpectedRevision,
                    request.Strength,
                    request.Agility,
                    request.Intuition,
                    request.Vitality,
                    cancellationToken);

                if (result is null)
                {
                    return Results.NotFound();
                }

                return Results.Ok(MapToResponse(result));
            })
            .AddEndpointFilter<ValidationFilter<AllocateStatsRequest>>()
            .RequireAuthorization()
            .WithTags("Character")
            .Produces<AllocateStatsResponse>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(404)
            .ProducesProblem(409);
    }

    private static AllocateStatsResponse MapToResponse(InternalCharacterResponse c) => new(
        Strength: c.Strength,
        Agility: c.Agility,
        Intuition: c.Intuition,
        Vitality: c.Vitality,
        UnspentPoints: c.UnspentPoints,
        Revision: c.Revision);
}
