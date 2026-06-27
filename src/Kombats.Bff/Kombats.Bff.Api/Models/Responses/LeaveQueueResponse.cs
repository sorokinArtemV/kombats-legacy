namespace Kombats.Bff.Api.Models.Responses;

public sealed record LeaveQueueResponse(
    bool LeftQueue,
    Guid? MatchId = null,
    Guid? BattleId = null);
