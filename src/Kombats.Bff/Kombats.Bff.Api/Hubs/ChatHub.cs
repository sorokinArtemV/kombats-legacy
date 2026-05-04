using Kombats.Bff.Application.Relay;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Kombats.Bff.Api.Hubs;

/// <summary>
/// Frontend-facing SignalR chat hub at <c>/chathub</c>. Thin relay over the
/// frozen Batch 3 Chat internal hub contract — every method is forwarded to
/// <see cref="IChatHubRelay"/> which proxies to Chat's <c>/chathub-internal</c>.
/// No domain logic, no event interpretation, no buffering.
/// </summary>
[Authorize]
public sealed class ChatHub(
    IChatHubRelay relay,
    ILogger<ChatHub> logger) : Hub
{
    public override async Task OnConnectedAsync()
    {
        string connectionId = Context.ConnectionId;
        string accessToken = GetAccessToken();

        logger.LogInformation("Frontend chat connection {ConnectionId} opening downstream relay", connectionId);

        try
        {
            await relay.ConnectAsync(connectionId, accessToken, Context.ConnectionAborted);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Failed to open downstream chat relay for frontend {ConnectionId}; aborting",
                connectionId);
            Context.Abort();
            return;
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        logger.LogInformation(
            "Frontend chat connection {ConnectionId} disconnected. Exception: {Error}",
            Context.ConnectionId, exception?.Message);

        await relay.DisconnectAsync(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    public Task<object?> JoinGlobalChat() =>
        relay.JoinGlobalChatAsync(Context.ConnectionId, Context.ConnectionAborted);

    public Task LeaveGlobalChat() =>
        relay.LeaveGlobalChatAsync(Context.ConnectionId, Context.ConnectionAborted);

    public Task SendGlobalMessage(string content) =>
        relay.SendGlobalMessageAsync(Context.ConnectionId, content, Context.ConnectionAborted);

    public Task<object?> SendDirectMessage(Guid recipientPlayerId, string content) =>
        relay.SendDirectMessageAsync(Context.ConnectionId, recipientPlayerId, content, Context.ConnectionAborted);

    private string GetAccessToken()
    {
        HttpContext? httpContext = Context.GetHttpContext();
        string? authHeader = httpContext?.Request.Headers["Authorization"].ToString();

        if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return authHeader["Bearer ".Length..].Trim();
        }

        // For WebSocket connections the token is in the query string (see Bootstrap JwtBearer events).
        string? queryToken = httpContext?.Request.Query["access_token"].ToString();
        if (!string.IsNullOrEmpty(queryToken))
        {
            return queryToken;
        }

        throw new HubException("No access token available for downstream chat authentication.");
    }
}
