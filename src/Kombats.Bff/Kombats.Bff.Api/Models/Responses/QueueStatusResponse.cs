namespace Kombats.Bff.Api.Models.Responses;

public sealed record QueueStatusResponse(
    string Status,
    Guid? MatchId = null,
    Guid? BattleId = null,
    string? MatchState = null);
