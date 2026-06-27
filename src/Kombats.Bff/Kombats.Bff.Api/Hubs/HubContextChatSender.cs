using Kombats.Bff.Application.Relay;
using Microsoft.AspNetCore.SignalR;

namespace Kombats.Bff.Api.Hubs;

/// <summary>
/// Sends events to a frontend SignalR connection via IHubContext&lt;ChatHub&gt;.
/// IHubContext is stable outside hub method invocation scope, unlike Hub.Clients.Caller
/// which must not be captured in long-lived callbacks.
/// </summary>
internal sealed class HubContextChatSender(IHubContext<ChatHub> hubContext) : IFrontendChatSender
{
    public Task SendAsync(string connectionId, string eventName, object?[] args, CancellationToken cancellationToken = default)
    {
        return hubContext.Clients.Client(connectionId).SendCoreAsync(eventName, args, cancellationToken);
    }
}
