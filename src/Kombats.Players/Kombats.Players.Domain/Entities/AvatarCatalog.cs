namespace Kombats.Players.Domain.Entities;

/// <summary>
/// Backend-authoritative catalog of allowed player avatar ids.
/// Avatars are predefined cosmetic identifiers; visual assets are resolved by the frontend.
/// Ids are treated as opaque strings on the wire and in downstream projections.
/// Deprecated ids must remain in <see cref="AllowedIds"/> so historical rows stay valid;
/// do not hard-delete entries, add new ones by appending.
/// </summary>
public static class AvatarCatalog
{
    /// <summary>
    /// Avatar id assigned to newly created characters.
    /// </summary>
    public const string Default = "shadow_oni";

    /// <summary>
    /// Legacy default value retained as an alias so historical rows remain valid.
    /// New characters use <see cref="Default"/> instead.
    /// </summary>
    public const string LegacyDefault = "default";

    public static readonly IReadOnlySet<string> AllowedIds = new HashSet<string>(StringComparer.Ordinal)
    {
        LegacyDefault,
        "female_archer",
        "female_ninja",
        "ronin",
        "shadow_assassin",
        Default,
        "silhouette",
    };

    public const int MaxLength = 64;

    public static bool IsValid(string? avatarId)
        => avatarId is not null && AllowedIds.Contains(avatarId);
}
