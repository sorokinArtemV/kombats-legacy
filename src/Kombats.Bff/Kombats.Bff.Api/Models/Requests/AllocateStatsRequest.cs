namespace Kombats.Bff.Api.Models.Requests;

public sealed record AllocateStatsRequest(
    int ExpectedRevision,
    int Strength,
    int Agility,
    int Intuition,
    int Vitality);
