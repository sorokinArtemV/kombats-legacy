using Kombats.Battle.Domain.Model;
using Kombats.Battle.Domain.Results;
using Kombats.Battle.Domain.Rules;

namespace Kombats.Battle.Application.ReadModels;

/// <summary>
/// Read model for battle state - used for UI queries and realtime notifications.
/// This is a read-only snapshot optimized for presentation.
/// </summary>
public sealed class BattleSnapshot
{
    public Guid BattleId { get; init; }
    public Guid MatchId { get; init; }
    public Guid PlayerAId { get; init; }
    public Guid PlayerBId { get; init; }
    public Ruleset Ruleset { get; init; } = null!;
    public BattlePhase Phase { get; init; }
    public int TurnIndex { get; init; }
    public DateTimeOffset DeadlineUtc { get; init; }
    public int NoActionStreakBoth { get; init; }
    public int LastResolvedTurnIndex { get; init; }
    public int Version { get; init; }
    
    // Participant metadata
    public string? PlayerAName { get; init; }
    public string? PlayerBName { get; init; }
    public int? PlayerAMaxHp { get; init; }
    public int? PlayerBMaxHp { get; init; }

    // Player HP
    public int? PlayerAHp { get; init; }
    public int? PlayerBHp { get; init; }
    
    // Player stats
    public int? PlayerAStrength { get; init; }
    public int? PlayerAStamina { get; init; }
    public int? PlayerAAgility { get; init; }
    public int? PlayerAIntuition { get; init; }
    public int? PlayerBStrength { get; init; }
    public int? PlayerBStamina { get; init; }
    public int? PlayerBAgility { get; init; }
    public int? PlayerBIntuition { get; init; }

    // Terminal outcome (only populated when Phase == Ended and the battle ended under the
    // enriched end-commit path). Used by BattleRecoveryService to avoid the data-less
    // OrphanRecovery fallback when Redis already knows the real winner/reason.
    public Guid? EndWinnerPlayerId { get; init; }
    public EndBattleReason? EndReason { get; init; }
    public int? EndFinalTurnIndex { get; init; }
    public DateTimeOffset? EndedAt { get; init; }
}





