using FluentAssertions;
using Kombats.Battle.Domain.Model;
using Xunit;

namespace Kombats.Battle.Domain.Tests.Model;

public class PlayerStatsTests
{
    [Fact]
    public void Create_WithValidValues_SetsProperties()
    {
        var stats = new PlayerStats(10, 20, 15, 12);

        stats.Strength.Should().Be(10);
        stats.Stamina.Should().Be(20);
        stats.Agility.Should().Be(15);
        stats.Intuition.Should().Be(12);
    }

    [Fact]
    public void Create_WithZeroValues_Succeeds()
    {
        var stats = new PlayerStats(0, 0, 0, 0);
        stats.Strength.Should().Be(0);
    }

    [Theory]
    [InlineData(-1, 0, 0, 0)]
    [InlineData(0, -1, 0, 0)]
    [InlineData(0, 0, -1, 0)]
    [InlineData(0, 0, 0, -1)]
    public void Create_WithNegativeValues_Throws(int str, int sta, int agi, int intu)
    {
        var act = () => new PlayerStats(str, sta, agi, intu);
        act.Should().Throw<ArgumentException>();
    }
}
