using Kombats.Battle.Application.Ports;
using Kombats.Battle.Domain.Results;
using Kombats.Battle.Domain.Rules;
using Kombats.Battle.Infrastructure.Configuration;
using Kombats.Battle.Realtime.Contracts;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kombats.Battle.Infrastructure.Realtime.SignalR;

/// <summary>
/// SignalR implementation of IBattleRealtimeNotifier.
/// Maps Application parameters to typed SignalR contracts.
/// Uses IHubContext&lt;BattleHub&gt; to reference the hub type directly.
/// </summary>
internal sealed class SignalRBattleRealtimeNotifier : IBattleRealtimeNotifier
{
    private readonly IHubContext<BattleHub> _hubContext;
    private readonly ILogger<SignalRBattleRealtimeNotifier> _logger;
    private readonly BattleRewardsOptions _rewards;

    public SignalRBattleRealtimeNotifier(
        IHubContext<BattleHub> hubContext,
        ILogger<SignalRBattleRealtimeNotifier> logger,
        IOptions<BattleRewardsOptions> rewards)
    {
        _hubContext = hubContext;
        _logger = logger;
        _rewards = rewards.Value;
    }

    public async Task NotifyBattleReadyAsync(Guid battleId, Guid playerAId, Guid playerBId, string? playerAName, string? playerBName, CancellationToken cancellationToken = default)
    {
        var payload = new BattleReadyRealtime
        {
            BattleId = battleId,
            PlayerAId = playerAId,
            PlayerBId = playerBId,
            PlayerAName = playerAName,
            PlayerBName = playerBName
        };

        await _hubContext.Clients.Group($"battle:{battleId}").SendAsync(
            RealtimeEventNames.BattleReady,
            payload,
            cancellationToken);
    }

    public async Task NotifyTurnOpenedAsync(Guid battleId, int turnIndex, DateTimeOffset deadlineUtc, CancellationToken cancellationToken = default)
    {
        var payload = new TurnOpenedRealtime
        {
            BattleId = battleId,
            TurnIndex = turnIndex,
            DeadlineUtc = deadlineUtc
        };

        await _hubContext.Clients.Group($"battle:{battleId}").SendAsync(
            RealtimeEventNames.TurnOpened,
            payload,
            cancellationToken);
    }

    public async Task NotifyTurnResolvedAsync(Guid battleId, int turnIndex, string playerAAction, string playerBAction, TurnResolutionLog? log = null, CancellationToken cancellationToken = default)
    {
        var payload = new TurnResolvedRealtime
        {
            BattleId = battleId,
            TurnIndex = turnIndex,
            PlayerAAction = playerAAction,
            PlayerBAction = playerBAction,
            Log = log != null ? RealtimeContractMapper.ToRealtimeTurnResolutionLog(log) : null
        };

        await _hubContext.Clients.Group($"battle:{battleId}").SendAsync(
            RealtimeEventNames.TurnResolved,
            payload,
            cancellationToken);
    }

    public async Task NotifyPlayerDamagedAsync(Guid battleId, Guid playerId, int damage, int remainingHp, int turnIndex, CancellationToken cancellationToken = default)
    {
        var payload = new PlayerDamagedRealtime
        {
            BattleId = battleId,
            PlayerId = playerId,
            Damage = damage,
            RemainingHp = remainingHp,
            TurnIndex = turnIndex
        };

        await _hubContext.Clients.Group($"battle:{battleId}").SendAsync(
            RealtimeEventNames.PlayerDamaged,
            payload,
            cancellationToken);
    }

    public async Task NotifyBattleStateUpdatedAsync(
        Guid battleId,
        Guid playerAId,
        Guid playerBId,
        Ruleset ruleset,
        string phase,
        int turnIndex,
        DateTimeOffset deadlineUtc,
        int noActionStreakBoth,
        int lastResolvedTurnIndex,
        string? endedReason,
        int version,
        int? playerAHp,
        int? playerBHp,
        string? playerAName,
        string? playerBName,
        int? playerAMaxHp,
        int? playerBMaxHp,
        CancellationToken cancellationToken = default)
    {
        var payload = new BattleStateUpdatedRealtime
        {
            BattleId = battleId,
            PlayerAId = playerAId,
            PlayerBId = playerBId,
            Ruleset = RealtimeContractMapper.ToRealtimeRuleset(ruleset),
            Phase = RealtimeContractMapper.ToRealtimePhase(phase, _logger),
            TurnIndex = turnIndex,
            DeadlineUtc = deadlineUtc,
            NoActionStreakBoth = noActionStreakBoth,
            LastResolvedTurnIndex = lastResolvedTurnIndex,
            EndedReason = RealtimeContractMapper.ToRealtimeEndReason(endedReason, _logger),
            Version = version,
            PlayerAHp = playerAHp,
            PlayerBHp = playerBHp,
            PlayerAName = playerAName,
            PlayerBName = playerBName,
            PlayerAMaxHp = playerAMaxHp,
            PlayerBMaxHp = playerBMaxHp
        };

        await _hubContext.Clients.Group($"battle:{battleId}").SendAsync(
            RealtimeEventNames.BattleStateUpdated,
            payload,
            cancellationToken);
    }

    public async Task NotifyBattleEndedAsync(Guid battleId, string reason, Guid? winnerPlayerId, DateTimeOffset endedAt, CancellationToken cancellationToken = default)
    {
        var endReason = RealtimeContractMapper.ToRealtimeEndReason(reason, _logger)
                       ?? BattleEndReasonRealtime.Unknown;

        // XP is awarded iff there is a winner. This mirrors Players'
        // HandleBattleCompletedHandler, which gates XP on
        // WinnerIdentityId.HasValue && LoserIdentityId.HasValue — and Battle's
        // own MassTransitBattleEventPublisher derives LoserIdentityId from
        // winnerPlayerId.HasValue, so the two collapse to the same condition.
        // Reason is intentionally not consulted; future reasons inherit the rule.
        int? winnerXp = winnerPlayerId.HasValue ? _rewards.WinXp : (int?)null;
        int? loserXp = winnerPlayerId.HasValue ? _rewards.LossXp : (int?)null;

        var payload = new BattleEndedRealtime
        {
            BattleId = battleId,
            Reason = endReason,
            WinnerPlayerId = winnerPlayerId,
            EndedAt = endedAt,
            WinnerXp = winnerXp,
            LoserXp = loserXp
        };

        await _hubContext.Clients.Group($"battle:{battleId}").SendAsync(
            RealtimeEventNames.BattleEnded,
            payload,
            cancellationToken);
    }
}



