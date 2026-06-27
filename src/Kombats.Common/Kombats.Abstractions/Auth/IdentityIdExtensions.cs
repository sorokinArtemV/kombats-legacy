using System.Security.Claims;

namespace Kombats.Abstractions.Auth;

public static class IdentityIdExtensions
{
    /// <summary>
    /// Extracts the identity ID (Keycloak subject) from the user's claims.
    /// Returns null if the "sub" claim is not present or is not a valid GUID.
    /// </summary>
    public static Guid? GetIdentityId(this ClaimsPrincipal principal)
    {
        var sub = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? principal.FindFirstValue("sub");

        if (sub is not null && Guid.TryParse(sub, out var identityId))
        {
            return identityId;
        }

        return null;
    }
}
