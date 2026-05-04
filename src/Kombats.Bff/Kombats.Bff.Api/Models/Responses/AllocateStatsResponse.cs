namespace Kombats.Bff.Api.Models.Responses;

public sealed record AllocateStatsResponse(
    int Strength,
    int Agility,
    int Intuition,
    int Vitality,
    int UnspentPoints,
    int Revision);
