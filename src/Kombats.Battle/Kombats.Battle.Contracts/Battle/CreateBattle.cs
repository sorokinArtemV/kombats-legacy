namespace Kombats.Battle.Contracts.Battle;

/// <summary>
/// Command to create a new battle.
/// Matchmaking does NOT provide ruleset — Battle service selects from configuration.
/// Carries explicit participant snapshots (PlayerA, PlayerB) with combat stats.
/// </summary>
public record CreateBattle
{
    public Guid BattleId { get; init; }
    public Guid MatchId { get; init; }
    public DateTimeOffset RequestedAt { get; init; }

    /// <summary>Participant combat snapshot for Player A from Matchmaking projection.</summary>
    public BattleParticipantSnapshot PlayerA { get; init; } = null!;

    /// <summary>Participant combat snapshot for Player B from Matchmaking projection.</summary>
    public BattleParticipantSnapshot PlayerB { get; init; } = null!;
}
