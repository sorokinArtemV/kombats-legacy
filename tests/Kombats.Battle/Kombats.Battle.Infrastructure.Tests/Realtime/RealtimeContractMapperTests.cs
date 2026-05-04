using FluentAssertions;
using Kombats.Battle.Application.ReadModels;
using Kombats.Battle.Domain.Model;
using Kombats.Battle.Domain.Rules;
using Kombats.Battle.Infrastructure.Realtime.SignalR;
using Kombats.Battle.Realtime.Contracts;
using Xunit;

namespace Kombats.Battle.Infrastructure.Tests.Realtime;

public class RealtimeContractMapperTests
{
    private static readonly CombatBalance Balance = new(
        hp: new HpBalance(50, 10),
        damage: new DamageBalance(5, 1.0m, 0.3m, 0.2m, 0.8m, 1.2m),
        mf: new MfBalance(2, 2),
        dodgeChance: new ChanceBalance(0.10m, 0.01m, 0.40m, 0.30m, 50m),
        critChance: new ChanceBalance(0.10m, 0.01m, 0.40m, 0.30m, 50m),
        critEffect: new CritEffectBalance(CritEffectMode.Multiplier, 1.5m, 0.5m));

    [Fact]
    public void ToRealtimeSnapshot_MapsNamesAndMaxHp()
    {
        var snapshot = new BattleSnapshot
        {
            BattleId = Guid.NewGuid(),
            MatchId = Guid.NewGuid(),
            PlayerAId = Guid.NewGuid(),
            PlayerBId = Guid.NewGuid(),
            Ruleset = Ruleset.Create(1, 30, 10, 42, Balance),
            Phase = BattlePhase.TurnOpen,
            TurnIndex = 1,
            DeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(30),
            NoActionStreakBoth = 0,
            LastResolvedTurnIndex = 0,
            Version = 1,
            PlayerAHp = 150,
            PlayerBHp = 130,
            PlayerAName = "Alice",
            PlayerBName = "Bob",
            PlayerAMaxHp = 150,
            PlayerBMaxHp = 130
        };

        var realtime = RealtimeContractMapper.ToRealtimeSnapshot(snapshot);

        realtime.PlayerAName.Should().Be("Alice");
        realtime.PlayerBName.Should().Be("Bob");
        realtime.PlayerAMaxHp.Should().Be(150);
        realtime.PlayerBMaxHp.Should().Be(130);
    }

    [Fact]
    public void ToRealtimeSnapshot_NullNames_MapsAsNull()
    {
        var snapshot = new BattleSnapshot
        {
            BattleId = Guid.NewGuid(),
            MatchId = Guid.NewGuid(),
            PlayerAId = Guid.NewGuid(),
            PlayerBId = Guid.NewGuid(),
            Ruleset = Ruleset.Create(1, 30, 10, 42, Balance),
            Phase = BattlePhase.TurnOpen,
            TurnIndex = 1,
            DeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(30),
            NoActionStreakBoth = 0,
            LastResolvedTurnIndex = 0,
            Version = 1,
            PlayerAHp = 100,
            PlayerBHp = 100,
            PlayerAName = null,
            PlayerBName = null,
            PlayerAMaxHp = null,
            PlayerBMaxHp = null
        };

        var realtime = RealtimeContractMapper.ToRealtimeSnapshot(snapshot);

        realtime.PlayerAName.Should().BeNull();
        realtime.PlayerBName.Should().BeNull();
        realtime.PlayerAMaxHp.Should().BeNull();
        realtime.PlayerBMaxHp.Should().BeNull();
    }
}
