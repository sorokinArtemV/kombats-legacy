using Kombats.Chat.Application.UseCases.GetConversationMessages;

namespace Kombats.Chat.Application.Notifications;

/// <summary>
/// Frozen Batch 3 server-to-client chat events. These records define the wire shape
/// of the SignalR events <c>GlobalMessageReceived</c>, <c>DirectMessageReceived</c>,
/// <c>PlayerOnline</c>, and <c>PlayerOffline</c>. Batch 5 (BFF) consumes these verbatim.
/// </summary>
public sealed record GlobalMessageEvent(
    Guid MessageId,
    SenderDto Sender,
    string Content,
    DateTimeOffset SentAt);

public sealed record DirectMessageEvent(
    Guid MessageId,
    Guid ConversationId,
    SenderDto Sender,
    string Content,
    DateTimeOffset SentAt);

public sealed record PlayerOnlineEvent(Guid PlayerId, string DisplayName);

public sealed record PlayerOfflineEvent(Guid PlayerId);

/// <summary>
/// Payload of the <c>ChatError</c> SignalR event sent to the caller only.
/// </summary>
public sealed record ChatErrorEvent(string Code, string Message, long? RetryAfterMs);
