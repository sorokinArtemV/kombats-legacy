using FluentAssertions;
using Kombats.Battle.Domain.Model;
using Kombats.Battle.Domain.Rules;
using Xunit;

namespace Kombats.Battle.Domain.Tests.Model;

public class PlayerActionTests
{
    private static readonly Guid PlayerId = Guid.NewGuid();

    [Fact]
    public void NoAction_CreatesNoActionWithNullZones()
    {
        var action = PlayerAction.NoAction(PlayerId, 1);

        action.IsNoAction.Should().BeTrue();
        action.AttackZone.Should().BeNull();
        action.BlockZonePrimary.Should().BeNull();
        action.BlockZoneSecondary.Should().BeNull();
        action.PlayerId.Should().Be(PlayerId);
        action.TurnIndex.Should().Be(1);
    }

    [Fact]
    public void Create_WithValidZones_CreatesAction()
    {
        var action = PlayerAction.Create(PlayerId, 1, BattleZone.Head, BattleZone.Chest, BattleZone.Belly);

        action.IsNoAction.Should().BeFalse();
        action.AttackZone.Should().Be(BattleZone.Head);
        action.BlockZonePrimary.Should().Be(BattleZone.Chest);
        action.BlockZoneSecondary.Should().Be(BattleZone.Belly);
    }

    [Fact]
    public void Create_WithNullAttackZone_ReturnsNoAction()
    {
        var action = PlayerAction.Create(PlayerId, 1, null, BattleZone.Chest, BattleZone.Belly);
        action.IsNoAction.Should().BeTrue();
    }

    [Fact]
    public void Create_WithInvalidBlockPattern_ReturnsNoAction()
    {
        // Head and Waist are not adjacent in ring topology
        var action = PlayerAction.Create(PlayerId, 1, BattleZone.Head, BattleZone.Head, BattleZone.Waist);
        action.IsNoAction.Should().BeTrue();
    }

    [Theory]
    [InlineData(BattleZone.Head, BattleZone.Chest)]
    [InlineData(BattleZone.Chest, BattleZone.Belly)]
    [InlineData(BattleZone.Belly, BattleZone.Waist)]
    [InlineData(BattleZone.Waist, BattleZone.Legs)]
    [InlineData(BattleZone.Legs, BattleZone.Head)]
    public void Create_WithAllValidBlockPatterns_Succeeds(BattleZone block1, BattleZone block2)
    {
        var action = PlayerAction.Create(PlayerId, 1, BattleZone.Belly, block1, block2);
        action.IsNoAction.Should().BeFalse();
    }

    [Fact]
    public void Create_WithReversedValidBlockPattern_Succeeds()
    {
        // Reversed order should also be valid
        var action = PlayerAction.Create(PlayerId, 1, BattleZone.Head, BattleZone.Belly, BattleZone.Chest);
        action.IsNoAction.Should().BeFalse();
    }
}
