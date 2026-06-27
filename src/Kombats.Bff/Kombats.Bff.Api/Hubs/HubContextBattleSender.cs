using Kombats.Bff.Application.Relay;
using Microsoft.AspNetCore.SignalR;

namespace Kombats.Bff.Api.Hubs;

/// <summary>
/// Sends events to a frontend SignalR connection via IHubContext&lt;BattleHub&gt;.
/// IHubContext is stable and safe to use outside hub method invocation scope,
/// unlike Hub.Clients.Caller which must not be captured in long-lived callbacks.
/// </summary>
public sealed class HubContextBattleSender(IHubContext<BattleHub> hubContext) : IFrontendBattleSender
{
    public Task SendAsync(string connectionId, string eventName, object?[] args, CancellationToken cancellationToken = default)
    {
        return hubContext.Clients.Client(connectionId).SendCoreAsync(eventName, args, cancellationToken);
    }
}
