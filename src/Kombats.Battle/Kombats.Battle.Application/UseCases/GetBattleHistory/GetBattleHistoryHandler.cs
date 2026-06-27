using Kombats.Battle.Application.Ports;

namespace Kombats.Battle.Application.UseCases.GetBattleHistory;

public sealed class GetBattleHistoryHandler
{
    private readonly IBattleHistoryRepository _repository;

    public GetBattleHistoryHandler(IBattleHistoryRepository repository)
    {
        _repository = repository;
    }

    /// <summary>
    /// Returns battle history for the given query.
    /// Returns null if battle not found.
    /// Throws InvalidOperationException if the requesting player is not a participant.
    /// </summary>
    public async Task<BattleHistoryResult?> HandleAsync(GetBattleHistoryQuery query, CancellationToken cancellationToken = default)
    {
        var result = await _repository.GetBattleHistoryAsync(query.BattleId, cancellationToken);

        if (result is null)
            return null;

        if (result.PlayerAId != query.RequestingPlayerId && result.PlayerBId != query.RequestingPlayerId)
            throw new InvalidOperationException("User is not a participant in this battle");

        return result;
    }
}
