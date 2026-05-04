using Kombats.Battle.Domain.Model;
using Kombats.Battle.Application.Models;
using Kombats.Battle.Application.ReadModels;

namespace Kombats.Battle.Application.Ports;

/// <summary>
/// Port interface for battle state persistence.
/// Application defines what it needs; Infrastructure provides implementation.
/// Works with domain models for writes and read models for queries.
/// </summary>
public interface IBattleStateStore
{
    Task<bool> TryInitializeBattleAsync(
        Guid battleId,
        BattleDomainState initialState,
        string? playerAName,
        string? playerBName,
        int? playerAMaxHp,
        int? playerBMaxHp,
        CancellationToken cancellationToken = default);

    Task<BattleSnapshot?> GetStateAsync(Guid battleId, CancellationToken cancellationToken = default);

    Task<bool> TryOpenTurnAsync(
        Guid battleId,
        int turnIndex,
        DateTimeOffset deadlineUtc,
        CancellationToken cancellationToken = default);

    Task<bool> TryMarkTurnResolvingAsync(
        Guid battleId,
        int turnIndex,
        CancellationToken cancellationToken = default);

    Task<bool> MarkTurnResolvedAndOpenNextAsync(
        Guid battleId,
        int currentTurnIndex,
        int nextTurnIndex,
        DateTimeOffset nextDeadlineUtc,
        int noActionStreak,
        int playerAHp,
        int playerBHp,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically transitions the battle to Ended and persists the full terminal outcome.
    /// <paramref name="outcome"/> is written into Redis alongside Phase=Ended so that
    /// BattleRecoveryService can reconstruct a faithful BattleCompleted event if the
    /// process crashes between this call and the bus-outbox flush.
    /// </summary>
    Task<EndBattleCommitResult> EndBattleAndMarkResolvedAsync(
        Guid battleId,
        int turnIndex,
        int noActionStreak,
        int playerAHp,
        int playerBHp,
        BattleEndOutcome outcome,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Claims due battles from the deadlines ZSET atomically using Redis locks.
    /// For each due battle, attempts to acquire a lease lock for the specific battle turn.
    /// Only battles where the lock is successfully acquired are returned and removed from the ZSET.
    /// If battle state is missing, the battle is removed from ZSET and skipped.
    /// This ensures only one worker processes a given battle turn, preventing duplicate resolutions.
    /// </summary>
    Task<IReadOnlyList<ClaimedBattleDue>> ClaimDueBattlesAsync(
        DateTimeOffset nowUtc,
        int limit,
        TimeSpan leaseTtl,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores an action atomically and checks if both players have submitted actions.
    /// This is an optimization to avoid the extra GetActionsAsync roundtrip after storing.
    /// Uses first-write-wins semantics (SET NX in Redis) for each player's action.
    /// </summary>
    Task<ActionStoreAndCheckResult> StoreActionAndCheckBothSubmittedAsync(
        Guid battleId,
        int turnIndex,
        Guid playerId,
        Guid playerAId,
        Guid playerBId,
        PlayerActionCommand actionCommand,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves canonical action commands for both players in a specific turn.
    /// Returns null for a player if no action was stored.
    /// </summary>
    Task<(PlayerActionCommand? PlayerAAction, PlayerActionCommand? PlayerBAction)> GetActionsAsync(
        Guid battleId,
        int turnIndex,
        Guid playerAId,
        Guid playerBId,
        CancellationToken cancellationToken = default);
}
