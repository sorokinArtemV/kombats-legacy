namespace Kombats.Matchmaking.Contracts;

/// <summary>
/// Integration event published by Matchmaking when a match reaches terminal state.
/// </summary>
public record MatchCompleted
{
    public Guid MessageId { get; init; }
    public Guid MatchId { get; init; }
    public Guid PlayerAIdentityId { get; init; }
    public Guid PlayerBIdentityId { get; init; }
    public Guid? WinnerIdentityId { get; init; }
    public Guid? LoserIdentityId { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    public int Version { get; init; } = 1;
}
