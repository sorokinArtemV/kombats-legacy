using Kombats.Abstractions;
using Kombats.Players.Application.Abstractions;
using Kombats.Players.Application.IntegrationEvents;
using Kombats.Players.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace Kombats.Players.Application.UseCases.AllocateStatPoints;

internal sealed class AllocateStatPointsHandler
    : ICommandHandler<AllocateStatPointsCommand, AllocateStatPointsResult>
{
    private readonly IUnitOfWork _uow;
    private readonly ICharacterRepository _characters;
    private readonly ICombatProfilePublisher _profilePublisher;
    private readonly ILogger<AllocateStatPointsHandler> _logger;

    public AllocateStatPointsHandler(
        IUnitOfWork uow,
        ICharacterRepository characters,
        ICombatProfilePublisher profilePublisher,
        ILogger<AllocateStatPointsHandler> logger)
    {
        _uow = uow;
        _characters = characters;
        _profilePublisher = profilePublisher;
        _logger = logger;
    }

    public async Task<Result<AllocateStatPointsResult>> HandleAsync(AllocateStatPointsCommand cmd, CancellationToken ct)
    {
        if (cmd.ExpectedRevision <= 0)
        {
            return Result.Failure<AllocateStatPointsResult>(
                Error.Validation("AllocateStatPoints.ExpectedRevisionInvalid", "ExpectedRevision must be a positive integer."));
        }

        var character = await _characters.GetByIdentityIdAsync(cmd.IdentityId, ct);
        if (character is null)
        {
            return Result.Failure<AllocateStatPointsResult>(
                Error.NotFound("AllocateStatPoints.CharacterNotFound", $"Character for identity {cmd.IdentityId} was not found."));
        }

        if (character.Revision != cmd.ExpectedRevision)
        {
            return Result.Failure<AllocateStatPointsResult>(
                Error.Conflict(
                    "AllocateStatPoints.RevisionMismatch",
                    $"Stale character state. Expected {cmd.ExpectedRevision}, but current is {character.Revision}. Reload and retry."));
        }

        try
        {
            character.AllocatePoints(cmd.Str, cmd.Agi, cmd.Intuition, cmd.Vit, DateTimeOffset.UtcNow);
        }
        catch (DomainException ex)
        {
            return ex.Code switch
            {
                "InvalidState" => Result.Failure<AllocateStatPointsResult>(
                    Error.Conflict("AllocateStatPoints.InvalidState", ex.Message)),

                "NegativePoints" => Result.Failure<AllocateStatPointsResult>(
                    Error.Validation("AllocateStatPoints.NegativePoints", ex.Message)),

                "NotEnoughPoints" => Result.Failure<AllocateStatPointsResult>(
                    Error.Validation("AllocateStatPoints.NotEnoughPoints", ex.Message)),

                "ZeroPoints" => Result.Failure<AllocateStatPointsResult>(
                    Error.Validation("AllocateStatPoints.ZeroPoints", ex.Message)),

                _ => Result.Failure<AllocateStatPointsResult>(
                    Error.Problem("AllocateStatPoints.DomainError", ex.Message))
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
        }
        catch (ConcurrencyConflictException)
        {
            _logger.LogWarning(
                "AllocateStatPoints concurrency conflict for IdentityId={IdentityId}, CharacterId={CharacterId}, ExpectedRevision={ExpectedRevision}",
                cmd.IdentityId, character.Id, cmd.ExpectedRevision);
            return Result.Failure<AllocateStatPointsResult>(
                Error.Conflict(
                    "AllocateStatPoints.ConcurrentUpdate",
                    "Character was modified by another request. Reload and retry."));
        }

        _logger.LogInformation(
            "Stat points allocated for IdentityId={IdentityId}, CharacterId={CharacterId}, Revision={Revision}, Str={Str}, Agi={Agi}, Intuition={Intuition}, Vit={Vit}, UnspentPoints={Unspent}",
            cmd.IdentityId, character.Id, character.Revision,
            character.Strength, character.Agility, character.Intuition, character.Vitality,
            character.UnspentPoints);

        return Result.Success(new AllocateStatPointsResult(
            Strength: character.Strength,
            Agility: character.Agility,
            Intuition: character.Intuition,
            Vitality: character.Vitality,
            UnspentPoints: character.UnspentPoints,
            Revision: character.Revision));
    }
}
