using Kombats.Battle.Realtime.Contracts;

namespace Kombats.Bff.Application.Narration;

public interface ICommentatorPolicy
{
    CommentatorCue? Evaluate(
        TurnResolutionLogRealtime turnLog,
        BattleParticipantSnapshot participants,
        CommentatorState state,
        int? playerAHp, int? playerBHp,
        int? playerAMaxHp, int? playerBMaxHp);

    CommentatorCue? EvaluateBattleEnd(
        BattleEndReasonRealtime reason,
        Guid? winnerPlayerId,
        BattleParticipantSnapshot participants,
        CommentatorState state);
}
