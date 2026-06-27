using Kombats.Abstractions;

namespace Kombats.Players.Application.UseCases.GetCharacter;

internal sealed record GetCharacterQuery(Guid IdentityId) : IQuery<CharacterStateResult>;
