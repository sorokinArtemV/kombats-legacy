namespace Kombats.Matchmaking.Application.UseCases.LeaveQueue;

public sealed record LeaveQueueResult(LeaveQueueStatus Status, Guid? MatchId = null, Guid? BattleId = null);

public enum LeaveQueueStatus
{
    Left,
    NotInQueue,
    AlreadyMatched
}
