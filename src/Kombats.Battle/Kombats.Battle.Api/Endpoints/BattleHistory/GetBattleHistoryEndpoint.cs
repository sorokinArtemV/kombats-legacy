using System.Security.Claims;
using Kombats.Battle.Application.UseCases.GetBattleHistory;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Kombats.Battle.Api.Endpoints.BattleHistory;

public sealed class GetBattleHistoryEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/internal/battles/{battleId:guid}/history", HandleAsync)
            .RequireAuthorization()
            .WithName("GetBattleHistory")
            .WithTags("BattleHistory");
    }

    private static async Task<IResult> HandleAsync(
        Guid battleId,
        GetBattleHistoryHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var userIdClaim = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                          ?? httpContext.User.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            return Results.Unauthorized();

        var query = new GetBattleHistoryQuery(battleId, userId);

        BattleHistoryResult? result;
        try
        {
            result = await handler.HandleAsync(query, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            return Results.Forbid();
        }

        if (result is null)
            return Results.NotFound();

        var response = MapToResponse(result);
        return Results.Ok(response);
    }

    private static BattleHistoryResponse MapToResponse(BattleHistoryResult result)
    {
        return new BattleHistoryResponse
        {
            BattleId = result.BattleId,
            MatchId = result.MatchId,
            PlayerAId = result.PlayerAId,
            PlayerBId = result.PlayerBId,
            PlayerAName = result.PlayerAName,
            PlayerBName = result.PlayerBName,
            PlayerAMaxHp = result.PlayerAMaxHp,
            PlayerBMaxHp = result.PlayerBMaxHp,
            State = result.State,
            EndReason = result.EndReason,
            WinnerPlayerId = result.WinnerPlayerId,
            CreatedAt = result.CreatedAt,
            EndedAt = result.EndedAt,
            Turns = result.Turns.Select(t => new TurnHistoryResponse
            {
                TurnIndex = t.TurnIndex,
                AtoBAttackZone = t.AtoBAttackZone,
                AtoBDefenderBlockPrimary = t.AtoBDefenderBlockPrimary,
                AtoBDefenderBlockSecondary = t.AtoBDefenderBlockSecondary,
                AtoBWasBlocked = t.AtoBWasBlocked,
                AtoBWasCrit = t.AtoBWasCrit,
                AtoBOutcome = t.AtoBOutcome,
                AtoBDamage = t.AtoBDamage,
                BtoAAttackZone = t.BtoAAttackZone,
                BtoADefenderBlockPrimary = t.BtoADefenderBlockPrimary,
                BtoADefenderBlockSecondary = t.BtoADefenderBlockSecondary,
                BtoAWasBlocked = t.BtoAWasBlocked,
                BtoAWasCrit = t.BtoAWasCrit,
                BtoAOutcome = t.BtoAOutcome,
                BtoADamage = t.BtoADamage,
                PlayerAHpAfter = t.PlayerAHpAfter,
                PlayerBHpAfter = t.PlayerBHpAfter,
                ResolvedAt = t.ResolvedAt
            }).ToArray()
        };
    }
}
