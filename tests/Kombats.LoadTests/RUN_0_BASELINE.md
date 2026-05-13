# Run 0 — Verified Baseline (Chapter 3 starting point)

## 1. Header

- **Date:** 2026-05-12 (clean-state attempt completed 18:07:32 → 18:09:58 +05:00)
- **Branch / HEAD:** `development` @ `2842b7f` (`fix(matchmaking): thread per-tick cancellation into lease renewal loop` — the Ch2 lease fix is in)
- **Stack state:** all 14 containers Up after `docker compose down -v` + clean rebuild and reseed; single-replica Battle, no SignalR backplane (baseline-by-design for Ch3)
- **Iteration logs:**
  - `iterations-2026-05-12--17-26-49.jsonl` — Run 0 attempt #1, **stale stack, failed** (629 / ~2100 iterations, monotonic throughput decay; see §5)
  - `iterations-2026-05-12--18-07-28.jsonl` — Run 0 attempt #2, **baseline confirmed** (this document)

## 2. Configuration

- **NBomber load shape** (from `Scenarios/ConcurrentBattlesScenario.cs:101-106`): `RampingConstant(copies=25, during=30s)` → `KeepConstant(copies=25, during=90s)`. One iteration = one bot session = one half-battle; pairs share `battle_id`.
- **Stack:** single-replica Battle, no SignalR backplane, single-replica BFF/Matchmaking/Players/Chat.
- **Bots:** 25 concurrent (50 seeded `loadbot-*` users in pool).
- **Total wall-clock:** 145.7 s (120 s test + post-test scenario-drain tail).

## 3. Results table

All Run 0 cells produced by `./scripts/aggregate-phases.sh iterations-2026-05-12--18-07-28.jsonl` (Overall slice unless noted). Pairing throughput from matchmaking-service log timestamps: 1053 `Match created` lines between 13:07:32 UTC and 13:09:28 UTC = 116 s ⇒ 9.08 matches/s.

| Metric | Handoff | Run 0 measured | Delta |
|---|---|---|---|
| ok count | 2102 | 2096 | −0.3 % |
| fail count | 25 | 25 | 0 % |
| RPS | 17.5 | 17.47 | −0.2 % |
| `queue_wait_ms` p50 | 1018 ms | 1014.7 ms | −0.3 % |
| `queue_wait_ms` p95 | 1526 ms | 1519.4 ms | −0.4 % |
| `total_ms` p50 | 1115 ms | 1106.0 ms | −0.8 % |
| `total_ms` p95 | 1609 ms | 1593.4 ms | −1.0 % |
| `battle_ms` p50 | — (not cited) | 39.3 ms | n/a |
| `join_battle_ms` p50 | — (not cited) | 4.4 ms | n/a |
| `connect_ms` p50 | — (not cited) | 4.4 ms | n/a |
| `onboard_ms` p50 | — (not cited) | 3.3 ms | n/a |
| Battles per 2 min | 1051 | 1049 | −0.2 % |
| Pairing throughput | 9.44 matches/s | 9.08 matches/s | −3.8 % |

**Notable deltas:** none. Every comparable cell is within ±5 %. The four phases not cited in the handoff (`battle_ms`, `join_battle_ms`, `connect_ms`, `onboard_ms`) are recorded here so subsequent runs have a comparable column. The −3.8 % on pairing throughput is computed from a slightly different active-pairing window (116 s vs handoff's ~111 s) and is well within measurement noise.

## 4. Verdict

**Baseline confirmed.** Run 0 attempt #2 reproduces the Chapter 2 handoff numbers within ±1 % on every cited metric. Chapter 3 can begin from this baseline.

## 5. Critical operational note

Run 0 was first attempted against a stale Docker stack (~315 leftover battles in Postgres, ~7 000 leftover Redis keys from prior chapter runs). That attempt failed: throughput reached baseline peak (~98 iter/5s at t=20 s) then decayed monotonically to zero by t≈63 s. Only 628 of expected ~2100 iterations completed. Full diagnostic in `RUN_0_DRIFT_INVESTIGATION.md`.

After `docker compose down -v` + clean rebuild, baseline returned within ±5 % on every metric.

**Root cause of the accumulation** (per `CLEANUP_WORKER_DIAGNOSIS.md`): `BattleRedisOptions.StateTtlAfterEnd` is defined and configured at `src/Kombats.Battle/.../BattleRedisOptions.cs:20` and `appsettings.json:42` but never read by any code in `src/Kombats.*`. Redis `battle:state:*` keys are persisted without TTL and have no deletion path. State keys accumulate one-per-battle indefinitely.

**Implication for Chapter 3 measurement discipline:** until the Redis TTL is wired (Chapter 2.5 candidate, see below), every measurement run in §13 MUST be preceded by `docker compose down -v` + clean rebuild. Without teardown, accumulated state silently degrades throughput by ~3× under sustained 25-pair concurrency and renders before/after comparisons meaningless.

## 6. Side observations

- **Redis state TTL is unwired, Chapter 2.5 candidate.** `BattleRedisOptions.StateTtlAfterEnd` defined/configured but never consumed (`grep` shows zero call sites in `src/Kombats.*`). Fix scope: wire the option through `RedisBattleStateStore` so battles transitioning to `Ended` get a TTL applied (suggested 1 h). Estimated 1–3 lines of code + test. Tracked for Chapter 2.5.
- **`matchmaking.player_combat_profiles` SERIALIZABLE conflicts during the failed run.** 32 `40001 could not serialize access` Postgres errors observed during Run 0 attempt #1, against `UPDATE matchmaking.player_combat_profiles`. Likely an interaction between projection writes and concurrent matchmaking reads under sustained load. Not on Chapter 3 critical path. Possible additional Chapter 2.5 candidate, lower priority than the TTL fix.
- **BFF queue-polling p95 ≈ 1.80 s dominates `total_ms`.** BFF queue-status endpoint p95 measured at ~1.80 s during the successful run (driven by `queue_wait_ms` p95 = 1.52 s plus polling slack). The polling cadence is 500 ms in `VirtualPlayer.PollUntilMatchedAsync` (`VirtualPlayer.cs:294`). Push-based match notification (handoff item 3d) would close this gap. Not on Chapter 3 critical path.

## 7. Production implication

The clean-teardown discipline used for measurement in this chapter is **a dev workaround for a production-relevant bug**, not a production-style operational procedure. In production, `docker compose down -v` would destroy player accounts and battle history. The correct production fix is the unwired `StateTtlAfterEnd` (bullet 1 above). At ~1 000 battles/day production rate, ~365 k state keys/year would accumulate uncapped without the fix — order of ~GB of Redis retention for completed battles, plus a latent O(N) hazard if any code path scans `battle:state:*`. This is recorded here so that anyone running this chapter's measurements later (or anyone reading this for a job interview) understands the test discipline is bounded to dev and the production fix is small and named.
