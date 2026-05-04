namespace Kombats.Players.Application.UseCases.AllocateStatPoints;

public sealed record AllocateStatPointsResult(
    int Strength,
    int Agility,
    int Intuition,
    int Vitality,
    int UnspentPoints,
    int Revision);

