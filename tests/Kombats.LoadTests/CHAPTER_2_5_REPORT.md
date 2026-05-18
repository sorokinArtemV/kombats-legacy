# Chapter 2.5 — Redis hygiene + Observability hardening

## What this chapter closed

Three deliverables before Chapter 4 capacity test:

- **D1 — Redis state TTL wiring.** `BattleRedisOptions.StateTtlAfterEnd` was defined and configured but never read. State keys (`battle:state:*`) accumulated indefinitely. Wired via `KeyExpireAsync` in `RedisBattleStateStore.EndBattleAndMarkResolvedAsync`. Production default set to 1 hour. Closes ~6-month-old architectural debt found by load testing that unit tests missed.

- **D2 — OTLP defensive WARN.** `KombatsObservabilityExtensions` silently skipped OTLP exporter attachment when endpoint was empty. This silent behavior caused 3 chapters of broken observability before being noticed (Chapter 3 Run 4 forensics). Added `Console.Error.WriteLine` WARN at startup. Defensive logging — noisy when misconfigured, silent when correct.

- **D3 — Compose chain documentation.** `README.md` now documents three canonical compose chains (full stack, multi-replica, IDE mode). Closes the Chapter 3 D6 mistake (forgot to include observability override file, caused silent telemetry loss).

## Architectural decision — TTL via C# wrapper, not Lua script

D1 had two paths: extend `EndBattleAndMarkResolvedScript` Lua to atomically `EXPIRE` inside the SET, or add a separate `KeyExpireAsync` call in C# after the script returns.

Chose the C# wrapper. Rationale:

- Ended battles are terminal — no reader depends on TTL being present the instant SET completes. TTL here is housekeeping, not consistency.
- The brief non-atomic window (< 1 RTT) is acceptable; the only failure mode is "battle's state key persists without TTL until cleaned by other means."
- Lua extension would require a sentinel for nullable `TimeSpan` — conditional logic in a hot-path script, not worth the atomicity gain.
- Smaller diff, no Lua hot-path touch.

Trigger to revisit: if a reader ever emerges that expects ended battles to have TTL set atomically with SET, switch to Lua-extension approach.

Inline comment in `RedisBattleStateStore.cs` preserves this rationale for future maintainers.

## Sustainable Run measurement (Run 5)

Methodology: 4 sequential 25-bot runs, no `down -v` between them, TTL overridden to 60s via env var, 90s sleep between runs. Goal: prove the system survives across multiple runs without between-run teardown.

Note: between Run 1 and Run 2 the architect's machine rebooted (Docker Desktop kills containers on macOS reboot). Run 1 was completed before the reboot; Runs 2-4 ran hot back-to-back after. Methodology deviation documented in `RUN_5_NOTES.md`.

Results — all 4 runs within ±3% of Run 0 baseline:

| Run | ok | RPS | total_ms p50 | total_ms p95 | sticky keys |
|---|---|---|---|---|---|
| Run 0 baseline | 2096 | 17.47 | 1106 | 1593 | n/a |
| Run 1 | 2102 | 17.5 | 1115 | 1609 | 3 |
| Run 2 | 2106 | 17.55 | 1134 | 1630 | 2 |
| Run 3 | n/a* | n/a* | n/a* | n/a* | 4 |
| Run 4 | 2108 | 17.57 | 1131 | 1625 | 2 |

*Run 3 NBomber report not captured separately; throughput pattern in Grafana identical to other runs.

**TTL mechanism captured in action:** Run 3 POST snapshot caught 309 keys with positive TTLs counting down (4-27s remaining of 60s override). T+30s monitor: 309 → 4 keys. Direct evidence of TTL firing, not just outcome-based inference.

**Sticky edge case:** 0.26% average rate (3/2/4/2 across 4 runs). These are battles where `KeyExpireAsync` was cancelled via `TaskCanceledException` before reaching Redis — the non-atomic window manifesting exactly as predicted in the architectural decision. Accepted as housekeeping cost.

## What this enables

Chapter 4 capacity test prerequisites met:
- TTL fix removes the need for `down -v` between runs (was a dev workaround for the unwired option)
- Defensive WARN ensures observability misconfigurations surface immediately
- Documented compose chains prevent the Chapter 3 D6 operational error

Without Chapter 2.5, sustained capacity tests (Chapter 4 onwards) would be blocked by Redis state accumulation under sustained load.

## Things deliberately not done

- BFF queue polling fix (push-based notifications) — known bottleneck from Run 0 §6, deferred until Chapter 4 capacity test confirms it as the first ceiling.
- `matchmaking.player_combat_profiles` SERIALIZABLE conflicts — known from Run 0 §6, deferred.
- Active battles split-brain across replicas — known issue, deferred.
- `BattleRecoveryWorker` scope change — not in scope; its Postgres-only recovery role is correct as-is.
