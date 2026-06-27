using Kombats.Matchmaking.Domain;

namespace Kombats.Matchmaking.Application.Abstractions;

/// <summary>
/// Port for match repository operations (Postgres source of truth).
/// </summary>
internal interface IMatchRepository
{
    /// <summary>
    /// Gets the latest active match for a player (by PlayerAId or PlayerBId).
    /// Returns null if no active match found.
    /// </summary>
    Task<Match?> GetActiveForPlayerAsync(Guid playerId, CancellationToken ct = default);

    /// <summary>
    /// Gets a match by MatchId.
    /// </summary>
    Task<Match?> GetByMatchIdAsync(Guid matchId, CancellationToken ct = default);

    /// <summary>
    /// Gets a match by BattleId.
    /// </summary>
    Task<Match?> GetByBattleIdAsync(Guid battleId, CancellationToken ct = default);

    /// <summary>
    /// Persists a new match (must be in Queued state).
    /// </summary>
    void Add(Match match);

    /// <summary>
    /// Attempts to CAS-update match state: BattleCreateRequested -> BattleCreated.
    /// Returns true if updated, false if state mismatch.
    /// </summary>
    Task<bool> TryAdvanceToBattleCreatedAsync(Guid matchId, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>
    /// Attempts to CAS-update match state: BattleCreated -> Completed or TimedOut.
    /// Returns true if updated, false if state mismatch.
    /// </summary>
    Task<bool> TryAdvanceToTerminalAsync(Guid matchId, MatchState terminalState, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>
    /// Bulk timeout: transitions all BattleCreateRequested matches older than threshold to TimedOut.
    /// Returns player ID pairs for each timed-out match (for Redis status cleanup).
    /// </summary>
    Task<List<(Guid PlayerAId, Guid PlayerBId)>> TimeoutStaleMatchesAsync(DateTimeOffset cutoff, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>
    /// Bulk timeout: transitions all BattleCreated matches older than threshold to TimedOut.
    /// Covers the gap where Battle service accepted but never completed a battle. See EI-015.
    /// Returns player ID pairs for each timed-out match (for Redis status cleanup).
    /// </summary>
    Task<List<(Guid PlayerAId, Guid PlayerBId)>> TimeoutStaleBattleCreatedMatchesAsync(DateTimeOffset cutoff, DateTimeOffset now, CancellationToken ct = default);
}

