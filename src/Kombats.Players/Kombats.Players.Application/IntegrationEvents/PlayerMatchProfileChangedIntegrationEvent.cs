using Kombats.Players.Contracts;
using Kombats.Players.Domain.Entities;

namespace Kombats.Players.Application.IntegrationEvents;

/// <summary>
/// Factory for building the canonical <see cref="PlayerCombatProfileChanged"/> integration event
/// from a domain Character entity.
/// </summary>
internal static class PlayerCombatProfileChangedFactory
{
    public static PlayerCombatProfileChanged FromCharacter(Character character)
    {
        return new PlayerCombatProfileChanged
        {
            MessageId = Guid.NewGuid(),
            IdentityId = character.IdentityId,
            CharacterId = character.Id,
            Name = character.Name,
            Level = character.Level,
            Strength = character.Strength,
            Agility = character.Agility,
            Intuition = character.Intuition,
            Vitality = character.Vitality,
            IsReady = character.IsReady,
            Revision = character.Revision,
            AvatarId = character.AvatarId,
            OccurredAt = DateTimeOffset.UtcNow
        };
    }
}
