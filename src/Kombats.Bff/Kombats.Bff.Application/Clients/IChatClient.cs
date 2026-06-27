using Kombats.Bff.Application.Models.Internal;

namespace Kombats.Bff.Application.Clients;

/// <summary>
/// Typed HTTP client over the Chat service's internal HTTP surface.
/// JWT is forwarded by <see cref="JwtForwardingHandler"/>.
/// </summary>
public interface IChatClient
{
    Task<InternalConversationListResponse?> GetConversationsAsync(
        CancellationToken cancellationToken = default);

    Task<InternalMessageListResponse?> GetMessagesAsync(
        Guid conversationId,
        DateTimeOffset? before,
        int limit,
        CancellationToken cancellationToken = default);

    Task<InternalMessageListResponse?> GetDirectMessagesAsync(
        Guid otherIdentityId,
        DateTimeOffset? before,
        int limit,
        CancellationToken cancellationToken = default);

    Task<InternalOnlinePlayersResponse?> GetOnlinePlayersAsync(
        int limit,
        int offset,
        CancellationToken cancellationToken = default);
}
