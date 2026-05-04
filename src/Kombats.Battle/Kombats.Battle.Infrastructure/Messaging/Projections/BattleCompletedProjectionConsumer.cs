using Kombats.Battle.Infrastructure.Data.DbContext;
using Kombats.Battle.Contracts.Battle;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kombats.Battle.Infrastructure.Messaging.Projections;

/// <summary>
/// Projection consumer for BattleCompleted events.
/// Updates the read model (Postgres Battle entity) when a battle completes.
/// </summary>
public class BattleCompletedProjectionConsumer : IConsumer<BattleCompleted>
{
    private readonly BattleDbContext _dbContext;
    private readonly ILogger<BattleCompletedProjectionConsumer> _logger;

    public BattleCompletedProjectionConsumer(
        BattleDbContext dbContext,
        ILogger<BattleCompletedProjectionConsumer> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<BattleCompleted> context)
    {
        var battleCompleted = context.Message;
        var battleId = battleCompleted.BattleId;

        _logger.LogInformation(
            "Processing BattleCompleted projection for BattleId: {BattleId}, Reason: {Reason}, MessageId: {MessageId}",
            battleId, battleCompleted.Reason, battleCompleted.MessageId);

        var battle = await _dbContext.Battles
            .FirstOrDefaultAsync(b => b.BattleId == battleId, context.CancellationToken);

        if (battle == null)
        {
            _logger.LogWarning(
                "Battle {BattleId} not found in read model for BattleCompleted projection. " +
                "This is idempotent - battle may have been created only in Redis. MessageId: {MessageId}",
                battleId, battleCompleted.MessageId);
            return;
        }

        // Idempotency check: if battle is already in terminal state, treat as idempotent
        if (battle.State == "Ended" && battle.EndedAt != null)
        {
            if (battle.EndReason == battleCompleted.Reason.ToString() &&
                battle.WinnerPlayerId == battleCompleted.WinnerIdentityId &&
                battle.EndedAt == battleCompleted.OccurredAt)
            {
                _logger.LogInformation(
                    "Battle {BattleId} already ended with matching data. Duplicate BattleCompleted event (idempotent). " +
                    "Reason: {Reason}, WinnerIdentityId: {WinnerIdentityId}, OccurredAt: {OccurredAt}, MessageId: {MessageId}",
                    battleId, battleCompleted.Reason, battleCompleted.WinnerIdentityId, battleCompleted.OccurredAt, battleCompleted.MessageId);
                return;
            }

            _logger.LogWarning(
                "Battle {BattleId} already ended but with different data. Existing: Reason={ExistingReason}, " +
                "WinnerPlayerId={ExistingWinner}, EndedAt={ExistingEndedAt}. " +
                "Event: Reason={EventReason}, WinnerIdentityId={EventWinner}, OccurredAt={EventOccurredAt}. " +
                "Keeping existing data (first write wins). MessageId: {MessageId}",
                battleId, battle.EndReason, battle.WinnerPlayerId, battle.EndedAt,
                battleCompleted.Reason, battleCompleted.WinnerIdentityId, battleCompleted.OccurredAt, battleCompleted.MessageId);
            return;
        }

        // Update read model with event data
        battle.State = "Ended";
        battle.EndedAt = battleCompleted.OccurredAt;
        battle.EndReason = battleCompleted.Reason.ToString();
        battle.WinnerPlayerId = battleCompleted.WinnerIdentityId;

        await _dbContext.SaveChangesAsync(context.CancellationToken);

        _logger.LogInformation(
            "Successfully updated read model for Battle {BattleId} from BattleCompleted event. " +
            "Reason: {Reason}, WinnerIdentityId: {WinnerIdentityId}, OccurredAt: {OccurredAt}, MessageId: {MessageId}",
            battleId, battleCompleted.Reason, battleCompleted.WinnerIdentityId, battleCompleted.OccurredAt, battleCompleted.MessageId);
    }
}
