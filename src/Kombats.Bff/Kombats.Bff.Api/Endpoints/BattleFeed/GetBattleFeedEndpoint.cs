using Kombats.Bff.Application.Clients;
using Kombats.Bff.Application.Narration;
using Kombats.Bff.Application.Narration.Feed;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Kombats.Bff.Api.Endpoints.BattleFeed;

public sealed class GetBattleFeedEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/battles/{battleId:guid}/feed", HandleAsync)
            .RequireAuthorization()
            .WithName("GetBattleFeed")
            .WithTags("BattleFeed")
            .Produces<BattleFeedResponse>()
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);
    }

    private static async Task<IResult> HandleAsync(
        Guid battleId,
        IBattleClient battleClient,
        INarrationPipeline pipeline,
        CancellationToken cancellationToken)
    {
        // Call Battle's internal history endpoint (JWT forwarded automatically by JwtForwardingHandler).
        // Returns null for 404, throws BffServiceException for 403 (propagated by middleware).
        var historyResponse = await battleClient.GetHistoryAsync(battleId, cancellationToken);

        if (historyResponse is null)
            return Results.NotFound();

        // Map client response to narration input model
        var history = MapToNarrationHistory(historyResponse);

        // Generate feed through deterministic narration pipeline
        var entries = pipeline.GenerateFullBattleFeed(history);

        var response = new BattleFeedResponse
        {
            BattleId = battleId,
            Entries = entries
        };

        return Results.Ok(response);
    }

    private static BattleHistory MapToNarrationHistory(BattleHistoryResponse response)
    {
        return new BattleHistory
        {
            BattleId = response.BattleId,
            PlayerAId = response.PlayerAId,
            PlayerBId = response.PlayerBId,
            PlayerAName = response.PlayerAName,
            PlayerBName = response.PlayerBName,
            PlayerAMaxHp = response.PlayerAMaxHp,
            PlayerBMaxHp = response.PlayerBMaxHp,
            EndReason = response.EndReason,
            WinnerPlayerId = response.WinnerPlayerId,
            Turns = response.Turns.Select(t => new BattleHistoryTurn
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
                PlayerBHpAfter = t.PlayerBHpAfter
            }).ToArray()
        };
    }
}
