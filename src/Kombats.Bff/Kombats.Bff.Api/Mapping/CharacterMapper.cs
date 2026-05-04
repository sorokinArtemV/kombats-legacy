using Kombats.Bff.Api.Models.Responses;
using Kombats.Bff.Application.Models.Internal;

namespace Kombats.Bff.Api.Mapping;

internal static class CharacterMapper
{
    // Fallback when Players returns a payload without avatar (rollout / mixed-version).
    private const string DefaultAvatarId = "default";

    public static CharacterResponse ToCharacterResponse(InternalCharacterResponse c) => new(
        CharacterId: c.CharacterId,
        OnboardingState: OnboardingStateMapper.ToDisplayString(c.OnboardingState),
        Name: c.Name,
        Strength: c.Strength,
        Agility: c.Agility,
        Intuition: c.Intuition,
        Vitality: c.Vitality,
        UnspentPoints: c.UnspentPoints,
        Revision: c.Revision,
        TotalXp: c.TotalXp,
        Level: c.Level,
        AvatarId: c.AvatarId ?? DefaultAvatarId);

    public static OnboardResponse ToOnboardResponse(InternalCharacterResponse c) => new(
        CharacterId: c.CharacterId,
        OnboardingState: OnboardingStateMapper.ToDisplayString(c.OnboardingState),
        Name: c.Name,
        Strength: c.Strength,
        Agility: c.Agility,
        Intuition: c.Intuition,
        Vitality: c.Vitality,
        UnspentPoints: c.UnspentPoints,
        Revision: c.Revision,
        TotalXp: c.TotalXp,
        Level: c.Level,
        AvatarId: c.AvatarId ?? DefaultAvatarId);
}
