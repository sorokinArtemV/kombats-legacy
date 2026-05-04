using FluentAssertions;
using Kombats.Battle.Domain.Model;
using Kombats.Battle.Domain.Rules;
using Xunit;

namespace Kombats.Battle.Domain.Tests.Rules;

public class CombatMathTests
{
    private static readonly CombatBalance Balance = TestHelpers.DefaultBalance;

    [Fact]
    public void ComputeDerived_HpCalculation()
    {
        // HP = BaseHp + Stamina * HpPerEnd = 50 + 20 * 10 = 250
        var stats = new PlayerStats(10, 20, 15, 12);
        var derived = CombatMath.ComputeDerived(stats, Balance);
        derived.HpMax.Should().Be(250);
    }

    [Fact]
    public void ComputeDerived_ZeroStamina_HpEqualsBase()
    {
        var stats = new PlayerStats(10, 0, 10, 10);
        var derived = CombatMath.ComputeDerived(stats, Balance);
        derived.HpMax.Should().Be(50); // BaseHp only
    }

    [Fact]
    public void ComputeDerived_DamageRange()
    {
        // BaseDamage = BaseWeaponDamage + Str*DamagePerStr + Agi*DamagePerAgi + Int*DamagePerInt
        //            = 5 + 10*1.0 + 15*0.3 + 12*0.2 = 5 + 10 + 4.5 + 2.4 = 21.9
        // DamageMin = floor(21.9 * 0.8) = floor(17.52) = 17
        // DamageMax = ceil(21.9 * 1.2)  = ceil(26.28)  = 27
        var stats = new PlayerStats(10, 20, 15, 12);
        var derived = CombatMath.ComputeDerived(stats, Balance);
        derived.DamageMin.Should().Be(17);
        derived.DamageMax.Should().Be(27);
    }

    [Fact]
    public void ComputeDerived_ModifierFactors()
    {
        // MfDodge = Agility * MfPerAgi = 15 * 2 = 30
        // MfAntiDodge = Agility * MfPerAgi = 15 * 2 = 30
        // MfCrit = Intuition * MfPerInt = 12 * 2 = 24
        // MfAntiCrit = Intuition * MfPerInt = 12 * 2 = 24
        var stats = new PlayerStats(10, 20, 15, 12);
        var derived = CombatMath.ComputeDerived(stats, Balance);
        derived.MfDodge.Should().Be(30);
        derived.MfAntiDodge.Should().Be(30);
        derived.MfCrit.Should().Be(24);
        derived.MfAntiCrit.Should().Be(24);
    }

    [Fact]
    public void ComputeChance_ZeroDiff_ReturnsBase()
    {
        var chance = CombatMath.ComputeChance(0, 0.10m, 0.01m, 0.40m, 0.30m, 50m);
        chance.Should().Be(0.10m);
    }

    [Fact]
    public void ComputeChance_PositiveDiff_IncreasesChance()
    {
        var chance = CombatMath.ComputeChance(50, 0.10m, 0.01m, 0.40m, 0.30m, 50m);
        chance.Should().BeGreaterThan(0.10m);
    }

    [Fact]
    public void ComputeChance_NegativeDiff_DecreasesChance()
    {
        var chance = CombatMath.ComputeChance(-50, 0.10m, 0.01m, 0.40m, 0.30m, 50m);
        chance.Should().BeLessThan(0.10m);
    }

    [Fact]
    public void ComputeChance_ApproachesMax()
    {
        // Very large positive diff should approach max
        var chance = CombatMath.ComputeChance(10000, 0.10m, 0.01m, 0.40m, 0.30m, 50m);
        chance.Should().BeGreaterThan(0.39m);
        chance.Should().BeLessThanOrEqualTo(0.40m);
    }

    [Fact]
    public void ComputeChance_ApproachesMin()
    {
        // Very large negative diff should approach min
        var chance = CombatMath.ComputeChance(-10000, 0.10m, 0.01m, 0.40m, 0.30m, 50m);
        chance.Should().BeLessThan(0.02m);
        chance.Should().BeGreaterThanOrEqualTo(0.01m);
    }

    [Fact]
    public void ComputeDodgeChance_EqualStats_ReturnsBase()
    {
        var attacker = new DerivedCombatStats(100, 10, 20, 30, 30, 20, 20);
        var defender = new DerivedCombatStats(100, 10, 20, 30, 30, 20, 20);
        var chance = CombatMath.ComputeDodgeChance(attacker, defender, Balance);
        // diff = defender.MfDodge - attacker.MfAntiDodge = 30 - 30 = 0
        chance.Should().Be(0.10m);
    }

    [Fact]
    public void ComputeDodgeChance_HigherDefenderAgility_IncreasesChance()
    {
        var attacker = new DerivedCombatStats(100, 10, 20, 20, 20, 20, 20);
        var defender = new DerivedCombatStats(100, 10, 20, 40, 40, 20, 20);
        var chance = CombatMath.ComputeDodgeChance(attacker, defender, Balance);
        // diff = 40 - 20 = 20 (positive, higher chance)
        chance.Should().BeGreaterThan(0.10m);
    }

    [Fact]
    public void ComputeCritChance_EqualStats_ReturnsBase()
    {
        var attacker = new DerivedCombatStats(100, 10, 20, 30, 30, 20, 20);
        var defender = new DerivedCombatStats(100, 10, 20, 30, 30, 20, 20);
        var chance = CombatMath.ComputeCritChance(attacker, defender, Balance);
        // diff = attacker.MfCrit - defender.MfAntiCrit = 20 - 20 = 0
        chance.Should().Be(0.10m);
    }

    [Fact]
    public void ComputeCritChance_HigherAttackerIntuition_IncreasesChance()
    {
        var attacker = new DerivedCombatStats(100, 10, 20, 20, 20, 40, 40);
        var defender = new DerivedCombatStats(100, 10, 20, 20, 20, 20, 20);
        var chance = CombatMath.ComputeCritChance(attacker, defender, Balance);
        // diff = 40 - 20 = 20 (positive, higher chance)
        chance.Should().BeGreaterThan(0.10m);
    }

    [Fact]
    public void RollDamage_WithinRange()
    {
        var rng = new DeterministicRandomProvider(42);
        var derived = new DerivedCombatStats(100, 10, 20, 5, 5, 3, 3);

        for (int i = 0; i < 100; i++)
        {
            var damage = CombatMath.RollDamage(rng, derived);
            damage.Should().BeGreaterThanOrEqualTo(10);
            damage.Should().BeLessThanOrEqualTo(20);
        }
    }

    [Fact]
    public void RollDamage_DeterministicWithSameSeed()
    {
        var derived = new DerivedCombatStats(100, 10, 20, 5, 5, 3, 3);
        var rng1 = new DeterministicRandomProvider(42);
        var rng2 = new DeterministicRandomProvider(42);

        for (int i = 0; i < 10; i++)
        {
            CombatMath.RollDamage(rng1, derived).Should().Be(CombatMath.RollDamage(rng2, derived));
        }
    }

    [Fact]
    public void ComputeDerived_ZeroStats_ProducesValidResults()
    {
        var stats = new PlayerStats(0, 0, 0, 0);
        var derived = CombatMath.ComputeDerived(stats, Balance);
        derived.HpMax.Should().Be(50); // BaseHp only
        derived.DamageMin.Should().Be(4); // floor(5 * 0.8) = 4
        derived.DamageMax.Should().Be(6); // ceil(5 * 1.2) = 6
        derived.MfDodge.Should().Be(0);
        derived.MfCrit.Should().Be(0);
    }

    [Fact]
    public void ComputeChance_MonotonicallyIncreasingWithDiff()
    {
        // Larger diff should produce larger chance
        var chanceLow = CombatMath.ComputeChance(10, 0.10m, 0.01m, 0.40m, 0.30m, 50m);
        var chanceMid = CombatMath.ComputeChance(50, 0.10m, 0.01m, 0.40m, 0.30m, 50m);
        var chanceHigh = CombatMath.ComputeChance(100, 0.10m, 0.01m, 0.40m, 0.30m, 50m);

        chanceLow.Should().BeLessThan(chanceMid);
        chanceMid.Should().BeLessThan(chanceHigh);
    }
}
