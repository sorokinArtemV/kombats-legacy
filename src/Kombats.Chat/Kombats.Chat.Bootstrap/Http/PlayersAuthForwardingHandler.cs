using Microsoft.AspNetCore.Authentication;
using Microsoft.Net.Http.Headers;

namespace Kombats.Chat.Bootstrap.Http;

/// <summary>
/// Forwards the inbound caller's bearer token to the Players service when Chat
/// falls back to HTTP (eligibility / display-name cache miss). Without this,
/// Players responds 401, the callers swallow the non-success silently, and
/// eligibility resolves to <c>not_eligible</c> for otherwise-ready players.
/// Requires <c>JwtBearerOptions.SaveToken = true</c> on Chat's auth config.
/// Falls back to the raw Authorization header or the SignalR <c>access_token</c>
/// query string so WebSocket-originated hub invocations are also covered.
/// </summary>
internal sealed class PlayersAuthForwardingHandler(IHttpContextAccessor httpContextAccessor)
    : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        HttpContext? httpContext = httpContextAccessor.HttpContext;
        if (httpContext is not null && !request.Headers.Contains(HeaderNames.Authorization))
        {
            string? token = await httpContext.GetTokenAsync("access_token");

            if (string.IsNullOrEmpty(token))
            {
                string? authorization = httpContext.Request.Headers[HeaderNames.Authorization].ToString();
                if (!string.IsNullOrEmpty(authorization) &&
                    authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    token = authorization["Bearer ".Length..].Trim();
                }
            }

            if (string.IsNullOrEmpty(token))
            {
                string? queryToken = httpContext.Request.Query["access_token"].ToString();
                if (!string.IsNullOrEmpty(queryToken))
                {
                    token = queryToken;
                }
            }

            if (!string.IsNullOrEmpty(token))
            {
                request.Headers.TryAddWithoutValidation(HeaderNames.Authorization, $"Bearer {token}");
            }
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
