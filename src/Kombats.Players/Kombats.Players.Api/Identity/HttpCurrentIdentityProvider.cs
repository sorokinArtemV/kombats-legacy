using Kombats.Players.Api.Extensions;
using Kombats.Abstractions;
using Microsoft.AspNetCore.Http;

namespace Kombats.Players.Api.Identity;

internal sealed class HttpCurrentIdentityProvider : ICurrentIdentityProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpCurrentIdentityProvider(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Result<CurrentIdentity> GetRequired()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            return Result.Failure<CurrentIdentity>(Error.Failure("Identity.Unauthenticated", "Request is not authenticated."));
        }

        var subject = user.GetSubjectId();
        if (subject is null)
        {
            return Result.Failure<CurrentIdentity>(Error.Failure("Identity.MissingOrInvalid", "Missing or invalid 'sub' claim."));
        }

        var identity = new CurrentIdentity(
            Subject: subject.Value,
            Username: user.FindFirst("preferred_username")?.Value,
            Email: user.FindFirst("email")?.Value);

        return Result.Success(identity);
    }
}
