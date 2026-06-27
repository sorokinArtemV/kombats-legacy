namespace Kombats.Battle.Infrastructure.Data.Entities;

public class BattleTurnEntity
{
    public Guid BattleId { get; set; }
    public int TurnIndex { get; set; }
    // A→B
    public string? AtoBAttackZone { get; set; }
    public string? AtoBDefenderBlockPrimary { get; set; }
    public string? AtoBDefenderBlockSecondary { get; set; }
    public bool AtoBWasBlocked { get; set; }
    public bool AtoBWasCrit { get; set; }
    public string AtoBOutcome { get; set; } = string.Empty;
    public int AtoBDamage { get; set; }
    // B→A
    public string? BtoAAttackZone { get; set; }
    public string? BtoADefenderBlockPrimary { get; set; }
    public string? BtoADefenderBlockSecondary { get; set; }
    public bool BtoAWasBlocked { get; set; }
    public bool BtoAWasCrit { get; set; }
    public string BtoAOutcome { get; set; } = string.Empty;
    public int BtoADamage { get; set; }
    // Post-turn state
    public int PlayerAHpAfter { get; set; }
    public int PlayerBHpAfter { get; set; }
    public DateTimeOffset ResolvedAt { get; set; }
}
