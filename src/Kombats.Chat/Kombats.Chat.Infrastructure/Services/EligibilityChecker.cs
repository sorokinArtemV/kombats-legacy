using System.Net.Http.Json;
using System.Text.Json;
using Kombats.Chat.Application.Ports;
using Microsoft.Extensions.Logging;

namespace Kombats.Chat.Infrastructure.Services;

internal sealed class EligibilityChecker(
    IPlayerInfoCache cache,
    IHttpClientFactory httpClientFactory,
    ILogger<EligibilityChecker> logger) : IEligibilityChecker
{
    private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(3);

    public async Task<EligibilityResult> CheckEligibilityAsync(Guid identityId, CancellationToken ct)
    {
        // 1. Check cache — derive eligibility from OnboardingState
        var cached = await cache.GetAsync(identityId, ct);
        if (cached is not null)
        {
            return cached.IsEligible
                ? new EligibilityResult(true, cached.Name)
                : new EligibilityResult(false);
        }

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
                if (profile is not null)
                {
                    string name = profile.DisplayName ?? "Unknown";
                    string onboardingState = profile.OnboardingState ?? "Unknown";

                    // Populate cache with canonical OnboardingState
                    var info = new CachedPlayerInfo(name, onboardingState);
                    await cache.SetAsync(identityId, info, ct);

                    return info.IsEligible
                        ? new EligibilityResult(true, name)
                        : new EligibilityResult(false);
                }
            }
            else
            {
                // Previously silent: 401/403/404 fell through to false and surfaced as
                // not_eligible with no trail. Log at Warning so auth-forwarding or
                // Players-side issues are diagnosable from Chat logs alone.
                logger.LogWarning(
                    "Players profile lookup for {IdentityId} returned non-success {StatusCode}; treating as not eligible",
                    identityId, (int)response.StatusCode);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "Failed to check eligibility for {IdentityId} from Players service", identityId);
        }

        // 3. HTTP failure/timeout → reject
        return new EligibilityResult(false);
    }

    private sealed class PlayerProfileResponse
    {
        public string? DisplayName { get; set; }
        public string? OnboardingState { get; set; }
    }
}
