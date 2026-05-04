namespace Kombats.Bff.Application.Narration;

/// <summary>
/// Input model for GenerateFullBattleFeed. Represents the complete history of a battle
/// as retrieved from Battle's history endpoint. This is the BFF's view of the data;
/// Batch 6 will map from the HTTP client response to this type.
/// </summary>
public sealed record BattleHistory
{
    public required Guid BattleId { get; init; }
    public required Guid PlayerAId { get; init; }
    public required Guid PlayerBId { get; init; }
    public string? PlayerAName { get; init; }
    public string? PlayerBName { get; init; }
    public int? PlayerAMaxHp { get; init; }
    public int? PlayerBMaxHp { get; init; }
    public string? EndReason { get; init; }
    public Guid? WinnerPlayerId { get; init; }
    public required BattleHistoryTurn[] Turns { get; init; }
}

public sealed record BattleHistoryTurn
{
    public required int TurnIndex { get; init; }
    public string? AtoBAttackZone { get; init; }
    public string? AtoBDefenderBlockPrimary { get; init; }
    public string? AtoBDefenderBlockSecondary { get; init; }
    public bool AtoBWasBlocked { get; init; }
    public bool AtoBWasCrit { get; init; }
    public required string AtoBOutcome { get; init; }
    public int AtoBDamage { get; init; }
    public string? BtoAAttackZone { get; init; }
    public string? BtoADefenderBlockPrimary { get; init; }
    public string? BtoADefenderBlockSecondary { get; init; }
    public bool BtoAWasBlocked { get; init; }
    public bool BtoAWasCrit { get; init; }
    public required string BtoAOutcome { get; init; }
    public int BtoADamage { get; init; }
    public int PlayerAHpAfter { get; init; }
    public int PlayerBHpAfter { get; init; }
}
