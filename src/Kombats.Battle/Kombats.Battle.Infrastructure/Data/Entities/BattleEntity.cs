namespace Kombats.Battle.Infrastructure.Data.Entities;

public class BattleEntity
{
    public Guid BattleId { get; set; }
    public Guid MatchId { get; set; }
    public Guid PlayerAId { get; set; }
    public Guid PlayerBId { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public string? EndReason { get; set; }
    public Guid? WinnerPlayerId { get; set; }
    public string? PlayerAName { get; set; }
    public string? PlayerBName { get; set; }
    public int? PlayerAMaxHp { get; set; }
    public int? PlayerBMaxHp { get; set; }
}