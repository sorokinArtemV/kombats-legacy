using Kombats.Abstractions;

namespace Kombats.Chat.Application.UseCases.SendGlobalMessage;

internal sealed record SendGlobalMessageCommand(Guid SenderIdentityId, string Content) : ICommand;
