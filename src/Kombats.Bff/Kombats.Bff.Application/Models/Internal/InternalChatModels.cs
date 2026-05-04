namespace Kombats.Bff.Application.Models.Internal;

// Duplicated DTOs mirroring the frozen Batch 3 Chat internal HTTP/hub response shapes.
// Per the approved decision (no shared DTO package), the BFF redeclares these types.
// Field names match the Chat service's JSON contract verbatim.

public sealed record InternalChatSenderDto(
    Guid PlayerId,
    string DisplayName);

public sealed record InternalChatMessageDto(
    Guid MessageId,
    Guid ConversationId,
    InternalChatSenderDto Sender,
    string Content,
    DateTimeOffset SentAt);

public sealed record InternalOtherPlayerDto(
    Guid PlayerId,
    string DisplayName);

public sealed record InternalConversationDto(
    Guid ConversationId,
    string Type,
    InternalOtherPlayerDto? OtherPlayer,
    DateTimeOffset? LastMessageAt);

public sealed record InternalConversationListResponse(
    List<InternalConversationDto> Conversations);

public sealed record InternalMessageListResponse(
    List<InternalChatMessageDto> Messages,
    bool HasMore);

public sealed record InternalOnlinePlayerDto(
    Guid PlayerId,
    string DisplayName);

public sealed record InternalOnlinePlayersResponse(
    List<InternalOnlinePlayerDto> Players,
    long TotalOnline);

public sealed record InternalPlayerProfileResponse(
    Guid PlayerId,
    string? DisplayName,
    int Level,
    int Strength,
    int Agility,
    int Intuition,
    int Vitality,
    int Wins,
    int Losses,
    // Nullable to tolerate pre-feature Players payloads during rollout;
    // BFF mappers coalesce to the default avatar id.
    string? AvatarId);
