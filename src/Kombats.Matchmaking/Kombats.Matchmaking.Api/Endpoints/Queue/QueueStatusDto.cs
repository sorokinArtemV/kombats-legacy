namespace Kombats.Matchmaking.Api.Endpoints.Queue;

public sealed record QueueStatusDto(string Status, Guid? MatchId = null, Guid? BattleId = null, string? MatchState = null);
