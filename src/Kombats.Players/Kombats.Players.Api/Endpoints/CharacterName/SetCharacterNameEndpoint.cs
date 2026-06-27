using Kombats.Abstractions;
using Kombats.Players.Api.Endpoints.Me;
using Kombats.Players.Api.Extensions;
using Kombats.Players.Api.Filters;
using Kombats.Players.Api.Identity;
using Kombats.Players.Application;
using Kombats.Players.Application.UseCases.SetCharacterName;

namespace Kombats.Players.Api.Endpoints.CharacterName;

internal sealed class SetCharacterNameEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/character/name", async (
                SetCharacterNameRequest request,
                ICurrentIdentityProvider identityProvider,
                ICommandHandler<SetCharacterNameCommand, CharacterStateResult> handler,
                CancellationToken ct) =>
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

                var command = new SetCharacterNameCommand(identityResult.Value.Subject, request.Name);
                var result = await handler.HandleAsync(command, ct);

                return result.Match(
                    value => Results.Ok(MeResponse.FromCharacterState(value)),
                    failure => result.ToProblem());
            })
            .WithRequestValidation<SetCharacterNameRequest>()
            .RequireAuthorization()
            .WithTags(Tags.PlayersCharacter)
            .WithSummary("Set character name")
            .WithDescription("Sets the character display name once (Draft → Named). Name must be 3–16 characters and globally unique.")
            .Produces<MeResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }
}
