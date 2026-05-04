namespace Kombats.Chat.Application.Ports;

internal interface IPlayerInfoCache
{
    Task<CachedPlayerInfo?> GetAsync(Guid identityId, CancellationToken ct);
    Task SetAsync(Guid identityId, CachedPlayerInfo info, CancellationToken ct);
    Task RemoveAsync(Guid identityId, CancellationToken ct);
}

/// <summary>
/// Cached player info. OnboardingState is the canonical eligibility signal from Players.
/// Eligibility is derived as: OnboardingState == "Ready".
/// </summary>
internal sealed record CachedPlayerInfo(string Name, string OnboardingState)
{
    public bool IsEligible => string.Equals(OnboardingState, "Ready", StringComparison.OrdinalIgnoreCase);
}
