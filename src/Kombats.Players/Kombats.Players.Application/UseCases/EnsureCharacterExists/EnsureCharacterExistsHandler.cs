using Kombats.Abstractions;
using Kombats.Players.Application.Abstractions;
using Kombats.Players.Application.IntegrationEvents;
using Kombats.Players.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Kombats.Players.Application.UseCases.EnsureCharacterExists;

internal sealed class EnsureCharacterExistsHandler
    : ICommandHandler<EnsureCharacterExistsCommand, CharacterStateResult>
{
    private readonly IUnitOfWork _uow;
    private readonly ICharacterRepository _characters;
    private readonly ICombatProfilePublisher _profilePublisher;
    private readonly ILogger<EnsureCharacterExistsHandler> _logger;

    public EnsureCharacterExistsHandler(
        IUnitOfWork uow,
        ICharacterRepository characters,
        ICombatProfilePublisher profilePublisher,
        ILogger<EnsureCharacterExistsHandler> logger)
    {
        _uow = uow;
        _characters = characters;
        _profilePublisher = profilePublisher;
        _logger = logger;
    }

    public async Task<Result<CharacterStateResult>> HandleAsync(EnsureCharacterExistsCommand cmd, CancellationToken ct)
    {
        var existing = await _characters.GetByIdentityIdAsync(cmd.IdentityId, ct);
        if (existing is not null)
        {
            return Result.Success(CharacterStateResult.FromCharacter(existing));
        }

        var character = Character.CreateDraft(cmd.IdentityId, DateTimeOffset.UtcNow);
        await _characters.AddAsync(character, ct);

        // Publish before SaveChanges so outbox entries are committed atomically
        // with domain changes (AD-01). With MassTransit outbox configured,
        // IPublishEndpoint.Publish() writes to outbox tables in the DbContext.
        await _profilePublisher.PublishAsync(
            PlayerCombatProfileChangedFactory.FromCharacter(character), ct);

        try
        {
            await _uow.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Character created (draft) for IdentityId={IdentityId}, CharacterId={CharacterId}, OnboardingState={OnboardingState}",
                cmd.IdentityId, character.Id, character.OnboardingState);

            return Result.Success(CharacterStateResult.FromCharacter(character));
        }
        catch (UniqueConstraintConflictException ex) when (ex.ConflictKind == UniqueConflictKind.IdentityId)
        {
            var race = await _characters.GetByIdentityIdAsync(cmd.IdentityId, ct);
            if (race is not null)
            {
                _logger.LogWarning(
                    "EnsureCharacter concurrent-create race resolved for IdentityId={IdentityId}, CharacterId={CharacterId}",
                    cmd.IdentityId, race.Id);
                return Result.Success(CharacterStateResult.FromCharacter(race));
            }

            return Result.Failure<CharacterStateResult>(
                Error.Conflict(
                    "EnsureCharacterExists.ConcurrentCreate",
                    "Character was created by another request. Retry the operation."));
        }
    }
}
