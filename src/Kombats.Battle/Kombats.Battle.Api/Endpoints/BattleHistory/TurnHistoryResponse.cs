namespace Kombats.Battle.Api.Endpoints.BattleHistory;

public sealed record TurnHistoryResponse
{
    public int TurnIndex { get; init; }
    public string? AtoBAttackZone { get; init; }
    public string? AtoBDefenderBlockPrimary { get; init; }
    public string? AtoBDefenderBlockSecondary { get; init; }
    public bool AtoBWasBlocked { get; init; }
    public bool AtoBWasCrit { get; init; }
    public string AtoBOutcome { get; init; } = string.Empty;
    public int AtoBDamage { get; init; }
    public string? BtoAAttackZone { get; init; }
    public string? BtoADefenderBlockPrimary { get; init; }
    public string? BtoADefenderBlockSecondary { get; init; }
    public bool BtoAWasBlocked { get; init; }
    public bool BtoAWasCrit { get; init; }
    public string BtoAOutcome { get; init; } = string.Empty;
    public int BtoADamage { get; init; }
    public int PlayerAHpAfter { get; init; }
    public int PlayerBHpAfter { get; init; }
    public DateTimeOffset ResolvedAt { get; init; }
}
