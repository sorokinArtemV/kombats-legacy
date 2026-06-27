using Kombats.Matchmaking.Domain;

namespace Kombats.Matchmaking.Application.UseCases.GetQueueStatus;

public sealed record QueueStatusResult(QueueStatusType Status, Guid? MatchId = null, Guid? BattleId = null, MatchState? MatchState = null);

public enum QueueStatusType
{
    NotQueued,
    Searching,
    Matched
}
