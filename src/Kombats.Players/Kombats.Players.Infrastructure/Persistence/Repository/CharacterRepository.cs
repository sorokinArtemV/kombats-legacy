using Kombats.Players.Application.Abstractions;
using Kombats.Players.Domain.Entities;
using Kombats.Players.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Kombats.Players.Infrastructure.Persistence.Repository;

internal sealed class CharacterRepository : ICharacterRepository
{
    private readonly PlayersDbContext _db;

    public CharacterRepository(PlayersDbContext db) => _db = db;

    public Task<Character?> GetByIdentityIdAsync(Guid identityId, CancellationToken ct)
        => _db.Characters
            .FirstOrDefaultAsync(c => c.IdentityId == identityId, ct);

    public Task<Character?> GetByIdAsync(Guid characterId, CancellationToken ct)
        => _db.Characters
            .FirstOrDefaultAsync(c => c.Id == characterId, ct);

    public Task AddAsync(Character character, CancellationToken ct)
    {
        _db.Characters.Add(character);
        return Task.CompletedTask;
    }

    public async Task<bool> IsNameTakenAsync(string normalizedName, Guid? excludeCharacterId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(normalizedName))
            return false;

        // Npgsql translates Trim() -> BTRIM(), ToLower() -> LOWER(), matching the DB index.
        return await _db.Characters
            .AnyAsync(c => c.Name != null
                           && c.Name.Trim().ToLower() == normalizedName
                           && (excludeCharacterId == null || c.Id != excludeCharacterId), ct);
    }
}
