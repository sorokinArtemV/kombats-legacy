using Kombats.Battle.Realtime.Contracts;
using Kombats.Bff.Application.Narration.Feed;

namespace Kombats.Bff.Application.Narration;

public interface INarrationPipeline
{
    BattleFeedUpdate GenerateTurnFeed(
        Guid battleId,
        TurnResolvedRealtime turnResolved,
        BattleParticipantSnapshot participants,
        CommentatorState commentatorState,
        int? playerAHp, int? playerBHp,
        int? playerAMaxHp, int? playerBMaxHp);

    BattleFeedUpdate GenerateBattleStartFeed(
        Guid battleId,
        BattleParticipantSnapshot participants);

    BattleFeedUpdate GenerateBattleEndFeed(
        Guid battleId,
        BattleEndedRealtime ended,
        BattleParticipantSnapshot participants,
        CommentatorState commentatorState);

    BattleFeedEntry[] GenerateFullBattleFeed(BattleHistory history);
}
