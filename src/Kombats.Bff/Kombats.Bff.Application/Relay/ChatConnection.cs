using Microsoft.AspNetCore.SignalR.Client;

namespace Kombats.Bff.Application.Relay;

/// <summary>
/// Wraps a downstream chat HubConnection along with its frontend connection id.
/// <see cref="IntentionalClose"/> is flipped to <c>true</c> before any local
/// shutdown path (DisconnectAsync, DisposeAsync, forced reconnect, timeout
/// teardown) so the <c>HubConnection.Closed</c> handler can suppress the
/// <c>ChatConnectionLost</c> notification on those paths.
/// </summary>
internal sealed class ChatConnection
{
    public string FrontendConnectionId { get; }
    public HubConnection Hub { get; }
    public bool IntentionalClose { get; set; }

    public ChatConnection(string frontendConnectionId, HubConnection hub)
    {
        FrontendConnectionId = frontendConnectionId;
        Hub = hub;
    }
}
