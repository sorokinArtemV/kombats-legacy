namespace Kombats.Bff.Application.Relay;

/// <summary>
/// Manages per-frontend-connection SignalR client connections from BFF to Chat's
/// <c>/chathub-internal</c>. Forwards client-to-server hub method invocations,
/// relays server-to-client events to the frontend connection via
/// <see cref="IFrontendChatSender"/>, and cleans up on frontend disconnect or
/// downstream failure.
/// </summary>
public interface IChatHubRelay
{
    /// <summary>
    /// Creates the downstream connection to Chat's <c>/chathub-internal</c> for the
    /// given frontend connection. Subscribes to all server-to-client events and
    /// forwards them to the frontend via <see cref="IFrontendChatSender"/>.
    /// Throws on connection failure (the dictionary is cleaned up before throwing).
    /// </summary>
    Task ConnectAsync(string frontendConnectionId, string accessToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Forwards <c>JoinGlobalChat</c> to the downstream chat hub. Returns the
    /// frozen <c>JoinGlobalChatResponse</c> shape (boxed as <see cref="object"/> —
    /// the BFF does not reference Chat application types).
    /// </summary>
    Task<object?> JoinGlobalChatAsync(string frontendConnectionId, CancellationToken cancellationToken = default);

    /// <summary>Forwards <c>LeaveGlobalChat</c> to the downstream chat hub.</summary>
    Task LeaveGlobalChatAsync(string frontendConnectionId, CancellationToken cancellationToken = default);

    /// <summary>Forwards <c>SendGlobalMessage</c> to the downstream chat hub.</summary>
    Task SendGlobalMessageAsync(string frontendConnectionId, string content, CancellationToken cancellationToken = default);

    /// <summary>
    /// Forwards <c>SendDirectMessage</c> to the downstream chat hub. Returns the
    /// frozen <c>SendDirectMessageResponse</c> shape boxed as <see cref="object"/>.
    /// </summary>
    Task<object?> SendDirectMessageAsync(string frontendConnectionId, Guid recipientPlayerId, string content, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disposes the downstream connection for a frontend connection.
    /// Called on frontend disconnect. Idempotent — safe to call when no connection exists.
    /// </summary>
    Task DisconnectAsync(string frontendConnectionId);
}
