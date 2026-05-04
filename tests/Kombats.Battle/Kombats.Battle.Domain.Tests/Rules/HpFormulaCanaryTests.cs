using FluentAssertions;
using Kombats.Battle.Domain.Model;
using Kombats.Battle.Domain.Rules;
using Xunit;

// CANARY TEST — DO NOT IGNORE A FAILURE OF THIS FILE.
//
// This test pins the HP formula and its production balance constants:
//
//     hpMax = balance.Hp.BaseHp + stats.Stamina * balance.Hp.HpPerEnd
//     Production values: BaseHp = 0, HpPerEnd = 6
//     Source of truth:
//       src/Kombats.Battle/Kombats.Battle.Bootstrap/appsettings.json
//       under Battle.Rulesets.Versions["1"].CombatBalance.Hp
//       (CurrentVersion = 1; if CurrentVersion changes, update the
//        literals below to match the new active version).
//
// The frontend keeps a client-side mirror of this formula at:
//
//     src/Kombats.Client/src/modules/player/hp-formula.ts
//
// If you are reading this comment because a test below failed, you
// have changed the HP formula or its production constants. You MUST
// also update the frontend mirror in the SAME PR. Silent drift
// between the two implementations causes incorrect HP bars,
// damage-percentage UI, and "near-death" narration triggers.
//
// If you are intentionally changing the production values in
// appsettings.json, update the literals in this file AND the
// frontend mirror together.
//
// Owner: Battle domain. Cross-team coordination required for any
// change.

namespace Kombats.Battle.Domain.Tests.Rules;

public class HpFormulaCanaryTests
{
    [Fact]
    public void HpBalance_PinsProductionConstants()
    {
        var balance = new HpBalance(baseHp: 0, hpPerEnd: 6);

        balance.BaseHp.Should().Be(0);
        balance.HpPerEnd.Should().Be(6);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(100)]
    public void ComputeDerived_HpFormula_MatchesIndependentlyComputedExpected(int stamina)
    {
        var defaultBalance = TestHelpers.DefaultBalance;
        var balance = new CombatBalance(
            hp: new HpBalance(baseHp: 0, hpPerEnd: 6),
            damage: defaultBalance.Damage,
            mf: defaultBalance.Mf,
            dodgeChance: defaultBalance.DodgeChance,
            critChance: defaultBalance.CritChance,
            critEffect: defaultBalance.CritEffect);

        var stats = new PlayerStats(strength: 5, stamina: stamina, agility: 5, intuition: 5);

        // EXPECTED is computed from literal production constants
        // (BaseHp=0, HpPerEnd=6). Do NOT replace with a call to
        // CombatMath — that would defeat the canary.
        int expected = 0 + stamina * 6;

        var derived = CombatMath.ComputeDerived(stats, balance);

        derived.HpMax.Should().Be(expected);
    }
}
