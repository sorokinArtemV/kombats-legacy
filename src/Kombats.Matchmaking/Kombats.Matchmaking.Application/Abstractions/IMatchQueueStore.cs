namespace Kombats.Matchmaking.Application.Abstractions;

/// <summary>
/// Port for queue operations (atomic join/leave/pop pairs).
/// </summary>
internal interface IMatchQueueStore
{
    /// <summary>
    /// Adds a player to the queue atomically (idempotent if already queued).
    /// Returns true if the player was added, false if already in queue.
    /// </summary>
    Task<bool> TryJoinQueueAsync(string variant, Guid playerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a player from the queue atomically (idempotent if not in queue).
    /// Returns true if the player was removed, false if not in queue.
    /// </summary>
    Task<bool> TryLeaveQueueAsync(string variant, Guid playerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically pops a pair of players from the queue.
    /// Returns the pair if both players are available, null otherwise.
    /// </summary>
    Task<(Guid PlayerAId, Guid PlayerBId)?> TryPopPairAsync(string variant, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically re-adds a player to the head of the queue (LPUSH + SADD).
    /// Used to restore queue state when a popped player cannot be matched (e.g., missing profile).
    /// Idempotent: no-op if player is already in the queued set.
    /// </summary>
    Task TryRequeueAsync(string variant, Guid playerId, CancellationToken cancellationToken = default);
}





