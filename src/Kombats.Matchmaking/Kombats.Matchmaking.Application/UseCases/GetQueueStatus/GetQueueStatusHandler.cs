using Kombats.Abstractions;
using Kombats.Matchmaking.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Kombats.Matchmaking.Application.UseCases.GetQueueStatus;

internal sealed class GetQueueStatusHandler : IQueryHandler<GetQueueStatusQuery, QueueStatusResult>
{
    private readonly IMatchRepository _matchRepository;
    private readonly IPlayerMatchStatusStore _statusStore;
    private readonly ILogger<GetQueueStatusHandler> _logger;

    public GetQueueStatusHandler(
        IMatchRepository matchRepository,
        IPlayerMatchStatusStore statusStore,
        ILogger<GetQueueStatusHandler> logger)
    {
        _matchRepository = matchRepository;
        _statusStore = statusStore;
        _logger = logger;
    }

    public async Task<Result<QueueStatusResult>> HandleAsync(GetQueueStatusQuery query, CancellationToken ct)
    {
        // Postgres is source of truth for match state
        var activeMatch = await _matchRepository.GetActiveForPlayerAsync(query.PlayerId, ct);
        if (activeMatch != null)
        {
            return new QueueStatusResult(
                QueueStatusType.Matched,
                activeMatch.MatchId,
                activeMatch.BattleId,
                activeMatch.State);
        }

        // Check Redis for searching status
        var status = await _statusStore.GetStatusAsync(query.PlayerId, ct);
        if (status is { State: PlayerMatchState.Searching })
        {
            return new QueueStatusResult(QueueStatusType.Searching);
        }

        return new QueueStatusResult(QueueStatusType.NotQueued);
    }
}
