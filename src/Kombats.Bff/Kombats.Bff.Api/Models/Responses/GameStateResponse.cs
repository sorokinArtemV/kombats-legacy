namespace Kombats.Bff.Api.Models.Responses;

public sealed record GameStateResponse(
    CharacterResponse? Character,
    QueueStatusResponse? QueueStatus,
    bool IsCharacterCreated,
    IReadOnlyList<string>? DegradedServices);
