namespace Kombats.Players.Contracts;

/// <summary>
/// Canonical integration event published by Players when a character's combat-relevant profile changes.
/// Consumers: Matchmaking (projection), optionally other read models.
/// Contains full combat snapshot so consumers do not need to query Players at runtime.
/// </summary>
public record PlayerCombatProfileChanged
{
    public Guid MessageId { get; init; }
    public Guid IdentityId { get; init; }
    public Guid CharacterId { get; init; }
    public string? Name { get; init; }
    public int Level { get; init; }
    public int Strength { get; init; }
    public int Agility { get; init; }
    public int Intuition { get; init; }
    public int Vitality { get; init; }
    public bool IsReady { get; init; }
    public int Revision { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    public int Version { get; init; } = 1;

    // Nullable on the wire so consumers predating this field keep deserializing;
    // producers populate it from the character's AvatarId.
    public string? AvatarId { get; init; }
}
