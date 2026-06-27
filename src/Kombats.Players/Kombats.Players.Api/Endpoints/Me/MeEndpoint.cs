using Kombats.Abstractions;
using Kombats.Players.Api.Extensions;
using Kombats.Players.Api.Identity;
using Kombats.Players.Application;
using Kombats.Players.Application.UseCases.EnsureCharacterExists;
using Kombats.Players.Application.UseCases.GetCharacter;

namespace Kombats.Players.Api.Endpoints.Me;

internal sealed class MeEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        // GET /api/me — character-centric; 404 if not provisioned
        app.MapGet("api/v1/me", async (
                ICurrentIdentityProvider identityProvider,
                IQueryHandler<GetCharacterQuery, CharacterStateResult> getCharacterHandler,
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

                var result = await getCharacterHandler.HandleAsync(
                    new GetCharacterQuery(identityResult.Value.Subject), ct);

                return result.Match(
                    value => Results.Ok(MeResponse.FromCharacterState(value)),
                    failure => result.ToProblem());
            })
            .RequireAuthorization()
            .WithTags(Tags.Account)
            .WithSummary("Current character")
            .WithDescription("Returns the current identity's character and onboarding state. Returns 404 if not provisioned; call POST /api/me/ensure first.")
            .Produces<MeResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // POST /api/me/ensure — idempotent provisioning
        app.MapPost("api/v1/me/ensure", async (
                ICurrentIdentityProvider identityProvider,
                ICommandHandler<EnsureCharacterExistsCommand, CharacterStateResult> ensureHandler,
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

                var ensureResult = await ensureHandler.HandleAsync(
                    new EnsureCharacterExistsCommand(identityResult.Value.Subject), ct);

                return ensureResult.Match(
                    value => Results.Ok(MeResponse.FromCharacterState(value)),
                    failure => ensureResult.ToProblem());
            })
            .RequireAuthorization()
            .WithTags(Tags.Account)
            .WithSummary("Ensure character exists")
            .WithDescription("Idempotently creates a draft character for the current identity if missing. Always returns 200 with character snapshot when authenticated.")
            .Produces<MeResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict);

        // GET /api/me/claims — development only: dump JWT claims
        var env = app.ServiceProvider.GetRequiredService<IHostEnvironment>();
        if (env.IsDevelopment())
        {
            app.MapGet("api/v1/me/claims", (HttpContext httpContext) =>
                {
                    var user = httpContext.User;
                    var claims = user.Claims.Select(c => new ClaimDto(c.Type, c.Value)).ToArray();
                    return Results.Ok(new { subject = user.FindFirst("sub")?.Value, claims });
                })
                .RequireAuthorization()
                .WithTags(Tags.Account)
                .WithSummary("Current claims (dev)")
                .WithDescription("Returns the current JWT claims. For development or debugging.")
                .Produces<object>();
        }
    }
}
