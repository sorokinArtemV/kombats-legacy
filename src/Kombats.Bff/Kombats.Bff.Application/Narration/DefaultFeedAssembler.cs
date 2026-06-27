using Kombats.Bff.Application.Narration.Feed;
using Kombats.Bff.Application.Narration.Templates;

namespace Kombats.Bff.Application.Narration;

/// <summary>
/// Assembles feed entries with deterministic keys and sequencing.
/// Entry key format: "{battleId}:{turnIndex}:{sequence}"
/// </summary>
public sealed class DefaultFeedAssembler : IFeedAssembler
{
    public BattleFeedEntry CreateEntry(
        Guid battleId,
        int turnIndex,
        int sequence,
        FeedEntryKind kind,
        NarrationTemplate template,
        string renderedText)
    {
        return new BattleFeedEntry
        {
            Key = $"{battleId}:{turnIndex}:{sequence}",
            BattleId = battleId,
            TurnIndex = turnIndex,
            Sequence = sequence,
            Kind = kind,
            Severity = template.Severity,
            Tone = template.Tone,
            Text = renderedText
        };
    }

    public BattleFeedUpdate CreateUpdate(Guid battleId, IReadOnlyList<BattleFeedEntry> entries)
    {
        return new BattleFeedUpdate
        {
            BattleId = battleId,
            Entries = entries.ToArray()
        };
    }
}
