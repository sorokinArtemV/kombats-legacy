using Kombats.Bff.Api.Mapping;
using Kombats.Bff.Api.Models.Responses;
using Kombats.Bff.Application.Clients;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Kombats.Bff.Api.Endpoints.Game;

public sealed class OnboardEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/game/onboard", async (
                IPlayersClient playersClient,
                CancellationToken cancellationToken) =>
            {
                var character = await playersClient.EnsureCharacterAsync(cancellationToken);

                if (character is null)
                {
                    return Results.StatusCode(502);
                }

                return Results.Ok(CharacterMapper.ToOnboardResponse(character));
            })
            .RequireAuthorization()
            .WithTags("Game")
            .Produces<OnboardResponse>()
            .ProducesProblem(401)
            .ProducesProblem(502);
    }
}
