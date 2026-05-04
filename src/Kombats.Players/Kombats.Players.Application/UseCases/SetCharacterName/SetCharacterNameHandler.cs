using Kombats.Abstractions;
using Kombats.Players.Application.Abstractions;
using Kombats.Players.Application.IntegrationEvents;
using Kombats.Players.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace Kombats.Players.Application.UseCases.SetCharacterName;

internal sealed class SetCharacterNameHandler
    : ICommandHandler<SetCharacterNameCommand, CharacterStateResult>
{
    private readonly IUnitOfWork _uow;
    private readonly ICharacterRepository _characters;
    private readonly ICombatProfilePublisher _profilePublisher;
    private readonly ILogger<SetCharacterNameHandler> _logger;

    public SetCharacterNameHandler(
        IUnitOfWork uow,
        ICharacterRepository characters,
        ICombatProfilePublisher profilePublisher,
        ILogger<SetCharacterNameHandler> logger)
    {
        _uow = uow;
        _characters = characters;
        _profilePublisher = profilePublisher;
        _logger = logger;
    }

    public async Task<Result<CharacterStateResult>> HandleAsync(SetCharacterNameCommand cmd, CancellationToken ct)
    {
        var character = await _characters.GetByIdentityIdAsync(cmd.IdentityId, ct);
        if (character is null)
        {
            return Result.Failure<CharacterStateResult>(
                Error.NotFound("SetCharacterName.NotProvisioned", "Character not provisioned. Call POST /api/me/ensure first."));
        }

        var normalizedName = cmd.Name.Trim().ToLowerInvariant();
        var nameTaken = await _characters.IsNameTakenAsync(normalizedName, character.Id, ct);
        if (nameTaken)
        {
            return Result.Failure<CharacterStateResult>(
                Error.Conflict("SetCharacterName.NameTaken", "This display name is already taken."));
        }

        try
        {
            character.SetNameOnce(cmd.Name, DateTimeOffset.UtcNow);
        }
        catch (DomainException ex)
        {
            return ex.Code switch
            {
                "InvalidState" => Result.Failure<CharacterStateResult>(
                    Error.Conflict("SetCharacterName.InvalidState", ex.Message)),

                "NameAlreadySet" => Result.Failure<CharacterStateResult>(
                    Error.Conflict("SetCharacterName.NameAlreadySet", ex.Message)),

                "InvalidName" => Result.Failure<CharacterStateResult>(
                    Error.Validation("SetCharacterName.InvalidName", ex.Message)),

                _ => Result.Failure<CharacterStateResult>(
                    Error.Problem("SetCharacterName.DomainError", ex.Message))
            };
        }

        // Publish before SaveChanges so outbox entries are committed atomically
        // with domain changes (AD-01). With MassTransit outbox configured,
        // IPublishEndpoint.Publish() writes to outbox tables in the DbContext.
        await _profilePublisher.PublishAsync(
            PlayerCombatProfileChangedFactory.FromCharacter(character), ct);

        try
        {
            await _uow.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Character named for IdentityId={IdentityId}, CharacterId={CharacterId}, OnboardingState={OnboardingState}",
                cmd.IdentityId, character.Id, character.OnboardingState);

            return Result.Success(CharacterStateResult.FromCharacter(character));
        }
        catch (ConcurrencyConflictException)
        {
            return Result.Failure<CharacterStateResult>(
                Error.Conflict(
                    "SetCharacterName.ConcurrentUpdate",
                    "Character was modified by another request. Reload and retry."));
        }
        catch (UniqueConstraintConflictException ex) when (ex.ConflictKind == UniqueConflictKind.CharacterName)
        {
            return Result.Failure<CharacterStateResult>(
                Error.Conflict("SetCharacterName.NameTaken", "This display name is already taken."));
        }
    }
}
