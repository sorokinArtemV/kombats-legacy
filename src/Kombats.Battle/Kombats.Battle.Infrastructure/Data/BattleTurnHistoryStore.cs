using Kombats.Battle.Application.Ports;
using Kombats.Battle.Domain.Results;
using Kombats.Battle.Infrastructure.Data.DbContext;
using Kombats.Battle.Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kombats.Battle.Infrastructure.Data;

internal sealed class BattleTurnHistoryStore : IBattleTurnHistoryStore
{
    private readonly BattleDbContext _dbContext;
    private readonly ILogger<BattleTurnHistoryStore> _logger;

    public BattleTurnHistoryStore(BattleDbContext dbContext, ILogger<BattleTurnHistoryStore> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task PersistTurnAsync(
        Guid battleId, int turnIndex, TurnResolutionLog log,
        int playerAHpAfter, int playerBHpAfter,
        CancellationToken cancellationToken = default)
    {
        var entity = MapToEntity(battleId, turnIndex, log, playerAHpAfter, playerBHpAfter);

        try
        {
            _dbContext.BattleTurns.Add(entity);
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogDebug(
                "Persisted turn history for BattleId: {BattleId}, TurnIndex: {TurnIndex}",
                battleId, turnIndex);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Idempotent: duplicate (battleId, turnIndex) is a no-op
            _logger.LogDebug(
                "Turn history already exists for BattleId: {BattleId}, TurnIndex: {TurnIndex} (idempotent)",
                battleId, turnIndex);

            // Detach the entity to avoid tracker conflicts on subsequent operations
            _dbContext.Entry(entity).State = EntityState.Detached;
        }
    }

    public void TrackTurn(
        Guid battleId, int turnIndex, TurnResolutionLog log,
        int playerAHpAfter, int playerBHpAfter)
    {
        var entity = MapToEntity(battleId, turnIndex, log, playerAHpAfter, playerBHpAfter);
        _dbContext.BattleTurns.Add(entity);

        _logger.LogDebug(
            "Tracked turn entity for BattleId: {BattleId}, TurnIndex: {TurnIndex} (pending SaveChangesAsync)",
            battleId, turnIndex);
    }

    private static BattleTurnEntity MapToEntity(
        Guid battleId, int turnIndex, TurnResolutionLog log,
        int playerAHpAfter, int playerBHpAfter)
    {
        return new BattleTurnEntity
        {
            BattleId = battleId,
            TurnIndex = turnIndex,
            AtoBAttackZone = log.AtoB.AttackZone?.ToString(),
            AtoBDefenderBlockPrimary = log.AtoB.DefenderBlockPrimary?.ToString(),
            AtoBDefenderBlockSecondary = log.AtoB.DefenderBlockSecondary?.ToString(),
            AtoBWasBlocked = log.AtoB.WasBlocked,
            AtoBWasCrit = log.AtoB.WasCrit,
            AtoBOutcome = log.AtoB.Outcome.ToString(),
            AtoBDamage = log.AtoB.Damage,
            BtoAAttackZone = log.BtoA.AttackZone?.ToString(),
            BtoADefenderBlockPrimary = log.BtoA.DefenderBlockPrimary?.ToString(),
            BtoADefenderBlockSecondary = log.BtoA.DefenderBlockSecondary?.ToString(),
            BtoAWasBlocked = log.BtoA.WasBlocked,
            BtoAWasCrit = log.BtoA.WasCrit,
            BtoAOutcome = log.BtoA.Outcome.ToString(),
            BtoADamage = log.BtoA.Damage,
            PlayerAHpAfter = playerAHpAfter,
            PlayerBHpAfter = playerBHpAfter,
            ResolvedAt = DateTimeOffset.UtcNow
        };
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        return ex.InnerException?.Message?.Contains("23505") == true ||
               ex.InnerException?.Message?.Contains("duplicate key") == true ||
               ex.InnerException?.Message?.Contains("unique constraint") == true;
    }
}
