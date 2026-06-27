namespace Kombats.Battle.Application.Ports;

/// <summary>
/// Holds the minimal info needed to publish BattleCompleted for an orphaned battle.
/// </summary>
public sealed record OrphanedBattleInfo(Guid MatchId, Guid PlayerAId, Guid PlayerBId, DateTimeOffset CreatedAt);

/// <summary>
/// Port for querying and recovering battle records (orphan sweep, stuck detection).
/// Queries the Postgres read model, not Redis active state.
/// </summary>
public interface IBattleRecoveryRepository
{
    /// <summary>
    /// Returns battle IDs that are in non-terminal state (not "Ended") and were created before the cutoff.
    /// These are candidates for recovery: either stuck-in-Resolving or orphaned.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetNonTerminalBattleIdsOlderThanAsync(
        DateTimeOffset cutoff,
        int limit,
        CancellationToken ct = default);

    /// <summary>
    /// Marks a non-terminal battle as ended with OrphanRecovery reason.
    /// The change is tracked but NOT saved — the caller must call <see cref="CommitAsync"/>
    /// after publishing the outbox event so both are persisted atomically.
    /// Returns battle info needed for the BattleCompleted event, or null if already ended.
    /// </summary>
    Task<OrphanedBattleInfo?> TryMarkOrphanedBattleEndedAsync(
        Guid battleId,
        DateTimeOffset endedAt,
        CancellationToken ct = default);

    /// <summary>
    /// Persists all pending changes (battle state + outbox records) in a single transaction.
    /// </summary>
    Task CommitAsync(CancellationToken ct = default);
}
