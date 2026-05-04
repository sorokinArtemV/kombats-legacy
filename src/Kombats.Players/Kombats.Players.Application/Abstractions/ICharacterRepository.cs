using Kombats.Players.Domain.Entities;

namespace Kombats.Players.Application.Abstractions;

internal interface ICharacterRepository
{
    Task<Character?> GetByIdentityIdAsync(Guid identityId, CancellationToken ct);
    Task<Character?> GetByIdAsync(Guid characterId, CancellationToken ct);
    Task AddAsync(Character character, CancellationToken ct);
    /// <summary>
    /// Returns true if any character has the given normalized name (trim + lower invariant).
    /// When <paramref name="excludeCharacterId"/> is set, that character is excluded (for renames).
    /// </summary>
    Task<bool> IsNameTakenAsync(string normalizedName, Guid? excludeCharacterId, CancellationToken ct);
}
