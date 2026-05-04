using Kombats.Players.Application.Abstractions;
using Kombats.Players.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Kombats.Players.Infrastructure.Persistence.EF;

internal sealed class EfUnitOfWork : IUnitOfWork
{
    private const string UniqueViolationSqlState = "23505";

    private static readonly Dictionary<string, UniqueConflictKind> ConstraintMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["ix_characters_identity_id"] = UniqueConflictKind.IdentityId,
            ["ix_characters_name_normalized"] = UniqueConflictKind.CharacterName,
        };

    private readonly PlayersDbContext _db;

    public EfUnitOfWork(PlayersDbContext db)
    {
        _db = db;
    }

    public async Task SaveChangesAsync(CancellationToken ct)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException(ex);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex, out var kind))
        {
            throw new UniqueConstraintConflictException(kind, ex);
        }
    }

    private static bool IsUniqueViolation(DbUpdateException ex, out UniqueConflictKind kind)
    {
        if (ex.InnerException is PostgresException pg && pg.SqlState == UniqueViolationSqlState)
        {
            if (pg.ConstraintName is not null && ConstraintMap.TryGetValue(pg.ConstraintName, out kind))
            {
                return true;
            }

            kind = UniqueConflictKind.Unknown;
            return true;
        }

        kind = default;
        return false;
    }
}
