using FluentAssertions;
using Kombats.Battle.Domain.Rules;
using Xunit;

namespace Kombats.Battle.Domain.Tests.Rules;

public class CombatBalanceTests
{
    [Fact]
    public void HpBalance_NegativeBaseHp_Throws()
    {
        var act = () => new HpBalance(-1, 10);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void HpBalance_ZeroHpPerEnd_Throws()
    {
        var act = () => new HpBalance(50, 0);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void DamageBalance_SpreadMinGteMax_Throws()
    {
        var act = () => new DamageBalance(5, 1, 0.3m, 0.2m, 1.0m, 1.0m);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void MfBalance_ZeroMfPerAgi_Throws()
    {
        var act = () => new MfBalance(0, 2);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ChanceBalance_MinGtMax_Throws()
    {
        var act = () => new ChanceBalance(0.1m, 0.5m, 0.3m, 0.3m, 50m);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ChanceBalance_ZeroKBase_Throws()
    {
        var act = () => new ChanceBalance(0.1m, 0.01m, 0.4m, 0.3m, 0m);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CritEffectBalance_ZeroMultiplier_Throws()
    {
        var act = () => new CritEffectBalance(CritEffectMode.Multiplier, 0m, 0.5m);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CritEffectBalance_HybridBlockMultiplierOutOfRange_Throws()
    {
        var act = () => new CritEffectBalance(CritEffectMode.Hybrid, 1.5m, 1.5m);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CombatBalance_NullSubRecord_Throws()
    {
        var act = () => new CombatBalance(null!,
            new DamageBalance(5, 1, 0.3m, 0.2m, 0.8m, 1.2m),
            new MfBalance(2, 2),
            new ChanceBalance(0.1m, 0.01m, 0.4m, 0.3m, 50m),
            new ChanceBalance(0.1m, 0.01m, 0.4m, 0.3m, 50m),
            new CritEffectBalance(CritEffectMode.Multiplier, 1.5m, 0.5m));
        act.Should().Throw<ArgumentNullException>();
    }
}
