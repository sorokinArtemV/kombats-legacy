using FluentAssertions;
using Kombats.Battle.Application.Models;
using Kombats.Battle.Application.Ports;
using Kombats.Battle.Application.ReadModels;
using Kombats.Battle.Application.UseCases.Lifecycle;
using Kombats.Battle.Domain.Model;
using Kombats.Battle.Domain.Rules;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Kombats.Battle.Application.Tests.Lifecycle;

public class BattleLifecycleAppServiceTests
{
    private readonly IBattleStateStore _stateStore = Substitute.For<IBattleStateStore>();
    private readonly IBattleRealtimeNotifier _notifier = Substitute.For<IBattleRealtimeNotifier>();
    private readonly IRulesetProvider _rulesetProvider = Substitute.For<IRulesetProvider>();
    private readonly ISeedGenerator _seedGenerator = Substitute.For<ISeedGenerator>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly BattleLifecycleAppService _service;

    private static readonly CombatBalance DefaultBalance = new(
        hp: new HpBalance(50, 10),
        damage: new DamageBalance(5, 1.0m, 0.3m, 0.2m, 0.8m, 1.2m),
        mf: new MfBalance(2, 2),
        dodgeChance: new ChanceBalance(0.10m, 0.01m, 0.40m, 0.30m, 50m),
        critChance: new ChanceBalance(0.10m, 0.01m, 0.40m, 0.30m, 50m),
        critEffect: new CritEffectBalance(CritEffectMode.Multiplier, 1.5m, 0.5m));

    public BattleLifecycleAppServiceTests()
    {
        _clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        _rulesetProvider.GetCurrentRuleset().Returns(
            new RulesetWithoutSeed(1, 30, 10, DefaultBalance));
        _seedGenerator.GenerateSeed().Returns(42);

        _service = new BattleLifecycleAppService(
            _stateStore, _notifier, _rulesetProvider,
            _seedGenerator, _clock,
            Substitute.For<ILogger<BattleLifecycleAppService>>());
    }

    [Fact]
    public async Task HandleBattleCreated_InitializesStateAndOpensTurn()
    {
        var battleId = Guid.NewGuid();
        var matchId = Guid.NewGuid();
        var playerAId = Guid.NewGuid();
        var playerBId = Guid.NewGuid();
        var profileA = new CombatProfile(playerAId, 10, 10, 10, 10);
        var profileB = new CombatProfile(playerBId, 10, 10, 10, 10);

        _stateStore.TryInitializeBattleAsync(Arg.Any<Guid>(), Arg.Any<BattleDomainState>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _stateStore.TryOpenTurnAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await _service.HandleBattleCreatedAsync(
            battleId, matchId, playerAId, playerBId, profileA, profileB, "PlayerA", "PlayerB");

        result.Should().NotBeNull();
        result!.RulesetVersion.Should().Be(1);
        result.Seed.Should().Be(42);

        await _stateStore.Received(1).TryInitializeBattleAsync(battleId, Arg.Any<BattleDomainState>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<CancellationToken>());
        await _stateStore.Received(1).TryOpenTurnAsync(battleId, 1, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleBattleCreated_WhenTurnAlreadyOpen_DoesNotNotify()
    {
        var battleId = Guid.NewGuid();
        var profileA = new CombatProfile(Guid.NewGuid(), 10, 10, 10, 10);
        var profileB = new CombatProfile(Guid.NewGuid(), 10, 10, 10, 10);

        _stateStore.TryOpenTurnAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(false); // Turn already open (convergent idempotency)

        await _service.HandleBattleCreatedAsync(
            battleId, Guid.NewGuid(), profileA.PlayerId, profileB.PlayerId, profileA, profileB, null, null);

        await _notifier.DidNotReceive().NotifyBattleReadyAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await _notifier.DidNotReceive().NotifyTurnOpenedAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleBattleCreated_WhenTurnOpened_NotifiesBoth()
    {
        var battleId = Guid.NewGuid();
        var playerAId = Guid.NewGuid();
        var playerBId = Guid.NewGuid();
        var profileA = new CombatProfile(playerAId, 10, 10, 10, 10);
        var profileB = new CombatProfile(playerBId, 10, 10, 10, 10);

        _stateStore.TryOpenTurnAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(true);

        await _service.HandleBattleCreatedAsync(
            battleId, Guid.NewGuid(), playerAId, playerBId, profileA, profileB, "PlayerA", "PlayerB");

        await _notifier.Received(1).NotifyBattleReadyAsync(battleId, playerAId, playerBId, "PlayerA", "PlayerB", Arg.Any<CancellationToken>());
        await _notifier.Received(1).NotifyTurnOpenedAsync(battleId, 1, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleBattleCreated_RulesetFailure_ReturnsNull()
    {
        _rulesetProvider.GetCurrentRuleset().Returns(_ => throw new InvalidOperationException("No ruleset"));

        var result = await _service.HandleBattleCreatedAsync(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new CombatProfile(Guid.NewGuid(), 10, 10, 10, 10),
            new CombatProfile(Guid.NewGuid(), 10, 10, 10, 10),
            null, null);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetBattleSnapshot_ValidPlayer_ReturnsSnapshot()
    {
        var battleId = Guid.NewGuid();
        var playerId = Guid.NewGuid();
        var snapshot = new BattleSnapshot
        {
            BattleId = battleId,
            PlayerAId = playerId,
            PlayerBId = Guid.NewGuid()
        };

        _stateStore.GetStateAsync(battleId, Arg.Any<CancellationToken>()).Returns(snapshot);

        var result = await _service.GetBattleSnapshotForPlayerAsync(battleId, playerId);
        result.Should().NotBeNull();
        result!.BattleId.Should().Be(battleId);
    }

    [Fact]
    public async Task GetBattleSnapshot_NonParticipant_Throws()
    {
        var battleId = Guid.NewGuid();
        var snapshot = new BattleSnapshot
        {
            BattleId = battleId,
            PlayerAId = Guid.NewGuid(),
            PlayerBId = Guid.NewGuid()
        };

        _stateStore.GetStateAsync(battleId, Arg.Any<CancellationToken>()).Returns(snapshot);

        var act = () => _service.GetBattleSnapshotForPlayerAsync(battleId, Guid.NewGuid());
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task GetBattleSnapshot_BattleNotFound_ReturnsNull()
    {
        _stateStore.GetStateAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((BattleSnapshot?)null);

        var result = await _service.GetBattleSnapshotForPlayerAsync(Guid.NewGuid(), Guid.NewGuid());
        result.Should().BeNull();
    }
}
