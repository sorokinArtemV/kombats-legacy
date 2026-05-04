using Kombats.Battle.Application.Ports;
using Kombats.Battle.Application.UseCases.GetBattleHistory;
using Kombats.Battle.Infrastructure.Data.DbContext;
using Microsoft.EntityFrameworkCore;

namespace Kombats.Battle.Infrastructure.Data;

internal sealed class BattleHistoryRepository : IBattleHistoryRepository
{
    private readonly BattleDbContext _dbContext;

    public BattleHistoryRepository(BattleDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<BattleHistoryResult?> GetBattleHistoryAsync(Guid battleId, CancellationToken cancellationToken = default)
    {
        var battle = await _dbContext.Battles
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.BattleId == battleId, cancellationToken);

        if (battle is null)
            return null;

        var turns = await _dbContext.BattleTurns
            .AsNoTracking()
            .Where(t => t.BattleId == battleId)
            .OrderBy(t => t.TurnIndex)
            .Select(t => new TurnHistoryItem
            {
                TurnIndex = t.TurnIndex,
                AtoBAttackZone = t.AtoBAttackZone,
                AtoBDefenderBlockPrimary = t.AtoBDefenderBlockPrimary,
                AtoBDefenderBlockSecondary = t.AtoBDefenderBlockSecondary,
                AtoBWasBlocked = t.AtoBWasBlocked,
                AtoBWasCrit = t.AtoBWasCrit,
                AtoBOutcome = t.AtoBOutcome,
                AtoBDamage = t.AtoBDamage,
                BtoAAttackZone = t.BtoAAttackZone,
                BtoADefenderBlockPrimary = t.BtoADefenderBlockPrimary,
                BtoADefenderBlockSecondary = t.BtoADefenderBlockSecondary,
                BtoAWasBlocked = t.BtoAWasBlocked,
                BtoAWasCrit = t.BtoAWasCrit,
                BtoAOutcome = t.BtoAOutcome,
                BtoADamage = t.BtoADamage,
                PlayerAHpAfter = t.PlayerAHpAfter,
                PlayerBHpAfter = t.PlayerBHpAfter,
                ResolvedAt = t.ResolvedAt
            })
            .ToArrayAsync(cancellationToken);

        return new BattleHistoryResult
        {
            BattleId = battle.BattleId,
            MatchId = battle.MatchId,
            PlayerAId = battle.PlayerAId,
            PlayerBId = battle.PlayerBId,
            PlayerAName = battle.PlayerAName,
            PlayerBName = battle.PlayerBName,
            PlayerAMaxHp = battle.PlayerAMaxHp,
            PlayerBMaxHp = battle.PlayerBMaxHp,
            State = battle.State,
            EndReason = battle.EndReason,
            WinnerPlayerId = battle.WinnerPlayerId,
            CreatedAt = battle.CreatedAt,
            EndedAt = battle.EndedAt,
            Turns = turns
        };
    }
}
