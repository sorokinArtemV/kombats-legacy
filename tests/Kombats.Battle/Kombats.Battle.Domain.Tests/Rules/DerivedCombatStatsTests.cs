using FluentAssertions;
using Kombats.Battle.Domain.Rules;
using Xunit;

namespace Kombats.Battle.Domain.Tests.Rules;

public class DerivedCombatStatsTests
{
    [Fact]
    public void Constructor_ValidValues_SetsProperties()
    {
        var stats = new DerivedCombatStats(100, 10, 20, 5, 5, 3, 3);
        stats.HpMax.Should().Be(100);
        stats.DamageMin.Should().Be(10);
        stats.DamageMax.Should().Be(20);
    }

    [Fact]
    public void Constructor_NegativeHpMax_Throws()
    {
        var act = () => new DerivedCombatStats(-1, 10, 20, 5, 5, 3, 3);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_DamageMinGtMax_Throws()
    {
        var act = () => new DerivedCombatStats(100, 20, 10, 5, 5, 3, 3);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_NegativeMfDodge_Throws()
    {
        var act = () => new DerivedCombatStats(100, 10, 20, -1, 5, 3, 3);
        act.Should().Throw<ArgumentException>();
    }
}
