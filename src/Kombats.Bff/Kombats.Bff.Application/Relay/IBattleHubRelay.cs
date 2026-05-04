namespace Kombats.Bff.Application.Relay;

/// <summary>
/// Manages per-user SignalR client connections from BFF to Battle's /battlehub.
/// Creates a downstream connection on JoinBattle, relays events to the frontend
/// via IFrontendBattleSender (stable IHubContext-based targeting),
/// and cleans up on battle end or frontend disconnect.
/// </summary>
public interface IBattleHubRelay
{
    /// <summary>
    /// Creates a SignalR client connection to Battle's /battlehub for the given user,
    /// calls JoinBattle on the downstream hub, subscribes to server-to-client events,
    /// and returns the battle snapshot. Events are relayed to the frontend via
    /// IFrontendBattleSender using the stored frontendConnectionId.
    /// </summary>
    Task<object> JoinBattleAsync(
        Guid battleId,
        string frontendConnectionId,
        string accessToken,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Forwards a SubmitTurnAction call from the frontend to the downstream Battle hub.
    /// </summary>
    Task SubmitTurnActionAsync(
        string frontendConnectionId,
        Guid battleId,
        int turnIndex,
        string actionPayload,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cleans up the downstream connection for a frontend connection.
    /// Called on frontend disconnect or battle end.
    /// </summary>
    Task DisconnectAsync(string frontendConnectionId);
}
