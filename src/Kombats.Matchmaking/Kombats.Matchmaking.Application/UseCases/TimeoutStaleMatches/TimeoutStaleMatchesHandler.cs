using Kombats.Abstractions;
using Kombats.Matchmaking.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Kombats.Matchmaking.Application.UseCases.TimeoutStaleMatches;

internal sealed class TimeoutStaleMatchesHandler : ICommandHandler<TimeoutStaleMatchesCommand, int>
{
    private readonly IMatchRepository _matchRepository;
    private readonly IPlayerMatchStatusStore _statusStore;
    private readonly ILogger<TimeoutStaleMatchesHandler> _logger;

    public TimeoutStaleMatchesHandler(
        IMatchRepository matchRepository,
        IPlayerMatchStatusStore statusStore,
        ILogger<TimeoutStaleMatchesHandler> logger)
    {
        _matchRepository = matchRepository;
        _statusStore = statusStore;
        _logger = logger;
    }

    public async Task<Result<int>> HandleAsync(TimeoutStaleMatchesCommand cmd, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        // Timeout BattleCreateRequested matches (60s default)
        var cutoff = now.AddSeconds(-cmd.TimeoutSeconds);
        var affectedPlayers = await _matchRepository.TimeoutStaleMatchesAsync(cutoff, now, ct);

        if (affectedPlayers.Count > 0)
        {
            _logger.LogWarning(
                "Timed out {Count} stale BattleCreateRequested matches older than {TimeoutSeconds}s",
                affectedPlayers.Count, cmd.TimeoutSeconds);

            await ClearPlayerStatusAsync(affectedPlayers, ct);
        }

        // Timeout BattleCreated matches (10min default — EI-015)
        var battleCreatedCutoff = now.AddSeconds(-cmd.BattleCreatedTimeoutSeconds);
        var battleCreatedPlayers = await _matchRepository.TimeoutStaleBattleCreatedMatchesAsync(battleCreatedCutoff, now, ct);

        if (battleCreatedPlayers.Count > 0)
        {
            _logger.LogWarning(
                "Timed out {Count} stale BattleCreated matches older than {BattleCreatedTimeoutSeconds}s",
                battleCreatedPlayers.Count, cmd.BattleCreatedTimeoutSeconds);

            await ClearPlayerStatusAsync(battleCreatedPlayers, ct);
        }

        return affectedPlayers.Count + battleCreatedPlayers.Count;
    }

    private async Task ClearPlayerStatusAsync(List<(Guid PlayerAId, Guid PlayerBId)> players, CancellationToken ct)
    {
        foreach (var (playerAId, playerBId) in players)
        {
            await _statusStore.RemoveStatusAsync(playerAId, ct);
            await _statusStore.RemoveStatusAsync(playerBId, ct);

            _logger.LogInformation(
                "Cleared Redis match status for timed-out match players: PlayerA={PlayerAId}, PlayerB={PlayerBId}",
                playerAId, playerBId);
        }
    }
}
