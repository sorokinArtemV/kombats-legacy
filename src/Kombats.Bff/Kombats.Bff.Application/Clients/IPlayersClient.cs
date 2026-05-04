using Kombats.Bff.Application.Models.Internal;

namespace Kombats.Bff.Application.Clients;

public interface IPlayersClient
{
    Task<InternalCharacterResponse?> GetCharacterAsync(CancellationToken cancellationToken = default);
    Task<InternalCharacterResponse?> EnsureCharacterAsync(CancellationToken cancellationToken = default);
    Task<InternalCharacterResponse?> SetCharacterNameAsync(string name, CancellationToken cancellationToken = default);
    Task<InternalCharacterResponse?> AllocateStatsAsync(int expectedRevision, int strength, int agility, int intuition, int vitality, CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes the current identity's character avatar. The AvatarId must be one of the
    /// backend-controlled catalog ids. Returns null if the character is not provisioned (HTTP 404).
    /// </summary>
    Task<InternalChangeAvatarResponse?> ChangeAvatarAsync(int expectedRevision, string avatarId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches the public player profile for any player by identity id.
    /// Returns null if the player is not found (HTTP 404).
    /// </summary>
    Task<InternalPlayerProfileResponse?> GetProfileAsync(Guid playerId, CancellationToken cancellationToken = default);
}
