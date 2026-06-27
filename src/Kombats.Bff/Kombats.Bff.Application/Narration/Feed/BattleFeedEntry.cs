namespace Kombats.Bff.Application.Narration.Feed;

public sealed record BattleFeedEntry
{
    public required string Key { get; init; }
    public required Guid BattleId { get; init; }
    public required int TurnIndex { get; init; }
    public required int Sequence { get; init; }
    public required FeedEntryKind Kind { get; init; }
    public required FeedEntrySeverity Severity { get; init; }
    public required FeedEntryTone Tone { get; init; }
    public required string Text { get; init; }
}
