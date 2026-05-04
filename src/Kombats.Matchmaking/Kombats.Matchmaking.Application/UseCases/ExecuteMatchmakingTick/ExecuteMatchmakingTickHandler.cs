using Kombats.Abstractions;
using Kombats.Matchmaking.Application.Abstractions;
using Kombats.Matchmaking.Domain;
using Microsoft.Extensions.Logging;

namespace Kombats.Matchmaking.Application.UseCases.ExecuteMatchmakingTick;

internal sealed class ExecuteMatchmakingTickHandler
    : ICommandHandler<ExecuteMatchmakingTickCommand, MatchmakingTickResult>
{
    private readonly IMatchQueueStore _queueStore;
    private readonly IMatchRepository _matchRepository;
    private readonly IPlayerCombatProfileRepository _profileRepository;
    private readonly ICreateBattlePublisher _battlePublisher;
    private readonly IPlayerMatchStatusStore _statusStore;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ExecuteMatchmakingTickHandler> _logger;

    public ExecuteMatchmakingTickHandler(
        IMatchQueueStore queueStore,
        IMatchRepository matchRepository,
        IPlayerCombatProfileRepository profileRepository,
        ICreateBattlePublisher battlePublisher,
        IPlayerMatchStatusStore statusStore,
        IUnitOfWork unitOfWork,
        ILogger<ExecuteMatchmakingTickHandler> logger)
    {
        _queueStore = queueStore;
        _matchRepository = matchRepository;
        _profileRepository = profileRepository;
        _battlePublisher = battlePublisher;
        _statusStore = statusStore;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<MatchmakingTickResult>> HandleAsync(
        ExecuteMatchmakingTickCommand cmd, CancellationToken ct)
    {
        // Atomically pop a pair from Redis queue
        var pair = await _queueStore.TryPopPairAsync(cmd.Variant, ct);
        if (pair == null)
        {
            return new MatchmakingTickResult(false);
        }

        var (playerAId, playerBId) = pair.Value;

        // Fetch combat profiles from local projection
        var profileA = await _profileRepository.GetByIdentityIdAsync(playerAId, ct);
        var profileB = await _profileRepository.GetByIdentityIdAsync(playerBId, ct);

        if (profileA == null || profileB == null)
        {
            _logger.LogError(
                "Combat profile missing for matched players. PlayerA={PlayerAId} (found={AFound}), PlayerB={PlayerBId} (found={BFound}). Re-queuing both players.",
                playerAId, profileA != null, playerBId, profileB != null);

            // Restore both players to queue head so they are not silently lost (EI-014)
            await _queueStore.TryRequeueAsync(cmd.Variant, playerAId, ct);
            await _queueStore.TryRequeueAsync(cmd.Variant, playerBId, ct);

            return new MatchmakingTickResult(false);
        }

        var matchId = Guid.NewGuid();
        var battleId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        // Create domain match and advance to BattleCreateRequested
        var match = Match.Create(matchId, battleId, playerAId, playerBId, cmd.Variant, now);
        match.MarkBattleCreateRequested(now);

        // Persist match
        _matchRepository.Add(match);

        // Publish CreateBattle via MassTransit outbox (atomic with SaveChanges)
        var request = new CreateBattleRequest(
            battleId,
            matchId,
            now,
            ToSnapshot(profileA),
            ToSnapshot(profileB));

        await _battlePublisher.PublishAsync(request, ct);

        // Atomic save: match insert + outbox message
        await _unitOfWork.SaveChangesAsync(ct);

        // Update Redis status for both players to Matched
        await _statusStore.SetMatchedAsync(playerAId, matchId, battleId, cmd.Variant, ct);
        await _statusStore.SetMatchedAsync(playerBId, matchId, battleId, cmd.Variant, ct);

        _logger.LogInformation(
            "Match created: MatchId={MatchId}, BattleId={BattleId}, PlayerA={PlayerAId}, PlayerB={PlayerBId}",
            matchId, battleId, playerAId, playerBId);

        return new MatchmakingTickResult(true, matchId, battleId);
    }

    private static ParticipantSnapshot ToSnapshot(PlayerCombatProfile p) =>
        new(p.IdentityId, p.CharacterId, p.Name, p.Level, p.Strength, p.Agility, p.Intuition, p.Vitality);
}
