namespace Kombats.Bff.Api.Models.Responses;

public sealed record CharacterResponse(
    Guid CharacterId,
    string OnboardingState,
    string? Name,
    int Strength,
    int Agility,
    int Intuition,
    int Vitality,
    int UnspentPoints,
    int Revision,
    long TotalXp,
    int Level,
    string AvatarId);
