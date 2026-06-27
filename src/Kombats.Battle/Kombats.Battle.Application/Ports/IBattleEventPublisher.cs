using Kombats.Battle.Domain.Results;

namespace Kombats.Battle.Application.Ports;

/// <summary>
/// Port interface for publishing integration events.
/// Application defines what it needs; Infrastructure provides MassTransit implementation.
/// </summary>
public interface IBattleEventPublisher
{
    /// <summary>
    /// Publishes the canonical BattleCompleted integration event.
    /// Consumed by Players (progression), Matchmaking (match lifecycle closure),
    /// and Battle's own read-model projection.
    /// </summary>
    Task PublishBattleCompletedAsync(
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
        CancellationToken cancellationToken = default);
}
