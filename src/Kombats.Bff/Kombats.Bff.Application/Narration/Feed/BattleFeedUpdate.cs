namespace Kombats.Bff.Application.Narration.Feed;

/// <summary>
/// SignalR transport batch — one or more feed entries produced for a single event.
/// </summary>
public sealed record BattleFeedUpdate
{
    public required Guid BattleId { get; init; }
    public required BattleFeedEntry[] Entries { get; init; }
}
