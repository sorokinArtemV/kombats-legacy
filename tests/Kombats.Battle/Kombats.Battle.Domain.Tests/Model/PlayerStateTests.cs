using FluentAssertions;
using Kombats.Battle.Domain.Model;
using Xunit;

namespace Kombats.Battle.Domain.Tests.Model;

public class PlayerStateTests
{
    private static PlayerState CreatePlayer(int maxHp = 100, int currentHp = 100)
        => new(Guid.NewGuid(), maxHp, currentHp, new PlayerStats(10, 10, 10, 10));

    [Fact]
    public void Constructor_TwoParam_SetsCurrentHpToMax()
    {
        var player = new PlayerState(Guid.NewGuid(), 100, new PlayerStats(5, 5, 5, 5));
        player.CurrentHp.Should().Be(100);
        player.MaxHp.Should().Be(100);
        player.IsAlive.Should().BeTrue();
    }

    [Fact]
    public void Constructor_ThreeParam_SetsCurrentHpExplicitly()
    {
        var player = new PlayerState(Guid.NewGuid(), 100, 50, new PlayerStats(5, 5, 5, 5));
        player.CurrentHp.Should().Be(50);
    }

    [Fact]
    public void ApplyDamage_ReducesHp()
    {
        var player = CreatePlayer(100, 80);
        player.ApplyDamage(30);
        player.CurrentHp.Should().Be(50);
    }

    [Fact]
    public void ApplyDamage_ClampsToZero()
    {
        var player = CreatePlayer(100, 20);
        player.ApplyDamage(50);
        player.CurrentHp.Should().Be(0);
        player.IsDead.Should().BeTrue();
    }

    [Fact]
    public void ApplyDamage_NegativeThrows()
    {
        var player = CreatePlayer();
        var act = () => player.ApplyDamage(-1);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Heal_IncreasesHp()
    {
        var player = CreatePlayer(100, 50);
        player.Heal(20);
        player.CurrentHp.Should().Be(70);
    }

    [Fact]
    public void Heal_ClampsToMax()
    {
        var player = CreatePlayer(100, 90);
        player.Heal(50);
        player.CurrentHp.Should().Be(100);
    }

    [Fact]
    public void Heal_NegativeThrows()
    {
        var player = CreatePlayer();
        var act = () => player.Heal(-1);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void IsAlive_TrueWhenHpAboveZero()
    {
        var player = CreatePlayer(100, 1);
        player.IsAlive.Should().BeTrue();
        player.IsDead.Should().BeFalse();
    }

    [Fact]
    public void IsDead_TrueWhenHpZero()
    {
        var player = CreatePlayer(100, 0);
        player.IsDead.Should().BeTrue();
        player.IsAlive.Should().BeFalse();
    }
}
