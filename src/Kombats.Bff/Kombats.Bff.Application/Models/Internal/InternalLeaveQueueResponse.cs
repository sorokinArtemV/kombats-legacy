namespace Kombats.Bff.Application.Models.Internal;

public sealed record InternalLeaveQueueResponse(
    bool Searching,
    Guid? MatchId = null,
    Guid? BattleId = null);
