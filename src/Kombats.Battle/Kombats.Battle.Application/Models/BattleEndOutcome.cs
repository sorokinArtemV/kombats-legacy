using Kombats.Battle.Domain.Results;

namespace Kombats.Battle.Application.Models;

/// <summary>
/// Terminal outcome of a battle, persisted alongside the Ended phase in the state store
/// so that recovery can reconstruct a faithful BattleCompleted event if the process crashes
/// between Redis end-commit and outbox flush. This restores fidelity that would otherwise
/// be lost to the OrphanRecovery fallback (which publishes every crashed battle as a draw).
/// </summary>
public sealed record BattleEndOutcome(
    Guid? WinnerPlayerId,
    EndBattleReason Reason,
    int FinalTurnIndex,
    DateTimeOffset OccurredAt);
