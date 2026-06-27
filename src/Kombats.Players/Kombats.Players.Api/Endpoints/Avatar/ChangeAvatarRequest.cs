namespace Kombats.Players.Api.Endpoints.Avatar;

/// <summary>
/// Request DTO for changing the character avatar.
/// </summary>
/// <param name="ExpectedRevision">Expected character revision for optimistic concurrency control.</param>
/// <param name="AvatarId">Avatar identifier from the backend-controlled catalog.</param>
public record ChangeAvatarRequest(int ExpectedRevision, string AvatarId);
