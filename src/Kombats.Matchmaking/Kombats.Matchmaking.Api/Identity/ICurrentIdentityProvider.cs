using Kombats.Abstractions;

namespace Kombats.Matchmaking.Api.Identity;

internal interface ICurrentIdentityProvider
{
    Result<Guid> GetRequiredSubject();
}
