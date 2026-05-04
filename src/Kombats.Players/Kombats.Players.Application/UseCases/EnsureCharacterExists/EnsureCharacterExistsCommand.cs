using Kombats.Abstractions;

namespace Kombats.Players.Application.UseCases.EnsureCharacterExists;

internal sealed record EnsureCharacterExistsCommand(Guid IdentityId) : ICommand<CharacterStateResult>;
