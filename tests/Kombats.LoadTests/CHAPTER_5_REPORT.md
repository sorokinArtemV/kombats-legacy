# Chapter 5 — Lift the Matchmaking Pairing-Rate Ceiling — Report

## What this chapter did

Chapter 4 found matchmaking pairing pinned at ~9.5 pairs/s (~19 RPS) — a
code-level cap, not a capacity cap. Chapter 5 removed that cap, measured the
new ceiling, and named the next wall. This was the first chapter that **fixed**
code rather than only measuring it.

## The fix

The Chapter 4 cap had two reinforcing causes in `src/Kombats.Matchmaking/*` —
an unconditional 100 ms `Task.Delay` after every tick and a one-pair-per-tick
handler: `1 / (100 ms + ~7 ms work) ≈ 9.35 pairs/s`.

Three changes, all on `feat/chapter-5-pairing-ceiling`:

1. **Bounded inner pairing loop** in `ExecuteMatchmakingTickHandler` — pairs
   back-to-back within one tick until the queue empties, `MaxPairsPerTick` (64)
   is reached, or a soft deadline (`LockTtlMs / 2` ≈ 2.5 s) elapses.
2. **Conditional idle backoff** in `MatchmakingPairingWorker` — `Task.Delay`
   now fires only when a tick created zero pairs; a productive tick continues
   immediately.
3. **`MaxPairsPerTick`** — new config key, default 64.

Recon (`CHAPTER_5_RECON.md`) established the correctness boundary: pair
atomicity lives in the Redis Lua script (untouched by the fix), and the loop is
bounded so one lease window stays well under the 5 s TTL.

## Result — before / after (50 simul, single replica)

| Metric              | Run 6 (before) | Run 7 (after) | Change   |
|---------------------|----------------|---------------|----------|
| Throughput          | 18.8 RPS       | 57.8 RPS      | **+208 %** |
| `total_ms` p95      | 2 671 ms       | 931 ms        | −65 %    |
| `queue_wait_ms` p95 | 2 566 ms       | 612 ms        | −76 %    |
| REAL error rate     | 0 %            | 0 %           | flat     |

The ceiling was lifted ~3×. The matchmaking worker, which slept ~93 % of wall
time behind the old `Task.Delay`, now runs productively — its CPU went from a
flat ~40–49 % (the sleeping signature) to a flat ~103 %.

## The new wall

The new pairing-rate ceiling is **matchmaking single-core CPU saturation**: at
50 simul the worker pegs one vCPU flat (~103 %). It is no longer paced by a
timer — it is paced by how fast one thread runs the five sequential
Redis/Postgres awaits per pair. This is a code / single-threading limit, not
host saturation (zero swap, host CPU well below its ceiling).

This corrects the recon prediction. Recon expected SERIALIZABLE `40001`
conflicts on `player_combat_profiles` as the likely next wall. Those conflicts
do appear and climb with load (8 → 60 across the scan) — but they retry cleanly
and are not the *first* wall. The first wall is the worker's own CPU; `40001`
is the likely *second* wall.

## Honest limits of the measurement

The capacity scan is **partial — 2 valid stages of 3**. Stage 3 (100 simul)
was attempted three times and is invalid every time: the single-process Locust
load generator saturated one macOS core, so its numbers reflect load-generator
starvation, not the backend. The measured ceiling is therefore a **lower
bound — ≥ 58 RPS / 50 simul sustained clean** — not a true ceiling. This
single-host Mac cannot honestly drive load past ~50 simul.

A distributed-Locust re-run was scoped; a read-only recon found the harness is
not multi-process-safe (four gaps, listed in `RUN_7_RESULTS.md`). Fixing it is
tool work, correctly left out of a measure-only phase.

## Scope held, and what is deferred

One bottleneck, one chapter. The fix touched `src/Kombats.Matchmaking/*` only;
the secondary walls were named, not fixed. Deferred:

- **Matchmaking multi-core / horizontal scaling** — the natural response to a
  single-core wall, but the single Redis lease is correctness machinery;
  reworking it without breaking pair atomicity is a Chapter 6 refactor.
- **Distributed load generator** — prerequisite for measuring above ~50 simul;
  four harness gaps documented in `RUN_7_RESULTS.md`.
- **`40001` SERIALIZABLE conflicts** — the likely second wall once the
  single-core wall is lifted.
