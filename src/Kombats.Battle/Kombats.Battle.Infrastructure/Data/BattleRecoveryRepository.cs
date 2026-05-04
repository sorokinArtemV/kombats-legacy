using Kombats.Battle.Application.Ports;
using Kombats.Battle.Infrastructure.Data.DbContext;
using Microsoft.EntityFrameworkCore;

namespace Kombats.Battle.Infrastructure.Data;

/// <summary>
/// Infrastructure implementation: queries and updates Postgres for battle recovery.
/// </summary>
internal sealed class BattleRecoveryRepository(BattleDbContext dbContext) : IBattleRecoveryRepository
{
    public async Task<IReadOnlyList<Guid>> GetNonTerminalBattleIdsOlderThanAsync(
        DateTimeOffset cutoff,
        int limit,
        CancellationToken ct = default)
    {
        return await dbContext.Battles
            .Where(b => b.State != "Ended" && b.CreatedAt < cutoff)
            .OrderBy(b => b.CreatedAt)
            .Select(b => b.BattleId)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<OrphanedBattleInfo?> TryMarkOrphanedBattleEndedAsync(
        Guid battleId,
        DateTimeOffset endedAt,
        CancellationToken ct = default)
    {
        var battle = await dbContext.Battles
            .FirstOrDefaultAsync(b => b.BattleId == battleId && b.State != "Ended", ct);

        if (battle is null)
            return null;

        battle.State = "Ended";
        battle.EndedAt = endedAt;
        battle.EndReason = "OrphanRecovery";

        return new OrphanedBattleInfo(battle.MatchId, battle.PlayerAId, battle.PlayerBId, battle.CreatedAt);
    }

    public Task CommitAsync(CancellationToken ct = default) => dbContext.SaveChangesAsync(ct);
}
