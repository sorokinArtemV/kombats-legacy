using System.Diagnostics;
using Kombats.Abstractions;
using Kombats.Matchmaking.Application.Abstractions;
using Kombats.Matchmaking.Domain;
using Microsoft.Extensions.Logging;

namespace Kombats.Matchmaking.Application.UseCases.ExecuteMatchmakingTick;

internal sealed class ExecuteMatchmakingTickHandler
    : ICommandHandler<ExecuteMatchmakingTickCommand, MatchmakingTickResult>
{
    // Registered for OTLP export by KombatsObservabilityExtensions
    // (AddSource("Kombats.matchmaking") via meterName). Listener-less calls
    // are no-ops; instrumentation overhead is sub-microsecond per pair.
    private static readonly ActivitySource _activitySource = new("Kombats.matchmaking");

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
        using var tickActivity = _activitySource.StartActivity("matchmaking.pair.tick");

        var maxPairs = cmd.MaxPairsPerTick > 0 ? cmd.MaxPairsPerTick : 1;
        var stopwatch = Stopwatch.StartNew();
        var pairsCreated = 0;

        while (true)
        {
            // Graceful exit: if the lease has been lost or the host is shutting down,
            // stop before issuing another pop. Mid-await cancellation still surfaces as
            // OperationCanceledException through the threaded token and is handled by
            // MatchmakingLeaseService.
            if (ct.IsCancellationRequested)
            {
                break;
            }

            if (pairsCreated >= maxPairs)
            {
                break;
            }

            if (stopwatch.ElapsedMilliseconds >= MatchmakingTickBudget.SoftDeadlineMs)
            {
                break;
            }

            (Guid, Guid)? pair;
            using (var a = _activitySource.StartActivity("matchmaking.pair.try-pop"))
            {
                pair = await _queueStore.TryPopPairAsync(cmd.Variant, ct);
            }

            // null = queue empty OR single player left (the Lua script requeues the lone
            // player to the tail itself — recon Q1). Either way, this tick is done.
            if (pair == null)
            {
                break;
            }

            var (playerAId, playerBId) = pair.Value;

            PlayerCombatProfile? profileA;
            using (var a = _activitySource.StartActivity("matchmaking.pair.profile-lookup-1"))
            {
                profileA = await _profileRepository.GetByIdentityIdAsync(playerAId, ct);
            }
            PlayerCombatProfile? profileB;
            using (var a = _activitySource.StartActivity("matchmaking.pair.profile-lookup-2"))
            {
                profileB = await _profileRepository.GetByIdentityIdAsync(playerBId, ct);
            }

            if (profileA == null || profileB == null)
            {
                _logger.LogError(
                    "Combat profile missing for matched players. PlayerA={PlayerAId} (found={AFound}), PlayerB={PlayerBId} (found={BFound}). Re-queuing both players.",
                    playerAId, profileA != null, playerBId, profileB != null);

                // Restore both players to queue head so they are not silently lost (EI-014).
                await _queueStore.TryRequeueAsync(cmd.Variant, playerAId, ct);
                await _queueStore.TryRequeueAsync(cmd.Variant, playerBId, ct);

                continue;
            }

            var matchId = Guid.NewGuid();
            var battleId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;

            var match = Match.Create(matchId, battleId, playerAId, playerBId, cmd.Variant, now);
            match.MarkBattleCreateRequested(now);

            _matchRepository.Add(match);

            var request = new CreateBattleRequest(
                battleId,
                matchId,
                now,
                ToSnapshot(profileA),
                ToSnapshot(profileB));

            using (var a = _activitySource.StartActivity("matchmaking.pair.publish-create-battle"))
            {
                await _battlePublisher.PublishAsync(request, ct);
            }

            using (var a = _activitySource.StartActivity("matchmaking.pair.save-changes"))
            {
                await _unitOfWork.SaveChangesAsync(ct);
            }

            using (var a = _activitySource.StartActivity("matchmaking.pair.set-matched-1"))
            {
                await _statusStore.SetMatchedAsync(playerAId, matchId, battleId, cmd.Variant, ct);
            }
            using (var a = _activitySource.StartActivity("matchmaking.pair.set-matched-2"))
            {
                await _statusStore.SetMatchedAsync(playerBId, matchId, battleId, cmd.Variant, ct);
            }

            pairsCreated++;

            _logger.LogInformation(
                "Match created: MatchId={MatchId}, BattleId={BattleId}, PlayerA={PlayerAId}, PlayerB={PlayerBId}",
                matchId, battleId, playerAId, playerBId);
        }

        return new MatchmakingTickResult(pairsCreated);
    }

    private static ParticipantSnapshot ToSnapshot(PlayerCombatProfile p) =>
        new(p.IdentityId, p.CharacterId, p.Name, p.Level, p.Strength, p.Agility, p.Intuition, p.Vitality);
}
