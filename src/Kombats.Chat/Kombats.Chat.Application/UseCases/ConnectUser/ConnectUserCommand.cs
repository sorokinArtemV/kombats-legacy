using Kombats.Abstractions;

namespace Kombats.Chat.Application.UseCases.ConnectUser;

internal sealed record ConnectUserCommand(Guid IdentityId) : ICommand;
