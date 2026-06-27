namespace Kombats.Bff.Api.Models.Responses;

// Frontend-facing chat response DTOs, mirroring the Chat internal HTTP shapes 1:1
// per the architecture spec section 11. Names use camelCase on the wire (default JSON config).

public sealed record ChatSenderResponse(
    Guid PlayerId,
    string DisplayName);

public sealed record ChatMessageResponse(
    Guid MessageId,
    Guid ConversationId,
    ChatSenderResponse Sender,
    string Content,
    DateTimeOffset SentAt);

public sealed record ChatOtherPlayerResponse(
    Guid PlayerId,
    string DisplayName);

public sealed record ChatConversationResponse(
    Guid ConversationId,
    string Type,
    ChatOtherPlayerResponse? OtherPlayer,
    DateTimeOffset? LastMessageAt);

public sealed record ConversationListResponse(
    IReadOnlyList<ChatConversationResponse> Conversations);

public sealed record MessageListResponse(
    IReadOnlyList<ChatMessageResponse> Messages,
    bool HasMore);

public sealed record OnlinePlayerResponse(
    Guid PlayerId,
    string DisplayName);

public sealed record OnlinePlayersResponse(
    IReadOnlyList<OnlinePlayerResponse> Players,
    long TotalOnline);

public sealed record PlayerCardResponse(
    Guid PlayerId,
    string DisplayName,
    int Level,
    int Strength,
    int Agility,
    int Intuition,
    int Vitality,
    int Wins,
    int Losses,
    string AvatarId);
