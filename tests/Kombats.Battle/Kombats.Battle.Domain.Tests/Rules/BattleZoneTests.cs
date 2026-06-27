using FluentAssertions;
using Kombats.Battle.Domain.Rules;
using Xunit;

namespace Kombats.Battle.Domain.Tests.Rules;

public class BattleZoneTests
{
    [Theory]
    [InlineData(BattleZone.Head, BattleZone.Chest, true)]
    [InlineData(BattleZone.Chest, BattleZone.Belly, true)]
    [InlineData(BattleZone.Belly, BattleZone.Waist, true)]
    [InlineData(BattleZone.Waist, BattleZone.Legs, true)]
    [InlineData(BattleZone.Legs, BattleZone.Head, true)]   // ring wraps
    [InlineData(BattleZone.Chest, BattleZone.Head, true)]   // reversed valid
    [InlineData(BattleZone.Head, BattleZone.Belly, false)]  // not adjacent
    [InlineData(BattleZone.Head, BattleZone.Waist, false)]
    [InlineData(BattleZone.Head, BattleZone.Legs, true)]    // ring wraps reversed
    [InlineData(BattleZone.Chest, BattleZone.Waist, false)]
    [InlineData(BattleZone.Chest, BattleZone.Legs, false)]
    [InlineData(BattleZone.Belly, BattleZone.Legs, false)]
    [InlineData(BattleZone.Head, BattleZone.Head, false)]   // same zone
    public void IsValidBlockPattern_ReturnsExpected(BattleZone z1, BattleZone z2, bool expected)
    {
        BattleZoneHelper.IsValidBlockPattern(z1, z2).Should().Be(expected);
    }

    [Theory]
    [InlineData(BattleZone.Head, BattleZone.Head, BattleZone.Chest, true)]
    [InlineData(BattleZone.Chest, BattleZone.Head, BattleZone.Chest, true)]
    [InlineData(BattleZone.Belly, BattleZone.Head, BattleZone.Chest, false)]
    [InlineData(BattleZone.Head, null, null, false)]
    public void IsZoneBlocked_ReturnsExpected(BattleZone attack, BattleZone? b1, BattleZone? b2, bool expected)
    {
        BattleZoneHelper.IsZoneBlocked(attack, b1, b2).Should().Be(expected);
    }

    [Fact]
    public void GetValidBlockPatterns_ReturnsFivePairs()
    {
        BattleZoneHelper.GetValidBlockPatterns().Should().HaveCount(5);
    }
}
