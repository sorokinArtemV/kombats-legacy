/**
 * Mirror of the server-side HP formula in
 * `Kombats.Battle.Domain.Rules.CombatMath.ComputeDerived`, parameterised by
 * `CombatBalance.Hp.BaseHp` / `CombatBalance.Hp.HpPerEnd`.
 *
 * Source of truth (constants live in backend ruleset config):
 *   src/Kombats.Battle/Kombats.Battle.Domain/Rules/CombatMath.cs
 *   src/Kombats.Battle/Kombats.Battle.Bootstrap/appsettings.json
 *     → Battle.Rulesets.Versions["1"].CombatBalance.Hp
 *
 * Naming reconciliation: the backend domain calls the stat `Stamina`; the
 * Players service and BFF expose the same value as `Vitality`. Same number,
 * different label — renaming on either side is out of scope for this mirror.
 *
 * Drift protection — backend canary test:
 *   tests/Kombats.Battle/Kombats.Battle.Domain.Tests/Rules/HpFormulaCanaryTests.cs
 * The canary pins both the formula and the production constants below.
 * Any change to `CombatBalance.Hp` on the backend fails the canary and
 * forces a coordinated update of this file in the same PR.
 *
 * The numeric values below are best-effort against the current ruleset and
 * may be aligned in a follow-up PR once the canary first surfaces an
 * authoritative value.
 */

const HP_BASE = 0;
const HP_PER_VITALITY = 6;

export function deriveMaxHp(vitality: number): number {
  return HP_BASE + vitality * HP_PER_VITALITY;
}
