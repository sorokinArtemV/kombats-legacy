using Kombats.Abstractions;

namespace Kombats.Chat.Application.UseCases.DisconnectUser;

internal sealed record DisconnectUserCommand(Guid IdentityId) : ICommand;
