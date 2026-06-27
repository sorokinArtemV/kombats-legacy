using Kombats.Matchmaking.Application.Abstractions;
using Kombats.Matchmaking.Infrastructure.Data;

namespace Kombats.Matchmaking.Infrastructure.Persistence;

/// <summary>
/// EF Core UnitOfWork implementation.
/// SaveChangesAsync also flushes MassTransit outbox messages atomically.
/// </summary>
internal sealed class EfUnitOfWork : IUnitOfWork
{
    private readonly MatchmakingDbContext _db;

    public EfUnitOfWork(MatchmakingDbContext db)
    {
        _db = db;
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
        => _db.SaveChangesAsync(ct);
}
