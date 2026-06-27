using Kombats.Abstractions;

namespace Kombats.Players.Api.Identity;

/// <summary>
/// Provides the current request's identity. Returns a result so endpoints can map failure to 401.
/// </summary>
internal interface ICurrentIdentityProvider
{
    /// <summary>
    /// Gets the current identity from the request (e.g. JWT). Returns failure if not authenticated or sub is missing/invalid.
    /// </summary>
    Result<CurrentIdentity> GetRequired();
}
