using Kombats.Battle.Domain.Engine;
using Kombats.Battle.Domain.Events;
using Kombats.Battle.Domain.Model;
using Kombats.Battle.Domain.Results;
using Kombats.Battle.Application.Models;
using Kombats.Battle.Application.Ports;
using Kombats.Battle.Application.Mapping;
using Kombats.Battle.Application.ReadModels;
using Microsoft.Extensions.Logging;

namespace Kombats.Battle.Application.UseCases.Turns;

/// <summary>
/// Application service for battle turn operations: submitting actions and resolving turns.
/// Orchestrates turn resolution with proper idempotency and state machine enforcement.
/// </summary>
public sealed class BattleTurnAppService
{
    private readonly IBattleStateStore _stateStore;
    private readonly IBattleEngine _battleEngine;
    private readonly IBattleRealtimeNotifier _notifier;
    private readonly IBattleEventPublisher _eventPublisher;
    private readonly IBattleUnitOfWork _unitOfWork;
    private readonly IActionIntake _actionIntake;
    private readonly IBattleTurnHistoryStore _turnHistoryStore;
    private readonly IClock _clock;
    private readonly ILogger<BattleTurnAppService> _logger;

    public BattleTurnAppService(
        IBattleStateStore stateStore,
        IBattleEngine battleEngine,
        IBattleRealtimeNotifier notifier,
        IBattleEventPublisher eventPublisher,
        IBattleUnitOfWork unitOfWork,
        IActionIntake actionIntake,
        IBattleTurnHistoryStore turnHistoryStore,
        IClock clock,
        ILogger<BattleTurnAppService> logger)
    {
        _stateStore = stateStore;
        _battleEngine = battleEngine;
        _notifier = notifier;
        _eventPublisher = eventPublisher;
        _unitOfWork = unitOfWork;
        _actionIntake = actionIntake;
        _turnHistoryStore = turnHistoryStore;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// Submits a player action for the current turn.
    /// Validates protocol (phase, turn index, deadline) and normalizes payload.
    /// If both players have submitted actions, triggers early resolution (best-effort).
    /// </summary>
    public async Task SubmitActionAsync(
        Guid battleId,
        Guid playerId,
        int clientTurnIndex,
        string? actionPayload,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "Submitting action for BattleId: {BattleId}, PlayerId: {PlayerId}, TurnIndex: {TurnIndex}",
            battleId, playerId, clientTurnIndex);

        // Load state
        var state = await _stateStore.GetStateAsync(battleId, cancellationToken);
        if (state == null)
        {
            _logger.LogWarning("Battle state not found for BattleId: {BattleId}", battleId);
            throw new InvalidOperationException($"Battle {battleId} not found");
        }

        // Verify player is a participant
        if (state.PlayerAId != playerId && state.PlayerBId != playerId)
        {
            _logger.LogWarning(
                "User {PlayerId} is not a participant in battle {BattleId}",
                playerId, battleId);
            throw new InvalidOperationException("User is not a participant in this battle");
        }

        // If battle is already ended, ignore gracefully — this is expected when a concurrent
        // submission triggers resolution and transitions Phase→Ended before this call loads state.
        // The client will learn about battle end via the BattleEnded SignalR notification.
        if (state.Phase == BattlePhase.Ended)
        {
            _logger.LogDebug(
                "Battle {BattleId} already ended, ignoring action from PlayerId: {PlayerId}",
                battleId, playerId);
            return;
        }

        // Process action through intake pipeline (parses JSON, validates protocol and semantics)
        // ActionIntakeService is the single source of truth for all protocol validation
        var canonicalAction = _actionIntake.ProcessAction(
            battleId,
            playerId,
            clientTurnIndex,
            actionPayload,
            state);

        // Store canonical action atomically and check if both players have submitted
        // This eliminates the extra GetActionsAsync roundtrip
        var storeAndCheckResult = await _stateStore.StoreActionAndCheckBothSubmittedAsync(
            battleId,
            state.TurnIndex,
            playerId,
            state.PlayerAId,
            state.PlayerBId,
            canonicalAction,
            cancellationToken);

        if (storeAndCheckResult.WasStored)
        {
            _logger.LogDebug(
                "Action stored for BattleId: {BattleId}, TurnIndex: {TurnIndex}, PlayerId: {PlayerId}, Quality: {Quality}, IsNoAction: {IsNoAction}, BothSubmitted: {BothSubmitted}",
                battleId, state.TurnIndex, playerId, canonicalAction.Quality, canonicalAction.IsNoAction, storeAndCheckResult.BothSubmitted);
        }
        else
        {
            _logger.LogDebug(
                "Action already submitted for BattleId: {BattleId}, TurnIndex: {TurnIndex}, PlayerId: {PlayerId}, BothSubmitted: {BothSubmitted}. Skipping duplicate submission.",
                battleId, state.TurnIndex, playerId, storeAndCheckResult.BothSubmitted);
        }

        // Early resolution optimization: if both players have actions, try to resolve immediately
        // This is best-effort; if CAS fails, deadline worker will handle it
        if (storeAndCheckResult.BothSubmitted)
        {
            try
            {
                // Both actions present - try early resolution
                // ResolveTurnAsync uses CAS, so it's safe to call even if another thread/worker is resolving
                await ResolveTurnAsync(battleId, cancellationToken);
            }
            catch (Exception ex)
            {
                // Don't fail action submission if early resolution fails
                _logger.LogDebug(ex,
                    "Early turn resolution failed for BattleId: {BattleId}, TurnIndex: {TurnIndex}. Deadline worker will handle it.",
                    battleId, state.TurnIndex);
            }
        }
    }

    /// <summary>
    /// Resolves a turn for a battle.
    /// Idempotent: safe to call multiple times (CAS ensures only one resolution succeeds).
    /// Also handles stuck-in-Resolving recovery: if a battle is already in Resolving phase
    /// (e.g., previous resolution attempt failed mid-way), re-attempts resolution directly.
    ///
    /// Flow:
    /// 1. Load and validate state (phase, turn index, idempotency)
    /// 2. CAS transition to Resolving phase (skipped if already Resolving)
    /// 3. Load actions, convert to domain, run engine
    /// 4. Dispatch result (battle ended or turn continues)
    /// </summary>
    public async Task<bool> ResolveTurnAsync(Guid battleId, CancellationToken cancellationToken = default)
    {
        // Phase 1: Load and validate state
        var (state, alreadyResolving) = await LoadAndValidateStateForResolution(battleId, cancellationToken);
        if (state == null)
            return false;

        var turnIndex = state.TurnIndex;

        // Phase 2: Atomic CAS transition to Resolving (skip if already in Resolving — recovery path)
        if (!alreadyResolving)
        {
            var markedResolving = await _stateStore.TryMarkTurnResolvingAsync(battleId, turnIndex, cancellationToken);
            if (!markedResolving)
            {
                _logger.LogWarning(
                    "Failed to mark turn {TurnIndex} as Resolving for BattleId: {BattleId}. May be duplicate or invalid state.",
                    turnIndex, battleId);
                return false;
            }
        }
        else
        {
            _logger.LogInformation(
                "Retrying resolution for BattleId: {BattleId} stuck in Resolving phase at TurnIndex: {TurnIndex}",
                battleId, turnIndex);
        }

        // Phase 3: Load actions and run domain engine
        var resolutionResult = await LoadActionsAndResolve(battleId, turnIndex, state, cancellationToken);
        if (resolutionResult == null)
            return false;

        // Phase 4: Dispatch result based on domain events
        return await DispatchResolutionResult(battleId, turnIndex, state, resolutionResult, cancellationToken);
    }

    /// <summary>
    /// Loads battle state and validates preconditions for turn resolution.
    /// Returns (null, false) if resolution should not proceed (idempotent/invalid state).
    /// Returns (state, true) if the battle is stuck in Resolving and should be retried.
    /// Returns (state, false) if the battle is in TurnOpen and ready for normal resolution.
    /// </summary>
    private async Task<(BattleSnapshot? State, bool AlreadyResolving)> LoadAndValidateStateForResolution(
        Guid battleId,
        CancellationToken cancellationToken)
    {
        var state = await _stateStore.GetStateAsync(battleId, cancellationToken);
        if (state == null)
        {
            _logger.LogWarning("Battle state not found for BattleId: {BattleId}", battleId);
            return (null, false);
        }

        var turnIndex = state.TurnIndex;

        // Idempotency check: if turn already resolved, return
        if (turnIndex <= state.LastResolvedTurnIndex)
        {
            _logger.LogInformation(
                "Turn {TurnIndex} already resolved (LastResolvedTurnIndex: {LastResolvedTurnIndex}) for BattleId: {BattleId}",
                turnIndex, state.LastResolvedTurnIndex, battleId);
            return (null, false);
        }

        if (state.Phase == BattlePhase.Ended)
        {
            _logger.LogInformation(
                "Battle {BattleId} already ended, ignoring ResolveTurn for TurnIndex: {TurnIndex}",
                battleId, turnIndex);
            return (null, false);
        }

        // Stuck-in-Resolving recovery: if battle is in Resolving phase, allow retry
        if (state.Phase == BattlePhase.Resolving && state.TurnIndex == turnIndex)
        {
            return (state, true);
        }

        // Normal path: must be TurnOpen
        if (state.Phase != BattlePhase.TurnOpen)
        {
            _logger.LogError(
                "Invalid state for ResolveTurn: BattleId: {BattleId}, TurnIndex: {TurnIndex}, State.Phase: {Phase}, State.TurnIndex: {StateTurnIndex}",
                battleId, turnIndex, state.Phase, state.TurnIndex);
            return (null, false);
        }

        return (state, false);
    }

    /// <summary>
    /// Reloads state after CAS, loads player actions, converts to domain, and runs engine resolution.
    /// Returns null if state disappeared after CAS (should not happen in practice).
    /// </summary>
    private async Task<BattleResolutionResult?> LoadActionsAndResolve(
        Guid battleId,
        int turnIndex,
        BattleSnapshot preResolvingState,
        CancellationToken cancellationToken)
    {
        // Reload state to get latest version after CAS
        var state = await _stateStore.GetStateAsync(battleId, cancellationToken);
        if (state == null)
        {
            _logger.LogError("Battle state disappeared after marking as Resolving for BattleId: {BattleId}", battleId);
            return null;
        }

        // Read canonical actions for both players
        var (playerAActionCommand, playerBActionCommand) = await _stateStore.GetActionsAsync(
            battleId,
            turnIndex,
            state.PlayerAId,
            state.PlayerBId,
            cancellationToken);

        // Convert canonical actions to domain PlayerAction objects
        // If action is missing (null), treat as NoAction
        var playerAAction = playerAActionCommand != null
            ? PlayerActionConverter.ToDomainAction(playerAActionCommand)
            : PlayerAction.NoAction(state.PlayerAId, turnIndex);

        var playerBAction = playerBActionCommand != null
            ? PlayerActionConverter.ToDomainAction(playerBActionCommand)
            : PlayerAction.NoAction(state.PlayerBId, turnIndex);

        // Convert to domain state and resolve turn using domain engine (pure logic)
        var domainState = BattleStateToDomainMapper.ToDomainState(state);
        return _battleEngine.ResolveTurn(domainState, playerAAction, playerBAction);
    }

    /// <summary>
    /// Dispatches the resolution result by processing domain events.
    /// Handles battle-ended and turn-continues branches.
    /// </summary>
    private async Task<bool> DispatchResolutionResult(
        Guid battleId,
        int turnIndex,
        BattleSnapshot state,
        BattleResolutionResult resolutionResult,
        CancellationToken cancellationToken)
    {
        foreach (var domainEvent in resolutionResult.Events)
        {
            switch (domainEvent)
            {
                case BattleEndedDomainEvent battleEnded:
                    return await CommitAndNotifyBattleEnded(
                        battleId, turnIndex, state, resolutionResult, battleEnded, cancellationToken);

                case TurnResolvedDomainEvent turnResolved:
                    return await CommitAndNotifyTurnContinued(
                        battleId, turnIndex, state, resolutionResult, turnResolved, cancellationToken);

                case PlayerDamagedDomainEvent:
                    // Handled within CommitAndNotifyTurnContinued
                    break;
            }
        }

        return true;
    }

    /// <summary>
    /// Commits battle end to Redis and sends notifications/events if this call ended the battle.
    /// Exact sequence: commit → notify clients → publish integration event → log.
    /// </summary>
    private async Task<bool> CommitAndNotifyBattleEnded(
        Guid battleId,
        int turnIndex,
        BattleSnapshot state,
        BattleResolutionResult resolutionResult,
        BattleEndedDomainEvent battleEnded,
        CancellationToken cancellationToken)
    {
        // Commit battle end atomically (includes HP update and terminal outcome snapshot).
        // The outcome is persisted onto Redis state so recovery can reconstruct a faithful
        // BattleCompleted if the process crashes before the bus-outbox flush below — without
        // this, the orphan fallback would republish every crashed battle as a data-less draw.
        var endOutcome = new BattleEndOutcome(
            battleEnded.WinnerPlayerId,
            battleEnded.Reason,
            battleEnded.FinalTurnIndex,
            battleEnded.OccurredAt);

        var endResult = await _stateStore.EndBattleAndMarkResolvedAsync(
            battleId,
            turnIndex,
            resolutionResult.NewState.NoActionStreakBoth,
            resolutionResult.NewState.PlayerA.CurrentHp,
            resolutionResult.NewState.PlayerB.CurrentHp,
            endOutcome,
            cancellationToken);

        if (endResult == EndBattleCommitResult.EndedNow)
        {
            // Track final turn in DbContext for atomic commit with outbox
            if (resolutionResult.TurnLog is not null)
            {
                _turnHistoryStore.TrackTurn(
                    battleId, turnIndex, resolutionResult.TurnLog,
                    resolutionResult.NewState.PlayerA.CurrentHp,
                    resolutionResult.NewState.PlayerB.CurrentHp);
            }

            // Mirror the per-turn notification ordering used by
            // CommitAndNotifyTurnContinued (damage → resolved → state) so the
            // killing-blow turn animates HP bars and reconciles client state
            // identically to a normal turn. TurnOpened is intentionally
            // omitted on the end path since no next turn exists; BattleEnded
            // takes its slot. Damage events are produced by the domain engine
            // alongside BattleEndedDomainEvent (ApplySimultaneousDamageAndEvents
            // runs before the end check), so resolutionResult.Events still
            // carries them on a battle-ending turn.
            var damageEvents = resolutionResult.Events.OfType<PlayerDamagedDomainEvent>().ToList();
            foreach (var damageEvent in damageEvents)
            {
                await _notifier.NotifyPlayerDamagedAsync(
                    battleId,
                    damageEvent.PlayerId,
                    damageEvent.Damage,
                    damageEvent.RemainingHp,
                    damageEvent.TurnIndex,
                    cancellationToken);
            }

            // Emit TurnResolved for the killing-blow turn before BattleEnded so
            // clients can populate the per-turn feed and Round Map. The domain
            // engine emits BattleEndedDomainEvent instead of TurnResolvedDomainEvent
            // when a turn ends the battle, so without this call the final turn's
            // resolution is persisted but never pushed to live clients (HTTP
            // backfill on /feed still works). Sequenced before NotifyBattleEnded
            // on the same hub instance to preserve per-connection ordering.
            if (resolutionResult.TurnLog is not null)
            {
                await _notifier.NotifyTurnResolvedAsync(
                    battleId,
                    turnIndex,
                    FormatActionFromLog(resolutionResult.TurnLog.AtoB, resolutionResult.TurnLog.BtoA),
                    FormatActionFromLog(resolutionResult.TurnLog.BtoA, resolutionResult.TurnLog.AtoB),
                    resolutionResult.TurnLog,
                    cancellationToken);
            }

            // Only notify/publish if battle ended in this call
            await _notifier.NotifyBattleEndedAsync(
                battleId,
                battleEnded.Reason.ToString(),
                battleEnded.WinnerPlayerId,
                battleEnded.OccurredAt,
                cancellationToken);

            // Trailing BattleStateUpdated mirrors the post-turn reconcile that
            // CommitAndNotifyTurnContinued sends on every normal turn. On the
            // end path it carries phase=Ended, the terminal HP values, and
            // EndedReason so a client that missed any of the prior frames
            // (TurnResolved/BattleEnded) still converges. Reload the snapshot
            // first to pick up the authoritative version bumped by the Redis
            // commit; HP comes from resolutionResult.NewState (matches the
            // values just written) so the BattleStateUpdated frame agrees with
            // the PlayerDamaged frames emitted moments earlier.
            var stateAfterEnd = await _stateStore.GetStateAsync(battleId, cancellationToken);
            if (stateAfterEnd != null)
            {
                await _notifier.NotifyBattleStateUpdatedAsync(
                    battleId,
                    stateAfterEnd.PlayerAId,
                    stateAfterEnd.PlayerBId,
                    stateAfterEnd.Ruleset,
                    stateAfterEnd.Phase.ToString(),
                    stateAfterEnd.TurnIndex,
                    stateAfterEnd.DeadlineUtc,
                    resolutionResult.NewState.NoActionStreakBoth,
                    turnIndex,
                    battleEnded.Reason.ToString(),
                    stateAfterEnd.Version,
                    resolutionResult.NewState.PlayerA.CurrentHp,
                    resolutionResult.NewState.PlayerB.CurrentHp,
                    stateAfterEnd.PlayerAName,
                    stateAfterEnd.PlayerBName,
                    stateAfterEnd.PlayerAMaxHp,
                    stateAfterEnd.PlayerBMaxHp,
                    cancellationToken);
            }
            else
            {
                _logger.LogWarning(
                    "Battle state disappeared after EndBattleAndMarkResolvedAsync for BattleId: {BattleId}; "
                    + "skipping trailing BattleStateUpdated notification.",
                    battleId);
            }

            // Publish canonical BattleCompleted (consumed by Players, Matchmaking, and Battle projection)
            // DurationMs=0: battle CreatedAt is not available in Redis state; deferred to future enhancement
            await _eventPublisher.PublishBattleCompletedAsync(
                battleId,
                state.MatchId,
                state.PlayerAId,
                state.PlayerBId,
                battleEnded.Reason,
                battleEnded.WinnerPlayerId,
                battleEnded.OccurredAt,
                turnCount: battleEnded.FinalTurnIndex,
                durationMs: 0,
                rulesetVersion: state.Ruleset.Version,
                cancellationToken);

            // Flush the bus outbox. UseBusOutbox buffers IPublishEndpoint.Publish calls on the
            // DbContext change tracker; without SaveChangesAsync the outbox row is never written
            // and the event never reaches RabbitMQ. Idempotency is protected upstream by the
            // Redis EndedNow gate, which ensures this branch runs at most once per battle.
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Battle {BattleId} ended. Reason: {Reason}, Winner: {WinnerPlayerId}",
                battleId, battleEnded.Reason, battleEnded.WinnerPlayerId);
        }
        else if (endResult == EndBattleCommitResult.AlreadyEnded)
        {
            _logger.LogInformation(
                "Battle {BattleId} already ended (duplicate ResolveTurn), skipping notifications",
                battleId);
        }
        else
        {
            _logger.LogWarning(
                "Battle {BattleId} could not be ended (NotCommitted). TurnIndex: {TurnIndex}",
                battleId, turnIndex);
        }

        return true; // Battle ended (or already ended)
    }

    /// <summary>
    /// Commits turn resolution + next turn opening, then sends all notifications.
    /// Exact sequence: commit → reload state → damage notifications → turn resolved → turn opened → state updated → log.
    /// </summary>
    private async Task<bool> CommitAndNotifyTurnContinued(
        Guid battleId,
        int turnIndex,
        BattleSnapshot state,
        BattleResolutionResult resolutionResult,
        TurnResolvedDomainEvent turnResolved,
        CancellationToken cancellationToken)
    {
        var nextTurnIndex = turnIndex + 1;
        var turnSeconds = state.Ruleset.TurnSeconds;
        var nextDeadline = _clock.UtcNow.AddSeconds(turnSeconds);

        // Commit turn resolution + next turn opening atomically (includes HP update)
        var nextTurnOpened = await _stateStore.MarkTurnResolvedAndOpenNextAsync(
            battleId,
            turnIndex,
            nextTurnIndex,
            nextDeadline,
            resolutionResult.NewState.NoActionStreakBoth,
            resolutionResult.NewState.PlayerA.CurrentHp,
            resolutionResult.NewState.PlayerB.CurrentHp,
            cancellationToken);

        if (!nextTurnOpened)
        {
            _logger.LogError(
                "Failed to open next turn {NextTurnIndex} for BattleId: {BattleId}",
                nextTurnIndex, battleId);
            return false;
        }

        // Persist turn history (best-effort — must not block battle progression)
        if (resolutionResult.TurnLog is not null)
        {
            try
            {
                await _turnHistoryStore.PersistTurnAsync(
                    battleId, turnIndex, resolutionResult.TurnLog,
                    resolutionResult.NewState.PlayerA.CurrentHp,
                    resolutionResult.NewState.PlayerB.CurrentHp,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to persist turn history for BattleId: {BattleId}, Turn: {TurnIndex}. "
                    + "Battle continues. Post-match feed may have a gap.",
                    battleId, turnIndex);
            }
        }

        // Reload state to get authoritative deadline
        var stateAfterTurnOpen = await _stateStore.GetStateAsync(battleId, cancellationToken);
        if (stateAfterTurnOpen == null)
        {
            _logger.LogError("Battle state disappeared after opening next turn for BattleId: {BattleId}", battleId);
            return false;
        }

        var authoritativeNextDeadline = stateAfterTurnOpen.DeadlineUtc;

        // Notify clients (only after successful commit)
        // Exact ordering: damage → turn resolved → turn opened → state updated
        var damageEvents = resolutionResult.Events.OfType<PlayerDamagedDomainEvent>().ToList();
        foreach (var damageEvent in damageEvents)
        {
            await _notifier.NotifyPlayerDamagedAsync(
                battleId,
                damageEvent.PlayerId,
                damageEvent.Damage,
                damageEvent.RemainingHp,
                damageEvent.TurnIndex,
                cancellationToken);
        }

        await _notifier.NotifyTurnResolvedAsync(
            battleId,
            turnIndex,
            FormatAction(turnResolved.PlayerAAction),
            FormatAction(turnResolved.PlayerBAction),
            turnResolved.Log,
            cancellationToken);

        await _notifier.NotifyTurnOpenedAsync(
            battleId,
            nextTurnIndex,
            authoritativeNextDeadline,
            cancellationToken);

        await _notifier.NotifyBattleStateUpdatedAsync(
            battleId,
            stateAfterTurnOpen.PlayerAId,
            stateAfterTurnOpen.PlayerBId,
            stateAfterTurnOpen.Ruleset,
            stateAfterTurnOpen.Phase.ToString(),
            nextTurnIndex,
            authoritativeNextDeadline,
            resolutionResult.NewState.NoActionStreakBoth,
            turnIndex,
            null, // endedReason
            stateAfterTurnOpen.Version,
            resolutionResult.NewState.PlayerA.CurrentHp,
            resolutionResult.NewState.PlayerB.CurrentHp,
            stateAfterTurnOpen.PlayerAName,
            stateAfterTurnOpen.PlayerBName,
            stateAfterTurnOpen.PlayerAMaxHp,
            stateAfterTurnOpen.PlayerBMaxHp,
            cancellationToken);

        _logger.LogInformation(
            "Turn {TurnIndex} resolved and Turn {NextTurnIndex} opened for BattleId: {BattleId}. Next deadline: {DeadlineUtc}",
            turnIndex, nextTurnIndex, battleId, authoritativeNextDeadline);

        return true;
    }

    private static string FormatAction(PlayerAction action)
    {
        if (action.IsNoAction)
            return "NoAction";

        var attackZone = action.AttackZone?.ToString() ?? "None";
        if (action.BlockZonePrimary != null && action.BlockZoneSecondary != null)
        {
            return $"Attack: {attackZone}, Block: {action.BlockZonePrimary}-{action.BlockZoneSecondary}";
        }
        return $"Attack: {attackZone}";
    }

    /// <summary>
    /// Formats a player's action string from the turn log without needing the
    /// PlayerAction domain object. Used on the battle-ending turn where the
    /// engine emits BattleEndedDomainEvent (no TurnResolvedDomainEvent), so the
    /// normalized PlayerAction objects aren't carried in the event stream.
    /// Mirrors <see cref="FormatAction"/> output exactly: AttackOutcome.NoAction
    /// is the post-normalization equivalent of PlayerAction.IsNoAction.
    /// <paramref name="attackerLog"/> holds this player's attack zone;
    /// <paramref name="opponentLog"/> holds this player's block zones via its
    /// DefenderBlockPrimary/Secondary fields (the opponent saw them defending).
    /// </summary>
    private static string FormatActionFromLog(AttackResolution attackerLog, AttackResolution opponentLog)
    {
        if (attackerLog.Outcome == AttackOutcome.NoAction)
            return "NoAction";

        var attackZone = attackerLog.AttackZone?.ToString() ?? "None";
        if (opponentLog.DefenderBlockPrimary != null && opponentLog.DefenderBlockSecondary != null)
        {
            return $"Attack: {attackZone}, Block: {opponentLog.DefenderBlockPrimary}-{opponentLog.DefenderBlockSecondary}";
        }
        return $"Attack: {attackZone}";
    }
}
