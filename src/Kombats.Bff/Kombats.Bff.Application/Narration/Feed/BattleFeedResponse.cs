namespace Kombats.Bff.Application.Narration.Feed;

/// <summary>
/// HTTP post-match response — complete battle feed for a finished battle.
/// </summary>
public sealed record BattleFeedResponse
{
    public required Guid BattleId { get; init; }
    public required BattleFeedEntry[] Entries { get; init; }
}
