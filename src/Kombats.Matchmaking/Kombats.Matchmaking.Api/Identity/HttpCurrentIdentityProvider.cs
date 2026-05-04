using Kombats.Abstractions;
using Kombats.Abstractions.Auth;
using Microsoft.AspNetCore.Http;

namespace Kombats.Matchmaking.Api.Identity;

internal sealed class HttpCurrentIdentityProvider : ICurrentIdentityProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpCurrentIdentityProvider(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Result<Guid> GetRequiredSubject()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true)
            return Result.Failure<Guid>(Error.Failure("Auth.Unauthenticated", "User is not authenticated."));

        var id = user.GetIdentityId();
        if (id is null)
            return Result.Failure<Guid>(Error.Failure("Auth.NoSubject", "Subject claim not found."));

        return id.Value;
    }
}
