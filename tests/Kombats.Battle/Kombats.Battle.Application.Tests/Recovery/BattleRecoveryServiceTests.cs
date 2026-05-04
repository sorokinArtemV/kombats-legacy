using FluentAssertions;
using Kombats.Battle.Application.Ports;
using Kombats.Battle.Application.ReadModels;
using Kombats.Battle.Application.UseCases.Recovery;
using Kombats.Battle.Application.UseCases.Turns;
using Kombats.Battle.Domain.Engine;
using Kombats.Battle.Domain.Model;
using Kombats.Battle.Domain.Results;
using Kombats.Battle.Domain.Rules;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Kombats.Battle.Application.Tests.Recovery;

public class BattleRecoveryServiceTests
{
    private readonly IBattleRecoveryRepository _recoveryRepo = Substitute.For<IBattleRecoveryRepository>();
    private readonly IBattleStateStore _stateStore = Substitute.For<IBattleStateStore>();
    private readonly IBattleEventPublisher _eventPublisher = Substitute.For<IBattleEventPublisher>();
    private readonly BattleRecoveryService _service;

    // BattleTurnAppService with mocked dependencies (needed for stuck-in-Resolving recovery)
    private readonly IBattleEngine _engine = Substitute.For<IBattleEngine>();
    private readonly IBattleRealtimeNotifier _notifier = Substitute.For<IBattleRealtimeNotifier>();
    private readonly IActionIntake _actionIntake = Substitute.For<IActionIntake>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly BattleTurnAppService _turnAppService;

    private readonly Guid _battleId = Guid.NewGuid();
    private readonly Guid _playerAId = Guid.NewGuid();
    private readonly Guid _playerBId = Guid.NewGuid();
    private readonly Guid _matchId = Guid.NewGuid();

    private static readonly CombatBalance Balance = new(
        hp: new HpBalance(50, 10),
        damage: new DamageBalance(5, 1.0m, 0.3m, 0.2m, 0.8m, 1.2m),
        mf: new MfBalance(2, 2),
        dodgeChance: new ChanceBalance(0.10m, 0.01m, 0.40m, 0.30m, 50m),
        critChance: new ChanceBalance(0.10m, 0.01m, 0.40m, 0.30m, 50m),
        critEffect: new CritEffectBalance(CritEffectMode.Multiplier, 1.5m, 0.5m));

    public BattleRecoveryServiceTests()
    {
        _clock.UtcNow.Returns(DateTimeOffset.UtcNow);

        _turnAppService = new BattleTurnAppService(
            _stateStore, _engine, _notifier, _eventPublisher,
            Substitute.For<IBattleUnitOfWork>(),
            _actionIntake,
            Substitute.For<IBattleTurnHistoryStore>(),
            _clock,
            Substitute.For<ILogger<BattleTurnAppService>>());

        _service = new BattleRecoveryService(
            _recoveryRepo,
            _stateStore,
            _turnAppService,
            _eventPublisher,
            Substitute.For<ILogger<BattleRecoveryService>>());
    }

    private BattleSnapshot CreateSnapshot(
        BattlePhase phase = BattlePhase.TurnOpen,
        int turnIndex = 1,
        int lastResolved = 0,
        Guid? endWinnerPlayerId = null,
        EndBattleReason? endReason = null,
        int? endFinalTurnIndex = null,
        DateTimeOffset? endedAt = null)
    {
        return new BattleSnapshot
        {
            BattleId = _battleId,
            MatchId = _matchId,
            PlayerAId = _playerAId,
            PlayerBId = _playerBId,
            Phase = phase,
            TurnIndex = turnIndex,
            LastResolvedTurnIndex = lastResolved,
            DeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(30),
            Ruleset = Ruleset.Create(1, 30, 10, 42, Balance),
            PlayerAHp = 100,
            PlayerBHp = 100,
            NoActionStreakBoth = 0,
            Version = 1,
            EndWinnerPlayerId = endWinnerPlayerId,
            EndReason = endReason,
            EndFinalTurnIndex = endFinalTurnIndex,
            EndedAt = endedAt
        };
    }

    // ========== Orphaned battle (no Redis state) gets force-ended ==========

    [Fact]
    public async Task RecoverBattle_OrphanedNoRedisState_ForceEndsAndPublishesBattleCompleted()
    {
        // Arrange: no Redis state, repository returns battle info
        _stateStore.GetStateAsync(_battleId, Arg.Any<CancellationToken>())
            .Returns((BattleSnapshot?)null);

        var orphanInfo = new OrphanedBattleInfo(_matchId, _playerAId, _playerBId, DateTimeOffset.UtcNow.AddMinutes(-5));
        _recoveryRepo.TryMarkOrphanedBattleEndedAsync(_battleId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(orphanInfo);

        // Act
        await _service.RecoverBattleAsync(_battleId, CancellationToken.None);

        // Assert: battle marked ended, event published, then committed atomically
        await _recoveryRepo.Received(1).TryMarkOrphanedBattleEndedAsync(
            _battleId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());

        await _eventPublisher.Received(1).PublishBattleCompletedAsync(
            _battleId, _matchId, _playerAId, _playerBId,
            EndBattleReason.SystemError, null,
            Arg.Any<DateTimeOffset>(),
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(),
            Arg.Any<CancellationToken>());

        await _recoveryRepo.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecoverBattle_OrphanedNoRedisState_PublishesBeforeCommit()
    {
        // Verify atomicity: publish (outbox add) must happen BEFORE commit (SaveChanges)
        _stateStore.GetStateAsync(_battleId, Arg.Any<CancellationToken>())
            .Returns((BattleSnapshot?)null);

        var orphanInfo = new OrphanedBattleInfo(_matchId, _playerAId, _playerBId, DateTimeOffset.UtcNow.AddMinutes(-5));
        _recoveryRepo.TryMarkOrphanedBattleEndedAsync(_battleId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(orphanInfo);

        var callOrder = new List<string>();
        _eventPublisher.PublishBattleCompletedAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(),
                Arg.Any<EndBattleReason>(), Arg.Any<Guid?>(), Arg.Any<DateTimeOffset>(),
                Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(_ => callOrder.Add("publish"));

        _recoveryRepo.CommitAsync(Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(_ => callOrder.Add("commit"));

        // Act
        await _service.RecoverBattleAsync(_battleId, CancellationToken.None);

        // Assert: publish added to outbox before commit persists both
        callOrder.Should().ContainInOrder("publish", "commit");
    }

    // ========== Battle in Resolving phase triggers recovery ==========

    [Fact]
    public async Task RecoverBattle_StuckInResolving_TriggersResolveTurn()
    {
        // Arrange: Redis shows Resolving
        var resolvingSnapshot = CreateSnapshot(phase: BattlePhase.Resolving, turnIndex: 1);
        _stateStore.GetStateAsync(_battleId, Arg.Any<CancellationToken>())
            .Returns(resolvingSnapshot, (BattleSnapshot?)null);

        // Act
        await _service.RecoverBattleAsync(_battleId, CancellationToken.None);

        // Assert: no force-end, no commit — resolution path is used instead
        await _recoveryRepo.DidNotReceive().TryMarkOrphanedBattleEndedAsync(
            Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await _recoveryRepo.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());

        // TryMarkTurnResolvingAsync should NOT be called (recovery skips CAS)
        await _stateStore.DidNotReceive().TryMarkTurnResolvingAsync(
            Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    // ========== Battle with Redis state Ended triggers recovery ==========

    [Fact]
    public async Task RecoverBattle_RedisStateEnded_WithEnrichedOutcome_PublishesRealWinnerAndReason()
    {
        // Arrange: Redis shows Ended AND carries the enriched terminal outcome
        // (winner, real reason, final turn, ended-at). This is the post-Option-B state
        // that closes the semantic-loss window: a battle that Redis resolved as a normal
        // win must be republished as a normal win, not as a data-less draw.
        var occurredAt = DateTimeOffset.UtcNow.AddSeconds(-2);
        var endedSnapshot = CreateSnapshot(
            phase: BattlePhase.Ended,
            lastResolved: 3,
            endWinnerPlayerId: _playerAId,
            endReason: EndBattleReason.Normal,
            endFinalTurnIndex: 3,
            endedAt: occurredAt);
        _stateStore.GetStateAsync(_battleId, Arg.Any<CancellationToken>())
            .Returns(endedSnapshot);

        var orphanInfo = new OrphanedBattleInfo(_matchId, _playerAId, _playerBId, DateTimeOffset.UtcNow.AddMinutes(-5));
        _recoveryRepo.TryMarkOrphanedBattleEndedAsync(_battleId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(orphanInfo);

        // Act
        await _service.RecoverBattleAsync(_battleId, CancellationToken.None);

        // Assert: battle marked ended, event published with REAL outcome, then committed
        await _eventPublisher.Received(1).PublishBattleCompletedAsync(
            _battleId, _matchId, _playerAId, _playerBId,
            EndBattleReason.Normal, _playerAId,
            occurredAt,
            3, Arg.Any<int>(), 1,
            Arg.Any<CancellationToken>());

        // And explicitly NOT the data-less SystemError/null fallback
        await _eventPublisher.DidNotReceive().PublishBattleCompletedAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(),
            EndBattleReason.SystemError, Arg.Any<Guid?>(), Arg.Any<DateTimeOffset>(),
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(),
            Arg.Any<CancellationToken>());

        await _recoveryRepo.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecoverBattle_RedisStateEnded_WithoutEnrichedOutcome_FallsBackToSystemError()
    {
        // Arrange: Redis shows Ended but has no enriched terminal outcome (pre-Option-B
        // battle, or an edge case where the fields are missing). The recovery path must
        // still function and must fall back to the data-less SystemError publication.
        var endedSnapshot = CreateSnapshot(phase: BattlePhase.Ended, lastResolved: 3);
        // No EndReason / EndWinnerPlayerId / EndFinalTurnIndex / EndedAt set
        _stateStore.GetStateAsync(_battleId, Arg.Any<CancellationToken>())
            .Returns(endedSnapshot);

        var orphanInfo = new OrphanedBattleInfo(_matchId, _playerAId, _playerBId, DateTimeOffset.UtcNow.AddMinutes(-5));
        _recoveryRepo.TryMarkOrphanedBattleEndedAsync(_battleId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(orphanInfo);

        // Act
        await _service.RecoverBattleAsync(_battleId, CancellationToken.None);

        // Assert: falls back to SystemError / null winner (previous behavior preserved)
        await _eventPublisher.Received(1).PublishBattleCompletedAsync(
            _battleId, _matchId, _playerAId, _playerBId,
            EndBattleReason.SystemError, null,
            Arg.Any<DateTimeOffset>(),
            3, Arg.Any<int>(), 1,
            Arg.Any<CancellationToken>());

        await _recoveryRepo.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecoverBattle_RedisStateEnded_AlreadyEndedInPostgres_NoOp()
    {
        // Arrange: Redis shows Ended, but Postgres already shows Ended (race with projection)
        var endedSnapshot = CreateSnapshot(phase: BattlePhase.Ended);
        _stateStore.GetStateAsync(_battleId, Arg.Any<CancellationToken>())
            .Returns(endedSnapshot);

        _recoveryRepo.TryMarkOrphanedBattleEndedAsync(_battleId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns((OrphanedBattleInfo?)null);

        // Act
        await _service.RecoverBattleAsync(_battleId, CancellationToken.None);

        // Assert: no event published, no commit
        await _eventPublisher.DidNotReceive().PublishBattleCompletedAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(),
            Arg.Any<EndBattleReason>(), Arg.Any<Guid?>(), Arg.Any<DateTimeOffset>(),
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(),
            Arg.Any<CancellationToken>());
        await _recoveryRepo.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    // ========== Active battle (TurnOpen) is not force-ended ==========

    [Fact]
    public async Task RecoverBattle_TurnOpenInRedis_DoesNotForceEnd()
    {
        // Arrange: Redis shows TurnOpen — battle is active, just old
        var activeSnapshot = CreateSnapshot(phase: BattlePhase.TurnOpen);
        _stateStore.GetStateAsync(_battleId, Arg.Any<CancellationToken>())
            .Returns(activeSnapshot);

        // Act
        await _service.RecoverBattleAsync(_battleId, CancellationToken.None);

        // Assert: no force-end, no resolution attempt
        await _recoveryRepo.DidNotReceive().TryMarkOrphanedBattleEndedAsync(
            Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await _recoveryRepo.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    // ========== Idempotency: already-ended battle is safe ==========

    [Fact]
    public async Task ForceEndOrphanedBattle_AlreadyEnded_NoOpNoPublish()
    {
        // Arrange: no Redis state, but repository says battle already ended
        _stateStore.GetStateAsync(_battleId, Arg.Any<CancellationToken>())
            .Returns((BattleSnapshot?)null);

        _recoveryRepo.TryMarkOrphanedBattleEndedAsync(_battleId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns((OrphanedBattleInfo?)null);

        // Act
        await _service.RecoverBattleAsync(_battleId, CancellationToken.None);

        // Assert: no event published, no commit
        await _eventPublisher.DidNotReceive().PublishBattleCompletedAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(),
            Arg.Any<EndBattleReason>(), Arg.Any<Guid?>(), Arg.Any<DateTimeOffset>(),
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(),
            Arg.Any<CancellationToken>());
        await _recoveryRepo.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    // ========== Scan returns no stale battles → no processing ==========

    [Fact]
    public async Task ScanAndRecover_NoBattles_ReturnsZero()
    {
        _recoveryRepo.GetNonTerminalBattleIdsOlderThanAsync(
                Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Guid>());

        var result = await _service.ScanAndRecoverAsync(
            DateTimeOffset.UtcNow.AddMinutes(-10), 50, CancellationToken.None);

        result.Should().Be(0);
    }

    // ========== ArenaOpen is not force-ended ==========

    [Fact]
    public async Task RecoverBattle_ArenaOpenInRedis_DoesNotForceEnd()
    {
        var arenaOpenSnapshot = CreateSnapshot(phase: BattlePhase.ArenaOpen);
        _stateStore.GetStateAsync(_battleId, Arg.Any<CancellationToken>())
            .Returns(arenaOpenSnapshot);

        await _service.RecoverBattleAsync(_battleId, CancellationToken.None);

        await _recoveryRepo.DidNotReceive().TryMarkOrphanedBattleEndedAsync(
            Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await _recoveryRepo.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }
}
