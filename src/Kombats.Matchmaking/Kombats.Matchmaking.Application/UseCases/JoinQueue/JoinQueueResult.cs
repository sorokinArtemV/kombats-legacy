using Kombats.Matchmaking.Domain;

namespace Kombats.Matchmaking.Application.UseCases.JoinQueue;

public sealed record JoinQueueResult(QueuePlayerStatus Status, Guid? MatchId = null, Guid? BattleId = null, MatchState? MatchState = null);

public enum QueuePlayerStatus
{
    Searching,
    AlreadyMatched
}
