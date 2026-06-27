using Kombats.Battle.Domain.Rules;

namespace Kombats.Battle.Domain.Tests;

internal static class TestHelpers
{
    public static CombatBalance DefaultBalance => new(
        hp: new HpBalance(baseHp: 50, hpPerEnd: 10),
        damage: new DamageBalance(
            baseWeaponDamage: 5,
            damagePerStr: 1.0m,
            damagePerAgi: 0.3m,
            damagePerInt: 0.2m,
            spreadMin: 0.8m,
            spreadMax: 1.2m),
        mf: new MfBalance(mfPerAgi: 2, mfPerInt: 2),
        dodgeChance: new ChanceBalance(@base: 0.10m, min: 0.01m, max: 0.40m, scale: 0.30m, kBase: 50m),
        critChance: new ChanceBalance(@base: 0.10m, min: 0.01m, max: 0.40m, scale: 0.30m, kBase: 50m),
        critEffect: new CritEffectBalance(CritEffectMode.Multiplier, multiplier: 1.5m, hybridBlockMultiplier: 0.5m));

    public static Ruleset DefaultRuleset(int seed = 42) => Ruleset.Create(
        version: 1,
        turnSeconds: 30,
        noActionLimit: 10,
        seed: seed,
        balance: DefaultBalance);
}
