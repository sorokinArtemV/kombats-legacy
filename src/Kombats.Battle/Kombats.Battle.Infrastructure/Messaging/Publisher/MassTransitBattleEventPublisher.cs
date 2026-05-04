using Kombats.Battle.Application.Ports;
using Kombats.Battle.Domain.Results;
using Kombats.Battle.Contracts.Battle;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Kombats.Battle.Infrastructure.Messaging.Publisher;

/// <summary>
/// MassTransit implementation of IBattleEventPublisher.
/// Publishes integration events via MassTransit (with outbox support).
/// Maps domain EndBattleReason to Contracts BattleEndReason.
/// </summary>
internal sealed class MassTransitBattleEventPublisher : IBattleEventPublisher
{
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<MassTransitBattleEventPublisher> _logger;

    public MassTransitBattleEventPublisher(
        IPublishEndpoint publishEndpoint,
        ILogger<MassTransitBattleEventPublisher> logger)
    {
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    public async Task PublishBattleCompletedAsync(
        Guid battleId,
        Guid matchId,
        Guid playerAId,
        Guid playerBId,
        EndBattleReason reason,
        Guid? winnerPlayerId,
        DateTimeOffset occurredAt,
        int turnCount,
        int durationMs,
        int rulesetVersion,
        CancellationToken cancellationToken = default)
    {
        var contractReason = MapReason(reason);

        // Derive loser: the participant who is not the winner. Null when no winner.
        Guid? loserPlayerId = winnerPlayerId.HasValue
            ? (winnerPlayerId.Value == playerAId ? playerBId : playerAId)
            : null;

        var battleCompleted = new BattleCompleted
        {
            MessageId = Guid.NewGuid(),
            BattleId = battleId,
            MatchId = matchId,
            PlayerAIdentityId = playerAId,
            PlayerBIdentityId = playerBId,
            WinnerIdentityId = winnerPlayerId,
            LoserIdentityId = loserPlayerId,
            Reason = contractReason,
            TurnCount = turnCount,
            DurationMs = durationMs,
            RulesetVersion = rulesetVersion,
            OccurredAt = occurredAt,
            Version = 1
        };

        await _publishEndpoint.Publish(battleCompleted, cancellationToken);

        // Terminal summary log — single line carrying every field an operator needs
        // to reconstruct a battle outcome without cross-referencing other logs.
        _logger.LogInformation(
            "Battle completed: BattleId={BattleId}, MatchId={MatchId}, Reason={Reason}, Winner={WinnerIdentityId}, Loser={LoserIdentityId}, TurnCount={TurnCount}, DurationMs={DurationMs}, RulesetVersion={RulesetVersion}",
            battleId, matchId, contractReason, winnerPlayerId, loserPlayerId, turnCount, durationMs, rulesetVersion);
    }

    private static BattleEndReason MapReason(EndBattleReason domainReason)
    {
        return domainReason switch
        {
            EndBattleReason.Normal => BattleEndReason.Normal,
            EndBattleReason.DoubleForfeit => BattleEndReason.DoubleForfeit,
            EndBattleReason.Timeout => BattleEndReason.Timeout,
            EndBattleReason.Cancelled => BattleEndReason.Cancelled,
            EndBattleReason.AdminForced => BattleEndReason.AdminForced,
            _ => BattleEndReason.SystemError
        };
    }
}
