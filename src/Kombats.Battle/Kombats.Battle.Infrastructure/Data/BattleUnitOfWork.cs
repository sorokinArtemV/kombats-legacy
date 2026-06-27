using Kombats.Battle.Application.Ports;
using Kombats.Battle.Infrastructure.Data.DbContext;

namespace Kombats.Battle.Infrastructure.Data;

internal sealed class BattleUnitOfWork(BattleDbContext dbContext) : IBattleUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => dbContext.SaveChangesAsync(cancellationToken);
}
