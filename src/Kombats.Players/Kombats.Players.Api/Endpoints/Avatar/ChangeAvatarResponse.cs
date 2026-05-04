namespace Kombats.Players.Api.Endpoints.Avatar;

/// <summary>
/// Response DTO after successful avatar change.
/// </summary>
/// <param name="AvatarId">Updated avatar identifier.</param>
/// <param name="Revision">Updated character revision (use as ExpectedRevision in subsequent updates).</param>
public record ChangeAvatarResponse(string AvatarId, int Revision);
