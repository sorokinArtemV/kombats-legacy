namespace Kombats.Bff.Application.Relay;

/// <summary>
/// Sends SignalR events to a specific frontend connection by connection ID.
/// Implemented via IHubContext&lt;BattleHub&gt; which is stable outside hub method scope.
/// This avoids capturing Hub.Clients.Caller in long-lived callbacks.
/// </summary>
public interface IFrontendBattleSender
{
    Task SendAsync(string connectionId, string eventName, object?[] args, CancellationToken cancellationToken = default);
}
