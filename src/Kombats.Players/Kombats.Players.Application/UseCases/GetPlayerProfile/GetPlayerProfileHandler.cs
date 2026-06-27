using Kombats.Abstractions;
using Kombats.Players.Application.Abstractions;

namespace Kombats.Players.Application.UseCases.GetPlayerProfile;

internal sealed class GetPlayerProfileHandler : IQueryHandler<GetPlayerProfileQuery, GetPlayerProfileQueryResponse>
{
    private readonly ICharacterRepository _characters;

    public GetPlayerProfileHandler(ICharacterRepository characters)
    {
        _characters = characters;
    }

    public async Task<Result<GetPlayerProfileQueryResponse>> HandleAsync(GetPlayerProfileQuery query, CancellationToken ct)
    {
        var character = await _characters.GetByIdentityIdAsync(query.IdentityId, ct);
        if (character is null)
        {
            return Result.Failure<GetPlayerProfileQueryResponse>(
                Error.NotFound("GetPlayerProfile.NotFound", "Player not found."));
        }

        var response = new GetPlayerProfileQueryResponse(
            PlayerId: character.IdentityId,
            DisplayName: character.Name,
            Level: character.Level,
            Strength: character.Strength,
            Agility: character.Agility,
            Intuition: character.Intuition,
            Vitality: character.Vitality,
            Wins: character.Wins,
            Losses: character.Losses,
            OnboardingState: character.OnboardingState,
            AvatarId: character.AvatarId);

        return Result.Success(response);
    }
}
