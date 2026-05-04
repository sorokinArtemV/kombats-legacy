using Kombats.Battle.Contracts.Battle;
using Kombats.Matchmaking.Application.Abstractions;
using Kombats.Matchmaking.Domain;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Kombats.Matchmaking.Infrastructure.Messaging.Consumers;

/// <summary>
/// Thin integration consumer for the canonical BattleCompleted event from Battle service.
/// Advances match state to terminal (Completed or TimedOut) and clears player match status
/// so players are no longer stuck as matched after battle ends.
/// Uses CAS to ensure idempotent, race-free state transition.
/// </summary>
internal sealed class BattleCompletedConsumer : IConsumer<BattleCompleted>
{
    private readonly IMatchRepository _matchRepository;
    private readonly IPlayerMatchStatusStore _playerMatchStatusStore;
    private readonly ILogger<BattleCompletedConsumer> _logger;

    public BattleCompletedConsumer(
        IMatchRepository matchRepository,
        IPlayerMatchStatusStore playerMatchStatusStore,
        ILogger<BattleCompletedConsumer> logger)
    {
        _matchRepository = matchRepository;
        _playerMatchStatusStore = playerMatchStatusStore;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<BattleCompleted> context)
    {
        var msg = context.Message;

        _logger.LogInformation(
            "Received BattleCompleted: BattleId={BattleId}, MatchId={MatchId}, Reason={Reason}",
            msg.BattleId, msg.MatchId, msg.Reason);

        var terminalState = msg.Reason == BattleEndReason.Timeout
            ? MatchState.TimedOut
            : MatchState.Completed;

        var updated = await _matchRepository.TryAdvanceToTerminalAsync(
            msg.MatchId,
            terminalState,
            DateTimeOffset.UtcNow,
            context.CancellationToken);

        if (updated)
        {
            _logger.LogInformation(
                "Match {MatchId} advanced to {TerminalState} for BattleId={BattleId}",
                msg.MatchId, terminalState, msg.BattleId);
        }
        else
        {
            _logger.LogInformation(
                "Match {MatchId} was not in BattleCreated state (already terminal or timed out). BattleId={BattleId}",
                msg.MatchId, msg.BattleId);
        }

        // Clear player match status so players can re-queue
        await _playerMatchStatusStore.RemoveStatusAsync(msg.PlayerAIdentityId, context.CancellationToken);
        await _playerMatchStatusStore.RemoveStatusAsync(msg.PlayerBIdentityId, context.CancellationToken);

        _logger.LogInformation(
            "Cleared match status for PlayerA={PlayerAId} and PlayerB={PlayerBId} after BattleCompleted",
            msg.PlayerAIdentityId, msg.PlayerBIdentityId);
    }
}
