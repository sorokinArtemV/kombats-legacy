namespace Kombats.Matchmaking.Infrastructure.Entities;

/// <summary>
/// EF Core entity for the Matchmaking-owned player combat profile projection.
/// Projected from Players service integration events.
/// Used for queue eligibility and future battle handoff.
/// </summary>
internal sealed class PlayerCombatProfileEntity
{
    public Guid IdentityId { get; set; }
    public Guid CharacterId { get; set; }
    public string? Name { get; set; }
    public int Level { get; set; }
    public int Strength { get; set; }
    public int Agility { get; set; }
    public int Intuition { get; set; }
    public int Vitality { get; set; }
    public bool IsReady { get; set; }
    public int Revision { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public string AvatarId { get; set; } = PlayerCombatProfileEntityDefaults.AvatarId;
}

internal static class PlayerCombatProfileEntityDefaults
{
    // Kept in sync with Kombats.Players.Domain.Entities.AvatarCatalog.Default.
    // Duplicated here to avoid a cross-service domain dependency — Matchmaking
    // receives avatar ids through the integration event and treats them as opaque.
    public const string AvatarId = "shadow_oni";
}
