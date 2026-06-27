using Kombats.Battle.Domain.Results;

namespace Kombats.Battle.Application.Ports;

public interface IBattleTurnHistoryStore
{
    /// <summary>
    /// Persists turn resolution. Idempotent: duplicate (battleId, turnIndex) is a no-op.
    /// </summary>
    Task PersistTurnAsync(
        Guid battleId, int turnIndex, TurnResolutionLog log,
        int playerAHpAfter, int playerBHpAfter,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds turn entity to DbContext change tracker without calling SaveChangesAsync.
    /// Used for battle-ending turns where the outbox flush handles the commit.
    /// </summary>
    void TrackTurn(
        Guid battleId, int turnIndex, TurnResolutionLog log,
        int playerAHpAfter, int playerBHpAfter);
}
