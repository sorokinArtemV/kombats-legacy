using Kombats.Chat.Application.UseCases.GetConversationMessages;
using Kombats.Chat.Application.UseCases.GetOnlinePlayers;

namespace Kombats.Chat.Application.UseCases.JoinGlobalChat;

/// <summary>
/// Frozen Batch 3 response shape for the SignalR <c>JoinGlobalChat</c> hub method.
/// Reuses <see cref="MessageDto"/>/<see cref="SenderDto"/>/<see cref="OnlinePlayerDto"/>
/// from the read-path query responses (per the duplicated-DTO, no-shared-DTO-package decision).
/// </summary>
public sealed record JoinGlobalChatResponse(
    Guid ConversationId,
    IReadOnlyList<MessageDto> RecentMessages,
    IReadOnlyList<OnlinePlayerDto> OnlinePlayers,
    long TotalOnline);
