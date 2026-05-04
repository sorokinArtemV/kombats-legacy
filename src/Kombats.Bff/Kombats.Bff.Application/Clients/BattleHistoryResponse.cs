namespace Kombats.Bff.Application.Clients;

/// <summary>
/// Client-side response model for Battle's internal history endpoint.
/// Maps to the API response from GET /api/internal/battles/{battleId}/history.
/// </summary>
public sealed record BattleHistoryResponse
{
    public Guid BattleId { get; init; }
    public Guid MatchId { get; init; }
    public Guid PlayerAId { get; init; }
    public Guid PlayerBId { get; init; }
    public string? PlayerAName { get; init; }
    public string? PlayerBName { get; init; }
    public int? PlayerAMaxHp { get; init; }
    public int? PlayerBMaxHp { get; init; }
    public string State { get; init; } = string.Empty;
    public string? EndReason { get; init; }
    public Guid? WinnerPlayerId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? EndedAt { get; init; }
    public TurnHistoryResponse[] Turns { get; init; } = [];
}
