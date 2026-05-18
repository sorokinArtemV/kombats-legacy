# Chapter 4 — Capacity Test + Locust Migration

## What this chapter closed

Four deliverables, plan §1 / §5:

- **D1 — NBomber → Locust migration.** Ported `VirtualPlayer.cs` + three C# clients to Python under `tests/Kombats.LoadTests/locust/`. Calibration Run #3 within ±5% of NBomber Run 0 on every acceptance metric (`PHASE_B1B_RESULTS.md`). The 25-pair NBomber Community-license ceiling is gone; user pool seeds to 400 cleanly.

- **D2 — Capacity ramp methodology.** Six-stage ramp, 5-min sustained hold, stop-on-first-break criteria, scaling experiment at the break point. Lives embedded in `CHAPTER_4_PLAN.md` §3 rather than the planned standalone `CAPACITY_METHODOLOGY.md` — the plan was the only Chapter 4 consumer, so promoting it to a shared file was deferred until a later chapter needs it.

- **D3 — Infrastructure scaling config.** `docker-compose.capacity.yml` (per-service CPU/memory limits + Postgres `max_connections=300`), `scripts/generate-battle-replicas.py` generator (N=1..4), env-driven Locust shape (`RAMP_USERS` / `HOLD_SECONDS`), Jaeger memory bound. 25-simul smoke landed inside the ±5% band (`PHASE_C_RESULTS.md`).

- **D4 — First capacity scan (Run 6).** Three of six stages run (25 / 50 / 100 simul, single Battle + single Matchmaking, cold start each). Stop-on-first-break tripped at Stage 3.

## Headline result

The single-host stack sustains ~50 concurrent bots cleanly; the latency cliff (`total_ms p95 > 4 800 ms`) crosses between 50 and 100 simul. At 100 simul `total_ms p95 = 5 242 ms`. Bottleneck: matchmaking pairing rate — fixed 100 ms `Task.Delay` plus one-pair-per-tick handler, gated by a single Redis lease, at `src/Kombats.Matchmaking/Kombats.Matchmaking.Bootstrap/Workers/MatchmakingPairingWorker.cs:72`. Throughput pinned at ~9.5 battles/s (~19 RPS), flat across all three stages — a code-level cap, not a capacity cap. Per-stage table in `RUN_6_RESULTS.md`.

## Success criteria

Plan §5 walked:

1. **Ceiling known** — yes, between 50 and 100 simul; only the latency cliff tripped.
2. **Cause named with file:line** — yes, `MatchmakingPairingWorker.cs:72` plus `ExecuteMatchmakingTickHandler.cs:52`.
3. **Scaling effect measured** — answered by recon, not measurement. See next section.
4. **Next chapter scoped** — yes; Chapter 5 = lift the pairing ceiling, secondary-wall list pre-staged in `RUN_6_RESULTS.md`.

## Key decision — scaling experiment answered by recon, not measurement

The plan called for re-running the breaking stage with increased Battle replicas to quantify scaling. The Phase D recon (`RUN_6_MATCHMAKING_RECON.md` Q4) showed this cannot help: the pairing variant is hardcoded (`MatchmakingPairingWorker.cs:41`), the lease key derives only from variant (`RedisLeaseLock.cs:181`), and losers of the lease race fall through to the same `Task.Delay(100 ms)`. Adding Battle replicas does nothing because Battle is not the bottleneck — its CPU *decreased* across the scan (57.8 % → 51.2 %). Adding Matchmaking replicas does nothing because they would share the same single-writer lease.

Running the experiment anyway would have measured "no change" at the cost of ~1 h of cold-start cycle for an outcome already provable from the code. Closure stands on the proof: the question "does scaling lift this ceiling?" has a definitive answer, just not a measured one.

## What this enables

Chapter 5 = lift the matchmaking pairing ceiling. Entry point is located (`MatchmakingPairingWorker.cs:72` + `ExecuteMatchmakingTickHandler.cs:52`), direction is named (reduce `TickDelayMs` or loop the handler over the queue until empty inside one lease window), and the secondary-wall list is pre-staged in `RUN_6_RESULTS.md` §"Secondary walls" (5 sequential Redis/Postgres awaits per pair, then `player_combat_profiles` SERIALIZABLE conflicts at ~30–50 battles/s). Chapter 5 starts with no recon debt.

STORY Part 3 (portfolio narrative covering Chapters 2.5 + 4) is a separate following task.

## Things deliberately not done

- The matchmaking fix — Chapter 5 scope; no change in `src/Kombats.*` this chapter.
- Stages 4–6 of the scan — arithmetic model agrees with measurement to within 0.8 % on Stage 3 latency; more stages add queue depth, not information.
- BFF horizontal scaling refactor (`_connections` → Redis) — Chapter 8+.
- Matchmaking horizontal scaling — blocked by hardcoded variant + single lease; follows the Chapter 5 fix, not before it.
- Cloud migration — host never tripped (zero swap, no container near its CPU or memory limit); the ceiling is code-side, not infra-side.

## Honest notes

Plan §1 predicted BFF queue-polling as the likely first ceiling; measurement said matchmaking. Predict-then-measure working as intended, not a plan failure. Ceremony budget held: leaner than Chapter 2.5 as intended — this report ≤ 80 lines (plan §8).
