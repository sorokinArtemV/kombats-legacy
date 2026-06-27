using Kombats.Abstractions;
using Kombats.Players.Application.Abstractions;
using Kombats.Players.Application.IntegrationEvents;
using Kombats.Players.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Kombats.Players.Application.Battles;

/// <summary>
/// Command to handle the canonical BattleCompleted integration event.
/// Supports no-winner outcomes (WinnerIdentityId/LoserIdentityId may be null).
/// </summary>
internal sealed record HandleBattleCompletedCommand(
    Guid MessageId,
    Guid BattleId,
    Guid? WinnerIdentityId,
    Guid? LoserIdentityId,
    string Reason) : ICommand;

internal sealed class HandleBattleCompletedHandler : ICommandHandler<HandleBattleCompletedCommand>
{
    // XP award constants are duplicated in Battle service (BattleRewards section
    // in appsettings, bound to BattleRewardsOptions) and used to populate
    // WinnerXp / LoserXp on the BattleEnded SignalR payload so the result screen
    // can render the XP row immediately. Players is the source of truth for what
    // is actually awarded; if you change values here, also update Battle's
    // BattleRewardsOptions to keep frontend display consistent with actual award.
    // Future cleanup: extract to shared rewards config service.
    private const long WinnerXp = 10;
    private const long LoserXp = 5;

    private readonly IInboxRepository _inbox;
    private readonly ICharacterRepository _characters;
    private readonly ILevelingConfigProvider _levelingProvider;
    private readonly IUnitOfWork _uow;
    private readonly ICombatProfilePublisher _profilePublisher;
    private readonly ILogger<HandleBattleCompletedHandler> _logger;

    public HandleBattleCompletedHandler(
        IInboxRepository inbox,
        ICharacterRepository characters,
        ILevelingConfigProvider levelingProvider,
        IUnitOfWork uow,
        ICombatProfilePublisher profilePublisher,
        ILogger<HandleBattleCompletedHandler> logger)
    {
        _inbox = inbox;
        _characters = characters;
        _levelingProvider = levelingProvider;
        _uow = uow;
        _profilePublisher = profilePublisher;
        _logger = logger;
    }

    public async Task<Result> HandleAsync(HandleBattleCompletedCommand command, CancellationToken cancellationToken)
    {
        if (await _inbox.IsProcessedAsync(command.MessageId, cancellationToken))
        {
            _logger.LogInformation(
                "BattleCompleted already processed, skipping: MessageId={MessageId}, BattleId={BattleId}, Idempotent={Idempotent}",
                command.MessageId, command.BattleId, true);
            return Result.Success();
        }

        var config = _levelingProvider.Get();
        Character? winner = null;
        Character? loser = null;

        if (command.WinnerIdentityId.HasValue && command.LoserIdentityId.HasValue)
        {
            winner = await _characters.GetByIdentityIdAsync(command.WinnerIdentityId.Value, cancellationToken);
            if (winner is null)
            {
                return Result.Failure(Error.NotFound(
                    "HandleBattleCompleted.WinnerNotFound",
                    $"Character for winner identity {command.WinnerIdentityId} not found."));
            }

            loser = await _characters.GetByIdentityIdAsync(command.LoserIdentityId.Value, cancellationToken);
            if (loser is null)
            {
                return Result.Failure(Error.NotFound(
                    "HandleBattleCompleted.LoserNotFound",
                    $"Character for loser identity {command.LoserIdentityId} not found."));
            }

            var now = DateTimeOffset.UtcNow;
            winner.AddExperience(WinnerXp, config, now);
            winner.RecordWin(now);

            loser.AddExperience(LoserXp, config, now);
            loser.RecordLoss(now);
        }

        await _inbox.AddProcessedAsync(command.MessageId, DateTimeOffset.UtcNow, cancellationToken);

        // Publish before SaveChanges so outbox entries are committed atomically
        // with domain changes (AD-01). With MassTransit outbox configured,
        // IPublishEndpoint.Publish() writes to outbox tables in the DbContext.
        if (winner is not null)
        {
            await _profilePublisher.PublishAsync(
                PlayerCombatProfileChangedFactory.FromCharacter(winner), cancellationToken);
        }

        if (loser is not null)
        {
            await _profilePublisher.PublishAsync(
                PlayerCombatProfileChangedFactory.FromCharacter(loser), cancellationToken);
        }

        await _uow.SaveChangesAsync(cancellationToken);

        if (winner is not null && loser is not null)
        {
            _logger.LogInformation(
                "BattleCompleted applied XP: MessageId={MessageId}, BattleId={BattleId}, Reason={Reason}, Winner={WinnerIdentityId} Level={WinnerLevel} TotalXp={WinnerTotalXp}, Loser={LoserIdentityId} Level={LoserLevel} TotalXp={LoserTotalXp}",
                command.MessageId, command.BattleId, command.Reason,
                winner.IdentityId, winner.Level, winner.TotalXp,
                loser.IdentityId, loser.Level, loser.TotalXp);
        }
        else
        {
            _logger.LogInformation(
                "BattleCompleted inbox recorded (no-winner outcome): MessageId={MessageId}, BattleId={BattleId}, Reason={Reason}",
                command.MessageId, command.BattleId, command.Reason);
        }

        return Result.Success();
    }
}
