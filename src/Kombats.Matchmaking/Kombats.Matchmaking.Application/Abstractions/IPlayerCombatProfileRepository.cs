namespace Kombats.Matchmaking.Application.Abstractions;

/// <summary>
/// Port for Matchmaking-owned player combat profile projection.
/// </summary>
internal interface IPlayerCombatProfileRepository
{
    /// <summary>
    /// Gets a player combat profile by identity id.
    /// Returns null if no projection exists for this player.
    /// </summary>
    Task<PlayerCombatProfile?> GetByIdentityIdAsync(Guid identityId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Upserts a player combat profile projection.
    /// Only applies the update if the incoming revision is newer than the stored revision,
    /// preventing stale event overwrites.
    /// Returns true if the projection was updated, false if skipped (stale).
    /// </summary>
    Task<bool> UpsertAsync(PlayerCombatProfile profile, CancellationToken cancellationToken = default);
}

/// <summary>
/// Application-level read model for player combat profile projection.
/// </summary>
internal sealed class PlayerCombatProfile
{
    public required Guid IdentityId { get; init; }
    public required Guid CharacterId { get; init; }
    public string? Name { get; init; }
    public required int Level { get; init; }
    public required int Strength { get; init; }
    public required int Agility { get; init; }
    public required int Intuition { get; init; }
    public required int Vitality { get; init; }
    public required bool IsReady { get; init; }
    public required int Revision { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }

    // Avatar id projected from the Players integration event. Nullable on the wire
    // to tolerate older producers; default populated at projection time below.
    public string? AvatarId { get; init; }
}
