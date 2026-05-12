using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Kombats.LoadTests.Configuration;
using Microsoft.Extensions.Logging;

namespace Kombats.LoadTests.Authentication;

/// <summary>
/// Real OAuth Resource Owner Password Credentials (ROPC) client against the
/// kombats-loadtest Keycloak client. Caches access tokens per username for
/// the configured token lifetime minus a small safety margin.
/// </summary>
internal sealed class KeycloakTokenClient : IDisposable
{
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(5);

    private readonly TargetOptions _target;
    private readonly HttpClient _http;
    private readonly ILogger<KeycloakTokenClient> _logger;
    private readonly ConcurrentDictionary<string, CachedToken> _cache = new();

    public KeycloakTokenClient(TargetOptions target, ILogger<KeycloakTokenClient> logger)
    {
        _target = target;
        _http = new HttpClient { BaseAddress = new Uri(target.KeycloakBaseUrl) };
        _logger = logger;
    }

    public async Task<string> GetAccessTokenAsync(UserCredentials user, CancellationToken ct)
    {
        if (_cache.TryGetValue(user.Username, out var cached) && cached.IsFresh(DateTimeOffset.UtcNow))
        {
            return cached.Token;
        }

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = _target.ClientId,
            ["client_secret"] = _target.ClientSecret,
            ["username"] = user.Username,
            ["password"] = user.Password,
        });

        using var resp = await _http.PostAsync(
            $"/realms/{_target.Realm}/protocol/openid-connect/token", form, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"ROPC failed for {user.Username}: HTTP {(int)resp.StatusCode}. Body: {body}");
        }

        using var doc = JsonDocument.Parse(body);
        var accessToken = doc.RootElement.GetProperty("access_token").GetString()
                          ?? throw new InvalidOperationException("access_token missing");

        var expiresAt = ExtractExpiry(accessToken) - RefreshMargin;
        _cache[user.Username] = new CachedToken(accessToken, expiresAt);
        _logger.LogDebug("Cached token for {Username}, expires at {ExpiresAt:O}", user.Username, expiresAt);
        return accessToken;
    }

    private static DateTimeOffset ExtractExpiry(string accessToken)
    {
        // Plain base64url-decode of the JWT payload. We trust this token because
        // we just got it from a successful ROPC call; we're not validating it
        // (services do that).
        var parts = accessToken.Split('.');
        if (parts.Length < 2)
        {
            return DateTimeOffset.UtcNow + TimeSpan.FromMinutes(30);
        }
        var payload = parts[1].Replace('-', '+').Replace('_', '/');
        switch (payload.Length % 4)
        {
            case 2: payload += "=="; break;
            case 3: payload += "="; break;
        }
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("exp", out var expElement))
        {
            return DateTimeOffset.UtcNow + TimeSpan.FromMinutes(30);
        }
        return DateTimeOffset.FromUnixTimeSeconds(expElement.GetInt64());
    }

    public void Dispose() => _http.Dispose();

    private readonly record struct CachedToken(string Token, DateTimeOffset ExpiresAt)
    {
        public bool IsFresh(DateTimeOffset now) => now < ExpiresAt;
    }
}
