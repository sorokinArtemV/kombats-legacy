namespace Kombats.Players.Application.UseCases.ChangeAvatar;

public sealed record ChangeAvatarResult(
    string AvatarId,
    int Revision);
