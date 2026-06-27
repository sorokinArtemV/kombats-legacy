namespace Kombats.Chat.Application.Ports;

internal interface IEligibilityChecker
{
    /// <summary>
    /// Checks if a player is eligible for chat.
    /// Returns (eligible, displayName) — displayName is set when eligible.
    /// </summary>
    Task<EligibilityResult> CheckEligibilityAsync(Guid identityId, CancellationToken ct);
}

internal sealed record EligibilityResult(bool Eligible, string? DisplayName = null);
