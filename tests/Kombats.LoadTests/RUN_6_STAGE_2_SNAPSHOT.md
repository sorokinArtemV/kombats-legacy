# Phase D — Stage 2 (50 simul) Snapshot

## Verdict

**CLEAN.** total_ms p95 = 2 671 ms (1.8× under the 4 800 ms cliff),
real-failure rate 0 %, zero swap. But "clean" hides the lede: doubling
bots did **not** double throughput (18.8 vs 18.6 RPS). Stage 2 found a
ceiling, not a margin.

## Run parameters

Users 50, ramp 30 s, hold 300 s, wall clock 334 s (15:17:14 → 15:22:48).
JSONL `iteration-logs/iterations-2026-05-16--15-17-14.jsonl` — **6 212
rows**. Capacity chain (base + obs + obs.override + override +
capacity.yml). Single Battle replica.

## aggregate-phases.sh — Overall slice

```
phase               count     p50_ms     p95_ms     p99_ms     max_ms
auth_ms              6212          0          0          0         61
onboard_ms           6212         11         32         43        162
connect_ms           6212         15         36         50        138
queue_wait_ms        6212       2043       2566       2597       2697
join_battle_ms       6212          4         16         32        285
battle_ms            6212         44        219        442        555
total_ms             6212       2158       2671       2734      89999
```

`max_ms=89999` = 16 BattleTimeouts, all shape-shutdown (§Error).

## vs Stage 1 (the anchor)

| Metric              | S1 (25) | S2 (50) | Δ           |
|---------------------|---------|---------|-------------|
| total_ms p50 / p95  | 1 085 / 1 202 | 2 158 / 2 671 | **+99 % / +122 %** |
| total_ms p99        | 1 304   | 2 734   | +110 %      |
| queue_wait p50 / p95| 1 015 / 1 041 | 2 043 / 2 566 | **+101 % / +147 %** |
| battle_ms p50 / p95 |  46 / 399 |  44 / 219 | −4 % / **−45 %** |
| throughput (iter/s) |  18.6   |  18.8   | **+1.1 %**  |

Doubling users doubled queue_wait but did NOT raise throughput; Battle
does same work per battle, bots just wait twice as long for a pair.
Upstream-bottleneck shape — matchmaking pairing rate is the cap.

## Error rate

| Class            | Count | % of total | vs Stage 1   |
|------------------|-------|------------|--------------|
| shape-shutdown   | 16    | 0.258 %    | +0.063 pp    |
| REAL failures    | 0     | 0.000 %    | flat (was 0) |

REAL per spec = `battle_ms > 0` AND `queue_wait > 6`. All 16 have
battle_ms=0 → shape-shutdown (4 had queue_wait drift to 10–14 ms vs S1's
≈4 ms — queue more loaded at shutdown). Well under 5 % threshold.

## Infrastructure snapshot (mid-hold) — T1 15:18:23 / T2 15:19:34 / T3 15:20:16

| Container           | T1 %  | T2 %  | T3 %  | Mem range   |
|---------------------|-------|-------|-------|-------------|
| kombats-battle      | 56.44 | 51.80 | 59.41 | 218–230 MiB |
| kombats-matchmaking | 40.19 | 40.85 | 40.50 | 158–165 MiB |
| kombats-postgres    | 30.11 | 30.09 | 28.69 | 133–153 MiB |
| kombats-bff         | 21.95 | 30.01 | 15.45 | 108–111 MiB |
| kombats-players     | 10.96 | 14.74 |  8.88 | 140–141 MiB |
| kombats-chat        |  9.91 | 10.80 |  9.92 | 126–129 MiB |
| kombats-redis/rabbit| 4-5 / 4 | 5 / 4 | 4 / 4 | low, no climb |

- **Postgres conns**: 39 / 41 / 45 (avg 42 / 300 = **14 %**).
- **Redis**: ops/sec 3068 / 2369 / 2824 (avg ~2 750 vs S1 3 388 — lower
  while DBSIZE warmed 14 316 → 29 184 → 38 123, T3 ≈ S1 steady 38 338).
- **RabbitMQ**: listed queues at depth 0; `rabbitmqctl list_queues`
  itself hit its 60 s timeout on all 3 — soft signal worth watching.
- **Host swap**: pre 0/0, post 0/0. **Zero swap** (pages free freed).

## vs Stage 1 infrastructure

| Service        | S1 → S2 avg       | Δ      |
|----------------|-------------------|--------|
| battle         | 57.8 → **55.9 %** | −3.2 % |
| matchmaking    | 39.7 → **40.5 %** | +2.0 % |
| postgres       | 27.0 → 29.6 %     | +9.6 % |
| bff            | 17.7 → 22.5 %     | +27 %  |
| chat           |  7.6 → 10.2 %     | +34 %  |
| postgres conns | 36 → 42           | +17 %  |

Battle/matchmaking flat-to-down despite 2× bots; bff/chat scale with
connected-bot count, not battle rate.

## Observations for the architect

- **Throughput ceiling, not margin.** Flat ~18.7 RPS regardless of
  input. New bots only add queue depth — they wait in a capped rate.
- **Bottleneck is matchmaking, not Battle.** Battle CPU went *down*;
  matchmaking flat 40 % S1→S2 is fixed-rate-consumer signature — likely
  a single-threaded pairing tick or sweep-interval cap.
- **Stage 3 prognosis:** planned lever is +Battle replicas, but Battle
  isn't the bottleneck here. At 100 simul, queue_wait most likely
  doubles again → ~5 200 ms p95, **tripping the 4 800 ms cliff**.
  Architect: accept Stage 3 as the cliff signal, or investigate
  matchmaking pairing rate first.
- **Postgres 42/300 conns** (14 %), scales with bot count not battles;
  Stage 4 (200) projects ~80–90 — still clear.
- **Zero real failures.** Shape-shutdown rate tracks iter count.

## How long it took

~10 min total: cold start + seed-400 ~2 min, smokes ~1 min, Locust 5.5
min, aggregate+report ~1.5 min (infra snapshots parallel).
