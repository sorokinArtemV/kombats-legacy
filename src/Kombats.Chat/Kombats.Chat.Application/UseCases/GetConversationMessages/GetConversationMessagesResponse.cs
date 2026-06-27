namespace Kombats.Chat.Application.UseCases.GetConversationMessages;

public sealed record GetConversationMessagesResponse(
    List<MessageDto> Messages,
    bool HasMore);

public sealed record MessageDto(
    Guid MessageId,
    Guid ConversationId,
    SenderDto Sender,
    string Content,
    DateTimeOffset SentAt);

public sealed record SenderDto(
    Guid PlayerId,
    string DisplayName);
