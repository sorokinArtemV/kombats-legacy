using System.Net.Http.Json;
using System.Text.Json;
using Kombats.Chat.Application.Ports;
using Microsoft.Extensions.Logging;

namespace Kombats.Chat.Infrastructure.Services;

internal sealed class DisplayNameResolver(
    IPlayerInfoCache cache,
    IHttpClientFactory httpClientFactory,
    ILogger<DisplayNameResolver> logger) : IDisplayNameResolver
{
    private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(3);

    public async Task<string> ResolveAsync(Guid identityId, CancellationToken ct)
    {
        // 1. Cache hit
        var cached = await cache.GetAsync(identityId, ct);
        if (cached is not null)
            return cached.Name;

        // 2. HTTP call to Players service
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(HttpTimeout);

            var httpClient = httpClientFactory.CreateClient("Players");
            var response = await httpClient.GetAsync($"api/v1/players/{identityId}/profile", cts.Token);

            if (response.IsSuccessStatusCode)
            {
                var profile = await response.Content.ReadFromJsonAsync<PlayerProfileResponse>(cts.Token);
                if (profile is not null && !string.IsNullOrEmpty(profile.DisplayName))
                {
                    // Populate cache with name and canonical OnboardingState
                    string onboardingState = profile.OnboardingState ?? "Unknown";
                    await cache.SetAsync(identityId, new CachedPlayerInfo(profile.DisplayName, onboardingState), ct);
                    return profile.DisplayName;
                }
            }
            else
            {
                logger.LogWarning(
                    "Players profile lookup for {IdentityId} returned non-success {StatusCode}; falling back to Unknown",
                    identityId, (int)response.StatusCode);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "Failed to resolve display name for {IdentityId} from Players service", identityId);
        }

        // 3. Fallback — do NOT cache "Unknown"
        return "Unknown";
    }

    // Minimal DTO for deserializing the Players profile response.
    private sealed class PlayerProfileResponse
    {
        public string? DisplayName { get; set; }
        public string? OnboardingState { get; set; }
    }
}
