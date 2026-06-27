namespace Kombats.Matchmaking.Application.Abstractions;

/// <summary>
/// Per-(identity, connectionRef) liveness store for the matchmaking queue.
/// Mirrors the chat-presence model: ref-counted membership in a global "online"
/// ZSET, per-identity refs SET with a short TTL refreshed on every heartbeat.
/// When the TTL lapses (no heartbeat from any tab), the identity becomes
/// eligible for sweep cleanup.
/// </summary>
internal interface IQueuePresenceStore
{
    /// <summary>
    /// Records a new (identity, connectionRef) pair. Adds the connectionRef to
    /// the per-identity refs SET, refreshes its TTL, and updates the online
    /// ZSET score. Returns true if this connectionRef was newly added (idempotent
    /// on repeat calls with the same ref).
    /// </summary>
    Task<bool> RegisterAsync(Guid identityId, string connectionRef, CancellationToken ct);

    /// <summary>
    /// Heartbeat: refreshes the refs SET TTL and updates the online ZSET score.
    /// Idempotent: if no presence record exists yet (e.g. server restart), creates
    /// one as if RegisterAsync had been called.
    /// </summary>
    Task RefreshAsync(Guid identityId, string connectionRef, CancellationToken ct);

    /// <summary>
    /// Removes one connectionRef. If it was the last ref for the identity,
    /// removes from the online ZSET and deletes the refs SET. Returns true if
    /// this call removed the last ref (i.e. identity is now offline).
    /// </summary>
    Task<bool> UnregisterAsync(Guid identityId, string connectionRef, CancellationToken ct);

    /// <summary>
    /// Returns true if the identity has at least one live connectionRef.
    /// </summary>
    Task<bool> IsAliveAsync(Guid identityId, CancellationToken ct);

    /// <summary>
    /// Atomically sweeps identities whose latest heartbeat is older than the
    /// given threshold: ZREMs each from the online ZSET (only THIS call's
    /// successful removals are returned, gating duplicate cleanup) and DELs
    /// the refs SET defensively. Returns the identities removed by this call.
    /// </summary>
    Task<IReadOnlyList<Guid>> SweepStaleAsync(TimeSpan staleAfter, CancellationToken ct);
}