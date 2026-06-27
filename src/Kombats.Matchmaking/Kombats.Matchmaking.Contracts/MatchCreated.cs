namespace Kombats.Matchmaking.Contracts;

/// <summary>
/// Integration event published by Matchmaking when a match is created and a battle is requested.
/// </summary>
public record MatchCreated
{
    public Guid MessageId { get; init; }
    public Guid MatchId { get; init; }
    public Guid PlayerAIdentityId { get; init; }
    public Guid PlayerBIdentityId { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    public int Version { get; init; } = 1;
}
