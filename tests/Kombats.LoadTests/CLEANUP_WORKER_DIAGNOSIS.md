# Cleanup Worker Diagnosis

## 1. Summary

`BattleRecoveryWorker` exists and was registered & running during Run 0, but
addresses only **half** of the leftover state and only after a long delay. It
scans Postgres every 30 s for `battle.battles` rows where `state != 'Ended'`
AND `created_at < now − 600 s`, then force-ends them. It never touches Redis
`battle:*` keys. The 7089 Redis keys in Run 0 were not the worker's
responsibility, and the 315 stale Postgres rows were too young (test ≈ 2 min)
for the 10-min threshold to consider them. The architect's recollection is
partially right: a recovery worker exists, but it is not a Redis GC and its
threshold makes it a no-op during/right after a load run.

## 2. Worker found

- **Worker:** `src/Kombats.Battle/Kombats.Battle.Bootstrap/Workers/BattleRecoveryWorker.cs:10`
- **Service:** `src/Kombats.Battle/Kombats.Battle.Application/UseCases/Recovery/BattleRecoveryService.cs:19`
- **Repo:** `src/Kombats.Battle/Kombats.Battle.Infrastructure/Data/BattleRecoveryRepository.cs:10`
- **Registration:** `src/Kombats.Battle/Kombats.Battle.Bootstrap/Program.cs:166` (`AddHostedService<BattleRecoveryWorker>`); hosted by Battle (single instance).
- **Cleans:** Postgres only. Per row: no Redis state → mark Ended + outbox `BattleCompleted`; Redis Phase=Ended → same; Phase=Resolving → re-resolve.
- **Rule:** `state != 'Ended' AND created_at < (now - StaleBattleThresholdSeconds)`.
- **Config** (`appsettings.json:90`): `ScanIntervalMs=30000`, `StaleBattleThresholdSeconds=600`, `BatchSize=50`.
- **Does NOT touch Redis.** `battle:action:*` / `battle:turn:*` get a 12 h TTL via `Battle.Redis.ActionTtl`. `battle:state:*` has **no TTL and no deletion path** — `BattleRedisOptions.StateTtlAfterEnd` is defined (`BattleRedisOptions.cs:20`, `appsettings.json:42` = null) but **never read anywhere** (zero call sites). Dead flag.
- Sibling `MatchTimeoutWorker` (`Workers/MatchTimeoutWorker.cs:11`) operates on `matchmaking.matches`, not battle state.

## 3. Runtime evidence

Run 0's container is gone (rebuilt after `docker compose down -v`). Current
Battle container (Up 12 min) shows boot line:

```
[13:06:42 INF] BattleRecoveryWorker started. ScanInterval=30000ms, StaleBattleThreshold=600s
```

No `Found N non-terminal battles` / `Orphaned battle` / `Force-ended` lines —
PG `count(*) FILTER (state != 'Ended') = 0` right now, so nothing to do. Boot
line is unconditional ⇒ absence = no work, not a silent crash. Current Redis
DB 0 after a fresh ~1053-battle run: 24068 keys = **15343 action / 7672 turn /
1053 state**. Sampled TTLs: action ≈ 42 487 s, turn ≈ 42 464 s (~12 h, matches
`ActionTtl`); **state TTL = -1 (no expiry)**. One state key per battle,
persists indefinitely.

## 4. Hypothesis ranking

1. **Worker scope mismatch — by design.** *(highest)* Worker recovers Postgres,
   not Redis. State keys accumulate forever; action/turn bounded at 12 h. The
   7089 in Run 0 = state-key buildup over days + decaying action/turn. Evidence:
   dead `StateTtlAfterEnd`, no `KeyDelete` on `battle:state:*` anywhere, current
   state-key count == battle count.
2. **10-min threshold > test duration.** *(complementary)* Run 0 lasted ~2 min;
   worker can't see stuck rows for 8+ min after test end. Whether it would
   eventually catch the 315 is unobserved — containers were wiped first.
3. **Worker disabled / crashing.** *(ruled out)* Boot line present, no
   exceptions, registration unconditional.
4. **Lease / consumer bug leaving rows non-terminal.** *(separate pathway)*
   315 stale rows ⇒ end-of-battle path failed for ~half of attempted battles.
   Different investigation; the worker is last-resort cleanup, not the cause.

## 5. Recommended next action

**Real bug in worker scope — recommend Chapter 2.5 after Chapter 3.** Current
design cannot keep Redis bounded under sustained load: state keys are eternal,
recovery worker never touches them. Two small fixes (not now): (a) wire
`StateTtlAfterEnd` through `RedisBattleStateStore` so ended battles get a TTL
(e.g., 1 h); (b) add a load-test harness teardown (FLUSHDB + TRUNCATE) so
measurement isn't conflated with leftover state — orthogonal to (a). Lowering
the 10-min threshold just for tests is not advised; it's reasonable in prod.

## 6. Questions for the architect

1. Is `StateTtlAfterEnd` deliberately unwired (debugging-replay use case)?
   Defined and configured but never consumed.
2. Is the recovery worker "best-effort recovery" or "the cleanup mechanism"?
   Code reads as the former (publishes `BattleCompleted`; doesn't GC Redis).
3. Is the expected steady-state `count(*) FILTER (state != 'Ended')` zero, or
   is a non-zero floor acceptable while the worker drains it? This decides
   whether Run 0's 315 means "worker hasn't had time" vs "battles leak faster
   than worker catches up."
4. Should Chapter 3 harness teardown assert `state != 'Ended'` count = 0 and
   `battle:state:*` count = expected, or is that an explicit non-goal?
