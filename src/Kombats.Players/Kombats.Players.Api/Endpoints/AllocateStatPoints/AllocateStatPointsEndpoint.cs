using Kombats.Abstractions;
using Kombats.Players.Api.Endpoints;
using Kombats.Players.Api.Extensions;
using Kombats.Players.Api.Filters;
using Kombats.Players.Api.Identity;
using Kombats.Players.Application.UseCases.AllocateStatPoints;
using Microsoft.AspNetCore.Http;

namespace Kombats.Players.Api.Endpoints.AllocateStatPoints;

internal sealed class AllocateStatPointsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/players/me/stats/allocate", async (
                AllocateStatPointsRequest request,
                ICurrentIdentityProvider identityProvider,
                ICommandHandler<AllocateStatPointsCommand, AllocateStatPointsResult> handler,
                CancellationToken cancellationToken) =>
            {
                var identityResult = identityProvider.GetRequired();
                if (identityResult.IsFailure)
                {
                    return Results.Problem(
                        title: identityResult.Error.Code,
                        detail: identityResult.Error.Description,
                        statusCode: StatusCodes.Status401Unauthorized,
                        type: "https://tools.ietf.org/html/rfc7235#section-3.1");
                }

                var identity = identityResult.Value;
                var command = new AllocateStatPointsCommand(
                    IdentityId: identity.Subject,
                    ExpectedRevision: request.ExpectedRevision,
                    Str: request.Str,
                    Agi: request.Agi,
                    Intuition: request.Intuition,
                    Vit: request.Vit);

                var result = await handler.HandleAsync(command, cancellationToken);

                return result.Match(
                    value => Results.Ok(new AllocateStatPointsResponse(
                        value.Strength,
                        value.Agility,
                        value.Intuition,
                        value.Vitality,
                        value.UnspentPoints,
                        value.Revision)),
                    failure => result.ToProblem());
            })
            .WithRequestValidation<AllocateStatPointsRequest>()
            .WithTags(Tags.PlayersStats)
            .WithSummary("Allocate stat points")
            .WithDescription("Allocates unspent stat points for the current identity's character. Character must be named first (Draft → Named). Uses IdentityId (JWT sub) and ExpectedRevision for optimistic concurrency.")
            .RequireAuthorization()
            .Produces<AllocateStatPointsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status500InternalServerError);
    }
}
