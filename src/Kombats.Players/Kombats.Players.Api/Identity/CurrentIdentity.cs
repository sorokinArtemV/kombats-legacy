namespace Kombats.Players.Api.Identity;

/// <summary>
/// API-layer representation of the current request's identity from JWT claims.
/// </summary>
internal sealed record CurrentIdentity(
    Guid Subject,
    string? Username,
    string? Email);
