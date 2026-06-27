using Kombats.Chat.Application.Notifications;
using Kombats.Chat.Application.Ports;
using Microsoft.AspNetCore.SignalR;

namespace Kombats.Chat.Api.Hubs;

internal sealed class SignalRChatNotifier(IHubContext<InternalChatHub> hub) : IChatNotifier
{
    public Task BroadcastGlobalMessageAsync(GlobalMessageEvent message, CancellationToken ct) =>
        hub.Clients.Group(ChatGroups.Global).SendAsync(ChatHubEvents.GlobalMessageReceived, message, ct);

    public Task SendDirectMessageAsync(Guid recipientIdentityId, DirectMessageEvent message, CancellationToken ct) =>
        hub.Clients.Group(ChatGroups.ForIdentity(recipientIdentityId))
            .SendAsync(ChatHubEvents.DirectMessageReceived, message, ct);

    public Task BroadcastPlayerOnlineAsync(PlayerOnlineEvent evt, CancellationToken ct) =>
        hub.Clients.Group(ChatGroups.Global).SendAsync(ChatHubEvents.PlayerOnline, evt, ct);

    public Task BroadcastPlayerOfflineAsync(PlayerOfflineEvent evt, CancellationToken ct) =>
        hub.Clients.Group(ChatGroups.Global).SendAsync(ChatHubEvents.PlayerOffline, evt, ct);
}
