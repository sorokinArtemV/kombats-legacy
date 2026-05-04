namespace Kombats.Bff.Application.Relay;

/// <summary>
/// Sends SignalR events to a specific frontend chat connection by connection ID.
/// Implemented via IHubContext&lt;ChatHub&gt; which is stable outside hub method scope,
/// unlike Hub.Clients.Caller which must not be captured in long-lived callbacks.
/// </summary>
public interface IFrontendChatSender
{
    Task SendAsync(string connectionId, string eventName, object?[] args, CancellationToken cancellationToken = default);
}
