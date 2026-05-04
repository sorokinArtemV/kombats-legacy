using Kombats.Players.Domain.Entities;

namespace Kombats.Players.Application;

/// <summary>
/// Snapshot of character state returned by provisioning and naming use cases.
/// </summary>
public sealed record CharacterStateResult(
    Guid CharacterId,
    Guid IdentityId,
    OnboardingState State,
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
    int Wins,
    int Losses,
    string AvatarId)
{
    public static CharacterStateResult FromCharacter(Character c) => new(
        CharacterId: c.Id,
        IdentityId: c.IdentityId,
        State: c.OnboardingState,
        Name: c.Name,
        Strength: c.Strength,
        Agility: c.Agility,
        Intuition: c.Intuition,
        Vitality: c.Vitality,
        UnspentPoints: c.UnspentPoints,
        Revision: c.Revision,
        TotalXp: c.TotalXp,
        Level: c.Level,
        LevelingVersion: c.LevelingVersion,
        Wins: c.Wins,
        Losses: c.Losses,
        AvatarId: c.AvatarId);
}
