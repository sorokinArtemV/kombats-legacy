using Kombats.Bff.Api.Models.Responses;
using Kombats.Bff.Application.Models.Internal;

namespace Kombats.Bff.Api.Mapping;

internal static class ChatMapper
{
    // Fallback when Players returns a profile payload without avatar (rollout / mixed-version).
    private const string DefaultAvatarId = "default";

    public static ConversationListResponse Map(InternalConversationListResponse src) =>
        new(src.Conversations.Select(Map).ToArray());

    public static MessageListResponse Map(InternalMessageListResponse src) =>
        new(src.Messages.Select(Map).ToArray(), src.HasMore);

    public static OnlinePlayersResponse Map(InternalOnlinePlayersResponse src) =>
        new(src.Players.Select(p => new OnlinePlayerResponse(p.PlayerId, p.DisplayName)).ToArray(), src.TotalOnline);

    public static PlayerCardResponse MapCard(InternalPlayerProfileResponse src) =>
        new(
            PlayerId: src.PlayerId,
            DisplayName: src.DisplayName ?? "Unknown",
            Level: src.Level,
            Strength: src.Strength,
            Agility: src.Agility,
            Intuition: src.Intuition,
            Vitality: src.Vitality,
            Wins: src.Wins,
            Losses: src.Losses,
            AvatarId: src.AvatarId ?? DefaultAvatarId);

    private static ChatConversationResponse Map(InternalConversationDto src) =>
        new(
            src.ConversationId,
            src.Type,
            src.OtherPlayer is null ? null : new ChatOtherPlayerResponse(src.OtherPlayer.PlayerId, src.OtherPlayer.DisplayName),
            src.LastMessageAt);

    private static ChatMessageResponse Map(InternalChatMessageDto src) =>
        new(
            src.MessageId,
            src.ConversationId,
            new ChatSenderResponse(src.Sender.PlayerId, src.Sender.DisplayName),
            src.Content,
            src.SentAt);
}
