namespace Kombats.Bff.Api.Models.Requests;

public sealed record ChangeAvatarRequest(int ExpectedRevision, string AvatarId);
