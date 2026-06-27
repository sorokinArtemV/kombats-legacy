using Kombats.Players.Application;
using Kombats.Players.Domain.Entities;

namespace Kombats.Players.Api.Endpoints.Me;

/// <summary>
/// Character-centric me/onboarding response. Same shape for GET /api/me, POST /api/me/ensure, and POST /api/character/name.
/// </summary>
internal sealed record MeResponse(
    Guid CharacterId,
    Guid IdentityId,
    OnboardingState OnboardingState,
    string? Name,
    int Strength,
    int Agility,
    int Intuition,
    int Vitality,
    int UnspentPoints,
    int Revision,
    long TotalXp,
    int Level,
    int LevelingVersion,
    string AvatarId)
{
    public static MeResponse FromCharacterState(CharacterStateResult r) => new(
        CharacterId: r.CharacterId,
        IdentityId: r.IdentityId,
        OnboardingState: r.State,
        Name: r.Name,
        Strength: r.Strength,
        Agility: r.Agility,
        Intuition: r.Intuition,
        Vitality: r.Vitality,
        UnspentPoints: r.UnspentPoints,
        Revision: r.Revision,
        TotalXp: r.TotalXp,
        Level: r.Level,
        LevelingVersion: r.LevelingVersion,
        AvatarId: r.AvatarId);
}
