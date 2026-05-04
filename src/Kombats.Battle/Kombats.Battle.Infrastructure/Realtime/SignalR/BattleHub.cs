using Kombats.Battle.Application.UseCases.Lifecycle;
using Kombats.Battle.Application.UseCases.Turns;
using Kombats.Battle.Realtime.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using System.Security.Claims;

namespace Kombats.Battle.Infrastructure.Realtime.SignalR;

/// <summary>
/// SignalR hub for battle operations.
/// Thin transport adapter that delegates to Application services.
/// </summary>
[Authorize]
public class BattleHub : Hub
{
    private readonly BattleLifecycleAppService _lifecycleService;
    private readonly BattleTurnAppService _turnAppService;
    private readonly ILogger<BattleHub> _logger;

    public BattleHub(
        BattleLifecycleAppService lifecycleService,
        BattleTurnAppService turnAppService,
        ILogger<BattleHub> logger)
    {
        _lifecycleService = lifecycleService;
        _turnAppService = turnAppService;
        _logger = logger;
    }

    public async Task<BattleSnapshotRealtime> JoinBattle(Guid battleId)
    {
        var userId = GetAuthenticatedUserId();

        _logger.LogInformation("User {UserId} joining battle {BattleId}, ConnectionId: {ConnectionId}", userId, battleId, Context.ConnectionId);

        await Groups.AddToGroupAsync(Context.ConnectionId, $"battle:{battleId}");

        try
        {
            var state = await _lifecycleService.GetBattleSnapshotForPlayerAsync(battleId, userId);
            if (state is null)
            {
                throw new HubException($"Battle {battleId} not found");
            }

            return RealtimeContractMapper.ToRealtimeSnapshot(state);
        }
        catch (InvalidOperationException ex)
        {
            throw new HubException(ex.Message);
        }
    }

    public async Task SubmitTurnAction(Guid battleId, int turnIndex, string actionPayload)
    {
        var userId = GetAuthenticatedUserId();

        _logger.LogInformation("User {UserId} submitting action for BattleId: {BattleId}, TurnIndex: {TurnIndex}, ConnectionId: {ConnectionId}", userId, battleId, turnIndex, Context.ConnectionId);

        try
        {
            await _turnAppService.SubmitActionAsync(battleId, userId, turnIndex, actionPayload);
        }
        catch (InvalidOperationException ex)
        {
            throw new HubException(ex.Message);
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Client disconnected: ConnectionId: {ConnectionId}, Exception: {Exception}", Context.ConnectionId, exception?.Message);
        await base.OnDisconnectedAsync(exception);
    }

    private Guid GetAuthenticatedUserId()
    {
        var userIdClaim = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                         ?? Context.User?.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            _logger.LogWarning("Unauthenticated or invalid user attempting hub operation");
            throw new HubException("User not authenticated");
        }

        return userId;
    }
}
