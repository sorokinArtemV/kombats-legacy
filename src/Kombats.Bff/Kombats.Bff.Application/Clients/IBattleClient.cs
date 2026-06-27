namespace Kombats.Bff.Application.Clients;

public interface IBattleClient
{
    /// <summary>
    /// Retrieves complete battle history from Battle's internal endpoint.
    /// Returns null if battle not found (404).
    /// Throws BffServiceException for 403 (non-participant) and other errors.
    /// </summary>
    Task<BattleHistoryResponse?> GetHistoryAsync(Guid battleId, CancellationToken cancellationToken = default);
}
