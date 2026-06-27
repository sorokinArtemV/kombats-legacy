namespace Kombats.Players.Api.Endpoints.AllocateStatPoints;

/// <summary>
/// Request DTO for allocating character stat points.
/// </summary>
/// <param name="ExpectedRevision">Expected character revision for optimistic concurrency control.</param>
/// <param name="Str">Points to allocate to strength.</param>
/// <param name="Agi">Points to allocate to agility.</param>
/// <param name="Intuition">Points to allocate to intuition.</param>
/// <param name="Vit">Points to allocate to vitality.</param>
public record AllocateStatPointsRequest(int ExpectedRevision, int Str, int Agi, int Intuition, int Vit);

