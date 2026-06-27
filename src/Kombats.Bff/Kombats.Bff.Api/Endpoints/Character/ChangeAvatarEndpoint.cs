using Kombats.Bff.Api.Models.Requests;
using Kombats.Bff.Api.Models.Responses;
using Kombats.Bff.Api.Validation;
using Kombats.Bff.Application.Clients;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Kombats.Bff.Api.Endpoints.Character;

public sealed class ChangeAvatarEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/character/avatar", async (
                ChangeAvatarRequest request,
                IPlayersClient playersClient,
                CancellationToken cancellationToken) =>
            {
                var result = await playersClient.ChangeAvatarAsync(
                    request.ExpectedRevision,
                    request.AvatarId,
                    cancellationToken);

                if (result is null)
                {
                    return Results.NotFound();
                }

                // Coalesce null AvatarId to default for rollout safety
                // (older Players builds may omit the field).
                return Results.Ok(new ChangeAvatarResponse(result.AvatarId ?? "default", result.Revision));
            })
            .AddEndpointFilter<ValidationFilter<ChangeAvatarRequest>>()
            .RequireAuthorization()
            .WithTags("Character")
            .Produces<ChangeAvatarResponse>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(404)
            .ProducesProblem(409);
    }
}
