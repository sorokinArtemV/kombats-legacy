using Kombats.Abstractions;

namespace Kombats.Players.Application.UseCases.ChangeAvatar;

internal sealed record ChangeAvatarCommand(
    Guid IdentityId,
    int ExpectedRevision,
    string AvatarId) : ICommand<ChangeAvatarResult>;
