namespace Kombats.Matchmaking.Application.UseCases.ExecuteMatchmakingTick;

public sealed record MatchmakingTickResult(bool MatchCreated, Guid? MatchId = null, Guid? BattleId = null);
