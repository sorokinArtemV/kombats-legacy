using Kombats.Battle.Application.UseCases.GetBattleHistory;

namespace Kombats.Battle.Application.Ports;

public interface IBattleHistoryRepository
{
    Task<BattleHistoryResult?> GetBattleHistoryAsync(Guid battleId, CancellationToken cancellationToken = default);
}
