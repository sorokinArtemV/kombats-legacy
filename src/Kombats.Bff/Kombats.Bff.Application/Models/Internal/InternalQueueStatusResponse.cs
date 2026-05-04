namespace Kombats.Bff.Application.Models.Internal;

public sealed record InternalQueueStatusResponse(
    string Status,
    Guid? MatchId = null,
    Guid? BattleId = null,
    string? MatchState = null);
