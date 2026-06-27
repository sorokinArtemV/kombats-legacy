using Kombats.Battle.Application.Ports;
using Kombats.Battle.Application.ReadModels;
using Kombats.Battle.Application.UseCases.Turns;
using Kombats.Battle.Domain.Model;
using Kombats.Battle.Domain.Results;
using Microsoft.Extensions.Logging;

namespace Kombats.Battle.Application.UseCases.Recovery;

/// <summary>
/// Orchestrates recovery of stuck and orphaned battles.
/// Called by BattleRecoveryWorker (thin scheduler in Bootstrap).
///
/// Three recovery paths:
/// 1. Stuck-in-Resolving: battle has Redis state with Phase=Resolving → re-attempt resolution
/// 2. Redis-Ended: Redis state Phase=Ended but Postgres non-terminal → force-end atomically (state + outbox)
/// 3. Orphaned: non-terminal in Postgres with no Redis state → force-end atomically (state + outbox)
/// </summary>
internal sealed class BattleRecoveryService(
    IBattleRecoveryRepository recoveryRepo,
    IBattleStateStore stateStore,
    BattleTurnAppService turnAppService,
    IBattleEventPublisher eventPublisher,
    ILogger<BattleRecoveryService> logger)
{
    /// <summary>
    /// Scans for non-terminal battles older than the cutoff and attempts recovery.
    /// Returns the number of battles processed (for logging).
    /// </summary>
    public async Task<int> ScanAndRecoverAsync(
        DateTimeOffset cutoff,
        int batchSize,
        CancellationToken ct)
    {
        var staleBattleIds = await recoveryRepo.GetNonTerminalBattleIdsOlderThanAsync(cutoff, batchSize, ct);

        if (staleBattleIds.Count == 0)
            return 0;

        logger.LogInformation(
            "Found {Count} non-terminal battles older than cutoff for recovery check",
            staleBattleIds.Count);

        int processed = 0;
        foreach (var battleId in staleBattleIds)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                await RecoverBattleAsync(battleId, ct);
                processed++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error recovering battle {BattleId}", battleId);
            }
        }

        return processed;
    }

    public async Task RecoverBattleAsync(Guid battleId, CancellationToken ct)
    {
        var redisState = await stateStore.GetStateAsync(battleId, ct);

        if (redisState is null)
        {
            await ForceEndOrphanedBattleAsync(battleId, ct);
        }
        else if (redisState.Phase == BattlePhase.Resolving)
        {
            logger.LogWarning("Battle {BattleId} stuck in Resolving phase, attempting recovery", battleId);
            var resolved = await turnAppService.ResolveTurnAsync(battleId, ct);
            logger.Log(
                resolved ? LogLevel.Information : LogLevel.Warning,
                resolved
                    ? "Successfully recovered stuck battle {BattleId} from Resolving"
                    : "Failed to recover battle {BattleId} from Resolving",
                battleId);
        }
        else if (redisState.Phase == BattlePhase.Ended)
        {
            // Redis says ended but Postgres is still non-terminal.
            // The process likely crashed after Redis committed Phase=Ended but before
            // BattleCompleted was written to the outbox. Force-end atomically.
            await ForceEndRedisEndedBattleAsync(battleId, redisState, ct);
        }
        // TurnOpen or ArenaOpen: battle is still active in Redis — not an orphan, just old
    }

    /// <summary>
    /// Force-ends an orphaned battle atomically: marks ended in Postgres AND publishes
    /// BattleCompleted to the MassTransit outbox in a single SaveChanges transaction.
    /// This prevents event loss if the process crashes between persistence and publication.
    /// </summary>
    public async Task ForceEndOrphanedBattleAsync(Guid battleId, CancellationToken ct)
    {
        logger.LogWarning("Orphaned battle {BattleId}: no Redis state, forcing end in Postgres", battleId);

        var now = DateTimeOffset.UtcNow;

        // Step 1: Mark battle as ended (tracked by DbContext, NOT saved yet)
        var info = await recoveryRepo.TryMarkOrphanedBattleEndedAsync(battleId, now, ct);
        if (info is null)
            return; // Already ended or doesn't exist

        // Step 2: Publish to outbox (adds OutboxMessage to same DbContext, NOT saved yet)
        // TurnCount=0, RulesetVersion=0: no Redis state available for orphaned battles
        int durationMs = (int)(now - info.CreatedAt).TotalMilliseconds;
        await eventPublisher.PublishBattleCompletedAsync(
            battleId,
            info.MatchId,
            info.PlayerAId,
            info.PlayerBId,
            EndBattleReason.SystemError,
            winnerPlayerId: null,
            now,
            turnCount: 0,
            durationMs: Math.Max(0, durationMs),
            rulesetVersion: 0,
            ct);

        // Step 3: Atomic commit — battle state change + outbox record in one transaction
        await recoveryRepo.CommitAsync(ct);

        logger.LogWarning("Force-ended orphaned battle {BattleId} as draw and published BattleCompleted", battleId);
    }

    /// <summary>
    /// Handles the case where Redis state is Ended but Postgres is still non-terminal.
    /// This occurs when the process crashed after Redis committed Phase=Ended but before
    /// BattleCompleted was written to the outbox.
    ///
    /// Prefers the enriched terminal outcome persisted on the Redis state (winner / reason /
    /// final turn / ended-at) — that data is the real outcome of the crashed battle and
    /// produces a faithful BattleCompleted event. Falls back to the data-less SystemError /
    /// null-winner path only when Redis has no enriched outcome (battles that ended before
    /// the enriched end-commit shipped, or edge cases where the fields were not written).
    /// </summary>
    private async Task ForceEndRedisEndedBattleAsync(
        Guid battleId,
        BattleSnapshot redisState,
        CancellationToken ct)
    {
        var hasEnrichedOutcome = redisState.EndReason.HasValue;

        logger.LogWarning(
            "Battle {BattleId} ended in Redis (Phase=Ended) but Postgres non-terminal — recovering (enriched={HasEnriched})",
            battleId, hasEnrichedOutcome);

        var now = DateTimeOffset.UtcNow;

        var info = await recoveryRepo.TryMarkOrphanedBattleEndedAsync(battleId, now, ct);
        if (info is null)
            return; // Already ended (race with projection consumer) or doesn't exist

        var reason = redisState.EndReason ?? EndBattleReason.SystemError;
        var winnerPlayerId = hasEnrichedOutcome ? redisState.EndWinnerPlayerId : null;
        var occurredAt = redisState.EndedAt ?? now;
        int turnCount = redisState.EndFinalTurnIndex ?? redisState.LastResolvedTurnIndex;
        int rulesetVersion = redisState.Ruleset?.Version ?? 0;
        int durationMs = Math.Max(0, (int)(occurredAt - info.CreatedAt).TotalMilliseconds);

        await eventPublisher.PublishBattleCompletedAsync(
            battleId,
            info.MatchId,
            info.PlayerAId,
            info.PlayerBId,
            reason,
            winnerPlayerId,
            occurredAt,
            turnCount: turnCount,
            durationMs: durationMs,
            rulesetVersion: rulesetVersion,
            ct);

        await recoveryRepo.CommitAsync(ct);

        logger.LogWarning(
            "Force-ended Redis-Ended battle {BattleId} with Reason={Reason}, Winner={WinnerPlayerId}, TurnCount={TurnCount} (enriched={HasEnriched})",
            battleId, reason, winnerPlayerId, turnCount, hasEnrichedOutcome);
    }
}
