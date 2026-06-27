namespace Kombats.Battle.Contracts.Battle;

/// <summary>
/// Canonical integration event published by Battle when a battle reaches terminal state.
/// Consumers: Players (progression), Matchmaking (match lifecycle closure),
/// Battle (read-model projection).
/// </summary>
public record BattleCompleted
{
    public Guid MessageId { get; init; }
    public Guid BattleId { get; init; }
    public Guid MatchId { get; init; }
    public Guid PlayerAIdentityId { get; init; }
    public Guid PlayerBIdentityId { get; init; }

    /// <summary>Null when no winner (e.g. DoubleForfeit, mutual timeout).</summary>
    public Guid? WinnerIdentityId { get; init; }

    /// <summary>Null when no loser (e.g. DoubleForfeit, mutual timeout).</summary>
    public Guid? LoserIdentityId { get; init; }

    public BattleEndReason Reason { get; init; }
    public int TurnCount { get; init; }
    public int DurationMs { get; init; }
    public int RulesetVersion { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    public int Version { get; init; } = 1;
}
