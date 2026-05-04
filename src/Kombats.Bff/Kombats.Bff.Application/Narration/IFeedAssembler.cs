using Kombats.Bff.Application.Narration.Feed;
using Kombats.Bff.Application.Narration.Templates;

namespace Kombats.Bff.Application.Narration;

public interface IFeedAssembler
{
    BattleFeedEntry CreateEntry(
        Guid battleId,
        int turnIndex,
        int sequence,
        FeedEntryKind kind,
        NarrationTemplate template,
        string renderedText);

    BattleFeedUpdate CreateUpdate(Guid battleId, IReadOnlyList<BattleFeedEntry> entries);
}
