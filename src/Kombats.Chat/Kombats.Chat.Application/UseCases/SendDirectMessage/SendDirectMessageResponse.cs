namespace Kombats.Chat.Application.UseCases.SendDirectMessage;

/// <summary>
/// Frozen Batch 3 response for the SignalR <c>SendDirectMessage</c> hub method.
/// </summary>
public sealed record SendDirectMessageResponse(
    Guid ConversationId,
    Guid MessageId,
    DateTimeOffset SentAt);
