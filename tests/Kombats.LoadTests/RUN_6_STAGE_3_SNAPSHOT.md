# Phase D — Stage 3 (100 simul) Snapshot

## Verdict

**BROKEN.** `total_ms p95 = 5 242 ms` trips the 4 800 ms latency cliff
by **+442 ms (+9.2 %)**. Throughput flat-up (19.0 RPS, not falling),
real-failure rate 0 %, zero swap, no container near cap. Single tripped
criterion = latency cliff, exactly as recon predicted. Scan stops here.

## Run parameters

Users 100, ramp 30 s, hold 300 s, wall 331 s (15:43:58 → 15:49:29).
JSONL `iteration-logs/iterations-2026-05-16--15-43-58.jsonl` — **6 274
rows**. Capacity chain (base + obs + obs.override + override + capacity).
Single Battle + single Matchmaking replica (recon Q4 — replicas cannot
move this ceiling).

## aggregate-phases.sh — Overall slice

```
phase               count     p50_ms     p95_ms     p99_ms     max_ms
auth_ms              6274          0          0         36         83
onboard_ms           6274         15         40         51        290
connect_ms           6274         17         42         55        175
queue_wait_ms        6274       4644       5143       5183       5399
join_battle_ms       6274          4         14         30        275
battle_ms            6274         40         94        291        580
total_ms             6274       4752       5242       5303      90003
```

`max_ms total_ms=90003` = 12 BattleTimeouts (shape-shutdown).

## vs Stage 1 and Stage 2 (the scan trend)

| Metric              | S1 (25) | S2 (50) | S3 (100) | S1→S2 | S2→S3 |
|---------------------|---------|---------|----------|-------|-------|
| total_ms p50        | 1 085   | 2 158   | **4 752** | +99 % | +120 % |
| total_ms p95        | 1 202   | 2 671   | **5 242** | +122 %| +96 %  |
| queue_wait p50      | 1 015   | 2 043   | 4 644     | +101 %| +127 % |
| queue_wait p95      | 1 041   | 2 566   | 5 143     | +147 %| +100 % |
| battle_ms p50 / p95 | 46/399  | 44/219  | 40/94     | flat/−45 % | flat/−57 % |
| throughput (iter/s) |  18.6   |  18.8   |  **19.0** | +1.1 %| +1.1 % |

Doubling rule holds: each 2× users ≈ 2× queue_wait, throughput pinned.
battle_ms keeps shrinking — upstream-bottleneck signature.

## Break analysis

| Criterion                          | Threshold | Measured | Verdict |
|------------------------------------|-----------|----------|---------|
| total_ms p95 cliff                 | > 4 800   | **5 242**| **TRIPPED (+442 ms, +9.2 %)** |
| REAL error rate                    | > 5 %     | 0.000 %  | clean   |
| throughput regression              | RPS falls | 19.0 ↑   | clean   |
| host: Docker 100 %>30 s OR swap    | —         | none     | clean   |

**Recon prediction:** total_ms p95 ≈ 5 200 ms → measured 5 242 ms →
**within 0.8 %**. Arithmetic (`1 / (100 ms + ~7 ms work) × 2 bots`)
confirmed across all three stages. `Task.Delay(100 ms)` at
`MatchmakingPairingWorker.cs:72` is the binding constraint.

## Error rate

| Class            | Count | % of total | vs Stage 2 |
|------------------|-------|------------|------------|
| shape-shutdown   | 12    | 0.191 %    | −0.067 pp  |
| REAL failures    | 0     | 0.000 %    | flat       |

All 12 BattleTimeouts match shape-shutdown exactly (`battle_ms=0`,
`queue_wait ∈ [2,4] ms`, `total≈90 003 ms`). **No new failure mode at
100 vs 50.** System degrades latency-gracefully. Outcomes: Won 2 825 /
Lost 2 825 / Draw 612 / BattleTimeout 12.

## Infrastructure snapshot (mid-hold)

T1 = 15:46:06 (~68 s into hold), T2 = 15:48:06 (~248 s in). T3 landed
40 s **after** the 15:49:29 run end — cooldown not steady-state, so
excluded from S3 averages.

| Container             | T1 % | T2 % | T3 cool | Mem range   |
|-----------------------|------|------|---------|-------------|
| kombats-battle        |50.02 |52.37 |  1.34   | 220–233 MiB |
| kombats-matchmaking   |53.85 |43.30 |  4.66   | 179–191 MiB |
| kombats-postgres      |36.90 |29.73 |  1.06   | 161–170 MiB |
| kombats-bff           |25.43 |24.24 |  0.09   | 114–118 MiB |
| kombats-players       |13.69 |10.62 |  0.75   | 142–152 MiB |
| kombats-chat          |12.75 | 5.64 |  0.91   | 137–146 MiB |
| redis / rabbit / rest | 5/5  | 5/4  |  ~0     | low, no climb |

- **Postgres conns**: 50 / 50 / 45 → mid-hold avg **50 / 300 = 16.7 %**
  (in projected 50-55 band).
- **Redis**: T1 3 229 ops/sec (DBSIZE 27 436), T2 2 951 ops/sec (54 268),
  post-run 65 ops/sec (72 559).
- **RabbitMQ**: `list_queues` hit 25 s timeout (as S2) but returned
  rows — all listed queues depth 0.
- **Host swap**: pre 0/0, post 0/0. **Zero swap.** Pages free 24 772 →
  37 502 (memory freed).

## vs Stage 1+2 infrastructure

| Service        | S1 avg | S2 avg | S3 avg | S1→S3 |
|----------------|--------|--------|--------|-------|
| battle         | 57.8   | 55.9   | **51.2** | **−12 % (NOT bottleneck)** |
| matchmaking    | 39.7   | 40.5   | 48.6   | +22 % (still 33 % of cap) |
| postgres       | 27.0   | 29.6   | 33.3   | +23 % |
| bff            | 17.7   | 22.5   | 24.8   | +40 % (per-bot HTTP) |
| postgres conns | 36     | 42     | 50     | +39 % (linear with bots) |

**Battle CPU DECREASED as users tripled** — most decisive signal Battle
is NOT the limiter. Matchmaking climb is auxiliary work (outbox, sweep
over more queued bots), not the pairing worker — by design it uses ~7 %
of one core (sleeps 93 % per recon Q3).

## Queue vs battle split

Direct `mm:player:*` probes returned 0 (racy at this resolution).
Little's Law: per-battle in-flight ≈ 44 ms ⇒ < 1 pair in battle at any
instant. Per-bot round-trip = 4 752 ms. **At steady state: ≈ 99 / 100
bots in queue, ≈ 1 in battle.** Bottleneck is naked.

## Observations for the architect

- **Break exactly as predicted.** p95 5 242 vs 5 200 predicted = 0.8 %.
  Arithmetic model now backed by three independent stages.
- **No new failure mode.** Only latency criterion tripped; real failures
  zero; throughput flat (not falling); no host pressure. Cleanest
  possible capacity-scan result.
- **Bottleneck story fully confirmed:** matchmaking pairing rate — not
  Battle (CPU went down), not BFF, not Postgres, not Redis, not
  RabbitMQ, not host.
- **Fix surface is one file, one line:** `MatchmakingPairingWorker.cs:72`.
  Recon Q5 lists secondary walls likely to appear post-fix (5 sequential
  awaits per pair → ~140 pairs/s floor; SERIALIZABLE conflicts on
  `player_combat_profiles`; outbox flush per pair). None visible today.
- **Scan ends here.** Stages 4-6 under current code would multiply
  queue_wait linearly — no new information. Phase E can begin.

## How long it took

Cold start + seed-400 ~4 min; smokes ~1.5 min; Locust 5.5 min; infra
snapshots parallel; aggregate + report ~2 min. **Total ~13 min** —
inside the 30 min budget.
