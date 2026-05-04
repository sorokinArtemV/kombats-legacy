using Kombats.Abstractions;
using Kombats.Players.Application.Abstractions;

namespace Kombats.Players.Application.UseCases.GetCharacter;

internal sealed class GetCharacterHandler : IQueryHandler<GetCharacterQuery, CharacterStateResult>
{
    private readonly ICharacterRepository _characters;

    public GetCharacterHandler(ICharacterRepository characters)
    {
        _characters = characters;
    }

    public async Task<Result<CharacterStateResult>> HandleAsync(GetCharacterQuery query, CancellationToken ct)
    {
        var character = await _characters.GetByIdentityIdAsync(query.IdentityId, ct);
        if (character is null)
        {
            return Result.Failure<CharacterStateResult>(
                Error.NotFound("GetCharacter.NotProvisioned", "Call POST /api/me/ensure after login."));
        }

        return Result.Success(CharacterStateResult.FromCharacter(character));
    }
}
