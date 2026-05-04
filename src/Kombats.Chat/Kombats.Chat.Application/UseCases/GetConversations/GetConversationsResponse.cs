namespace Kombats.Chat.Application.UseCases.GetConversations;

public sealed record GetConversationsResponse(
    List<ConversationDto> Conversations);

public sealed record ConversationDto(
    Guid ConversationId,
    string Type,
    OtherPlayerDto? OtherPlayer,
    DateTimeOffset? LastMessageAt);

/// <summary>
/// For DM conversations, the other participant's info.
/// Display name is resolved via IDisplayNameResolver (cache → Players HTTP → "Unknown").
/// </summary>
public sealed record OtherPlayerDto(
    Guid PlayerId,
    string DisplayName);
