namespace Kombats.Battle.Realtime.Contracts;

/// <summary>
/// Realtime contract for BattleEnded event.
/// </summary>
/// <remarks>
/// WinnerXp / LoserXp are populated when the battle has a winner so the result
/// screen can render the XP row from the SignalR payload directly, without
/// waiting for the async Players handler. Both null when there is no winner
/// (draw, double forfeit, system error) — mirrors the Players award rule
/// exactly: XP is awarded iff WinnerIdentityId and LoserIdentityId are both
/// set on BattleCompleted, which Battle constructs from winnerPlayerId.HasValue.
/// </remarks>
public record BattleEndedRealtime
{
    public Guid BattleId { get; init; }
    public BattleEndReasonRealtime Reason { get; init; }
    public Guid? WinnerPlayerId { get; init; }
    public DateTimeOffset EndedAt { get; init; }
    public int? WinnerXp { get; init; }
    public int? LoserXp { get; init; }
}






