using Kombats.Abstractions;
using Kombats.Players.Api.Extensions;
using Kombats.Players.Api.Filters;
using Kombats.Players.Api.Identity;
using Kombats.Players.Application.UseCases.ChangeAvatar;
using Microsoft.AspNetCore.Http;

namespace Kombats.Players.Api.Endpoints.Avatar;

internal sealed class ChangeAvatarEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/character/avatar", async (
                ChangeAvatarRequest request,
                ICurrentIdentityProvider identityProvider,
                ICommandHandler<ChangeAvatarCommand, ChangeAvatarResult> handler,
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

                var command = new ChangeAvatarCommand(
                    IdentityId: identityResult.Value.Subject,
                    ExpectedRevision: request.ExpectedRevision,
                    AvatarId: request.AvatarId);

                var result = await handler.HandleAsync(command, cancellationToken);

                return result.Match(
                    value => Results.Ok(new ChangeAvatarResponse(value.AvatarId, value.Revision)),
                    failure => result.ToProblem());
            })
            .WithRequestValidation<ChangeAvatarRequest>()
            .RequireAuthorization()
            .WithTags(Tags.PlayersCharacter)
            .WithSummary("Change character avatar")
            .WithDescription("Updates the character avatar to one of the backend-controlled catalog ids. Uses IdentityId (JWT sub) and ExpectedRevision for optimistic concurrency.")
            .Produces<ChangeAvatarResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }
}
