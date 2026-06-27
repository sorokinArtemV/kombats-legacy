using System.Security.Claims;

namespace Kombats.Players.Api.Extensions;

/// <summary>
/// Claim-parsing helpers only. No domain meaning.
/// </summary>
public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// Parses the "sub" claim (preferred) or NameIdentifier as Guid. Returns null if missing or invalid.
    /// </summary>
    public static Guid? GetSubjectId(this ClaimsPrincipal principal)
    {
        var sub = principal.FindFirst("sub")?.Value
                  ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(sub))
        {
            return null;
        }

        return Guid.TryParse(sub, out var id) ? id : null;
    }
}
