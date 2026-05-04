namespace Kombats.Battle.Application.UseCases.GetBattleHistory;

public sealed record GetBattleHistoryQuery(Guid BattleId, Guid RequestingPlayerId);
