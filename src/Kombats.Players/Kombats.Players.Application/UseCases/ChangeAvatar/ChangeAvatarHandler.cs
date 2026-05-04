using Kombats.Abstractions;
using Kombats.Players.Application.Abstractions;
using Kombats.Players.Application.IntegrationEvents;
using Kombats.Players.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace Kombats.Players.Application.UseCases.ChangeAvatar;

internal sealed class ChangeAvatarHandler
    : ICommandHandler<ChangeAvatarCommand, ChangeAvatarResult>
{
    private readonly IUnitOfWork _uow;
    private readonly ICharacterRepository _characters;
    private readonly ICombatProfilePublisher _profilePublisher;
    private readonly ILogger<ChangeAvatarHandler> _logger;

    public ChangeAvatarHandler(
        IUnitOfWork uow,
        ICharacterRepository characters,
        ICombatProfilePublisher profilePublisher,
        ILogger<ChangeAvatarHandler> logger)
    {
        _uow = uow;
        _characters = characters;
        _profilePublisher = profilePublisher;
        _logger = logger;
    }

    public async Task<Result<ChangeAvatarResult>> HandleAsync(ChangeAvatarCommand cmd, CancellationToken ct)
    {
        if (cmd.ExpectedRevision <= 0)
        {
            return Result.Failure<ChangeAvatarResult>(
                Error.Validation("ChangeAvatar.ExpectedRevisionInvalid", "ExpectedRevision must be a positive integer."));
        }

        var character = await _characters.GetByIdentityIdAsync(cmd.IdentityId, ct);
        if (character is null)
        {
            return Result.Failure<ChangeAvatarResult>(
                Error.NotFound("ChangeAvatar.CharacterNotFound", $"Character for identity {cmd.IdentityId} was not found."));
        }

        if (character.Revision != cmd.ExpectedRevision)
        {
            return Result.Failure<ChangeAvatarResult>(
                Error.Conflict(
                    "ChangeAvatar.RevisionMismatch",
                    $"Stale character state. Expected {cmd.ExpectedRevision}, but current is {character.Revision}. Reload and retry."));
        }

        var revisionBefore = character.Revision;

        try
        {
            character.ChangeAvatar(cmd.AvatarId, DateTimeOffset.UtcNow);
        }
        catch (DomainException ex)
        {
            return ex.Code switch
            {
                "InvalidAvatar" => Result.Failure<ChangeAvatarResult>(
                    Error.Validation("ChangeAvatar.InvalidAvatar", ex.Message)),

                _ => Result.Failure<ChangeAvatarResult>(
                    Error.Problem("ChangeAvatar.DomainError", ex.Message))
            };
        }

        // No-op: requested avatar matches current. Skip publish + save so we don't
        // emit an empty integration event or churn the outbox for a stale client retry.
        if (character.Revision == revisionBefore)
        {
            return Result.Success(new ChangeAvatarResult(
                AvatarId: character.AvatarId,
                Revision: character.Revision));
        }

        // Publish before SaveChanges so outbox entries are committed atomically
        // with domain changes (AD-01). With MassTransit outbox configured,
        // IPublishEndpoint.Publish() writes to outbox tables in the DbContext.
        await _profilePublisher.PublishAsync(
            PlayerCombatProfileChangedFactory.FromCharacter(character), ct);

        try
        {
            await _uow.SaveChangesAsync(ct);
        }
        catch (ConcurrencyConflictException)
        {
            _logger.LogWarning(
                "ChangeAvatar concurrency conflict for IdentityId={IdentityId}, CharacterId={CharacterId}, ExpectedRevision={ExpectedRevision}",
                cmd.IdentityId, character.Id, cmd.ExpectedRevision);
            return Result.Failure<ChangeAvatarResult>(
                Error.Conflict(
                    "ChangeAvatar.ConcurrentUpdate",
                    "Character was modified by another request. Reload and retry."));
        }

        _logger.LogInformation(
            "Avatar changed for IdentityId={IdentityId}, CharacterId={CharacterId}, Revision={Revision}, AvatarId={AvatarId}",
            cmd.IdentityId, character.Id, character.Revision, character.AvatarId);

        return Result.Success(new ChangeAvatarResult(
            AvatarId: character.AvatarId,
            Revision: character.Revision));
    }
}
