using Kombats.Abstractions;

namespace Kombats.Chat.Application.UseCases.SendDirectMessage;

internal sealed record SendDirectMessageCommand(
    Guid SenderIdentityId,
    Guid RecipientIdentityId,
    string Content) : ICommand<SendDirectMessageResponse>;
