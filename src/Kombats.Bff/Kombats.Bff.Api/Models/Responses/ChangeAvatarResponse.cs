namespace Kombats.Bff.Api.Models.Responses;

public sealed record ChangeAvatarResponse(
    string AvatarId,
    int Revision);
