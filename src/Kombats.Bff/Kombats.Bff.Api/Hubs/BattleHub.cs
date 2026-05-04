using Kombats.Bff.Application.Relay;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Kombats.Bff.Api.Hubs;

/// <summary>
/// Frontend-facing SignalR hub for battle realtime.
/// Thin relay: all calls are forwarded to Battle's /battlehub via IBattleHubRelay.
/// No domain logic, no event interpretation, no buffering.
///
/// Downstream event relay targets the frontend by connection ID via IFrontendBattleSender
/// (backed by IHubContext&lt;BattleHub&gt;), not via captured Hub.Clients.Caller.
/// Hub.Clients/Context/Groups must not be stored for use outside hub method scope.
/// </summary>
[Authorize]
public sealed class BattleHub(
    IBattleHubRelay relay,
    ILogger<BattleHub> logger) : Hub
{
    public async Task<object> JoinBattle(Guid battleId)
    {
        string accessToken = GetAccessToken();
        string connectionId = Context.ConnectionId;

        logger.LogInformation(
            "Frontend {ConnectionId} joining battle {BattleId}",
            connectionId, battleId);

        object snapshot = await relay.JoinBattleAsync(
            battleId,
            connectionId,
            accessToken,
            Context.ConnectionAborted);

        return snapshot;
    }

    public async Task SubmitTurnAction(Guid battleId, int turnIndex, string actionPayload)
    {
        logger.LogInformation(
            "Frontend {ConnectionId} submitting action for battle {BattleId} turn {TurnIndex}",
            Context.ConnectionId, battleId, turnIndex);

        await relay.SubmitTurnActionAsync(
            Context.ConnectionId,
            battleId,
            turnIndex,
            actionPayload,
            Context.ConnectionAborted);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        logger.LogInformation(
            "Frontend {ConnectionId} disconnected. Exception: {Error}",
            Context.ConnectionId, exception?.Message);

        await relay.DisconnectAsync(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    private string GetAccessToken()
    {
        // Extract the JWT from the hub context for forwarding to Battle
        HttpContext? httpContext = Context.GetHttpContext();
        string? authHeader = httpContext?.Request.Headers["Authorization"].ToString();

        if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return authHeader["Bearer ".Length..].Trim();
        }

        // For WebSocket connections, the token may be in the query string
        string? queryToken = httpContext?.Request.Query["access_token"].ToString();
        if (!string.IsNullOrEmpty(queryToken))
        {
            return queryToken;
        }

        throw new HubException("No access token available for downstream authentication.");
    }
}
