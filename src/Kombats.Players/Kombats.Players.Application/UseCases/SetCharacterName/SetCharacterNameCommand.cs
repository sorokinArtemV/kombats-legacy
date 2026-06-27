using Kombats.Abstractions;

namespace Kombats.Players.Application.UseCases.SetCharacterName;

internal sealed record SetCharacterNameCommand(Guid IdentityId, string Name) : ICommand<CharacterStateResult>;
