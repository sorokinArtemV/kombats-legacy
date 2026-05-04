namespace Kombats.Players.Domain.Entities;

/// <summary>
/// Onboarding progression: Draft (no name) → Named (name set) → Ready (stats allocated at least once).
/// </summary>
public enum OnboardingState
{
    Draft = 0,
    Named = 1,
    Ready = 2
}
