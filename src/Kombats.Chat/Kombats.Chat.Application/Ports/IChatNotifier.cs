using Kombats.Chat.Application.Notifications;

namespace Kombats.Chat.Application.Ports;

/// <summary>
/// Application-side abstraction over the chat real-time transport (SignalR in v1).
/// Implemented in the Api project against <c>IHubContext&lt;InternalChatHub&gt;</c>.
/// </summary>
internal interface IChatNotifier
{
    Task BroadcastGlobalMessageAsync(GlobalMessageEvent message, CancellationToken ct);

    Task SendDirectMessageAsync(Guid recipientIdentityId, DirectMessageEvent message, CancellationToken ct);

    Task BroadcastPlayerOnlineAsync(PlayerOnlineEvent evt, CancellationToken ct);

    Task BroadcastPlayerOfflineAsync(PlayerOfflineEvent evt, CancellationToken ct);
}
