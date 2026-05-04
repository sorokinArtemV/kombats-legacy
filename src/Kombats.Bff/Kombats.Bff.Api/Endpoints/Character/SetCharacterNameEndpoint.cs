using Kombats.Bff.Api.Mapping;
using Kombats.Bff.Api.Models.Requests;
using Kombats.Bff.Api.Models.Responses;
using Kombats.Bff.Api.Validation;
using Kombats.Bff.Application.Clients;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Kombats.Bff.Api.Endpoints.Character;

public sealed class SetCharacterNameEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/character/name", async (
                SetCharacterNameRequest request,
                IPlayersClient playersClient,
                CancellationToken cancellationToken) =>
            {
                var character = await playersClient.SetCharacterNameAsync(
                    request.Name, cancellationToken);

                if (character is null)
                {
                    return Results.NotFound();
                }

                return Results.Ok(CharacterMapper.ToCharacterResponse(character));
            })
            .AddEndpointFilter<ValidationFilter<SetCharacterNameRequest>>()
            .RequireAuthorization()
            .WithTags("Character")
            .Produces<CharacterResponse>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(404);
    }
}
