# Phase D — Stage 1 (25 simul) Snapshot

## Verdict

**CLEAN.** All four break criteria pass with margin: total_ms p95 =
1 202 ms (4× under the 4 800 ms cliff), error rate 0.195 %, throughput
18.6 RPS at steady state (above calibration), zero swap, no container
near cap. Scan procedure validated end-to-end; Stage 2 can begin.

## Run parameters

Users 25, ramp 30 s, hold 300 s, wall clock 331 s (15:02:54 → 15:08:25).
JSONL `iteration-logs/iterations-2026-05-16--15-02-54.jsonl` — **6 154
rows**. Compose chain: base + observability + observability.override +
override + capacity.yml. Single Battle replica.

## aggregate-phases.sh — Overall slice

```
phase               count     p50_ms     p95_ms     p99_ms     max_ms
auth_ms              6154          0          0          0         51
onboard_ms           6154          9         30         41        103
connect_ms           6154         14         36         50        109
queue_wait_ms        6154       1015       1041       1056       3078
join_battle_ms       6154          4         14         24        272
battle_ms            6154         46        399        465        558
total_ms             6154       1085       1202       1304      90004
```

`max_ms total_ms=90004` comes from 12 BattleTimeouts (see §Error rate).

## vs calibration baseline (B1b Run #3, 90 s hold)

| Metric            | Calibration | Stage 1 | Δ        |
|-------------------|-------------|---------|----------|
| total_ms p50      | 1 120       | 1 085   | −3.1 %   |
| total_ms p95      | 1 588       | 1 202   | −24.3 %  |
| queue_wait p50    | 1 020       | 1 015   | −0.5 %   |
| queue_wait p95    | 1 518       | 1 041   | −31.4 %  |
| ok count          | 2 090       | 6 142   | +194 %   |

Favorable-direction outlier reproduces PHASE_C + PHASE_D_PREFLIGHT
exactly. p95 is **essentially identical to the pre-flight 120 s p95**
(total_ms 1 202 vs 1 213 = −0.9 %; queue_wait 1 041 vs 1 036 = +0.5 %).
**5-min hold does not shift the distribution** — the favorable outlier
is steady-state, not a run-length artefact.

## Error rate

12 `BattleTimeout` / 6 154 = **0.195 %**. 0 `QueueTimeout`, 0 generic
`Error`. All 12 match the documented end-of-shape-shutdown signature
(queue_wait≈4 ms, battle_ms=0, total_ms≈90 000 ms, error =
`battle loop timed out waiting for next TurnOpened`). Same edge case
PHASE_D_PREFLIGHT had (2 / 2 125 = 0.09 %); scaled with iter count.

## Infrastructure snapshot (mid-hold)

### docker stats — 3 samples 30 s apart (T1 15:04:55 / T2 15:05:27 / T3 15:05:59)

| Container             | T1 CPU% | T2 CPU% | T3 CPU% | Mem range   |
|-----------------------|---------|---------|---------|-------------|
| kombats-battle        | 59.45   | 57.88   | 55.96   | 224–233 MiB |
| kombats-matchmaking   | 42.12   | 38.71   | 38.16   | 156–160 MiB |
| kombats-postgres      | 27.92   | 27.91   | 25.30   | 129–134 MiB |
| kombats-bff           | 17.25   | 18.57   | 17.26   | 104–106 MiB |
| kombats-players       | 11.00   | 13.71   | 10.06   | 137–139 MiB |
| kombats-chat          |  9.10   |  6.67   |  7.04   | 128–131 MiB |
| kombats-otel-collector|  6.10   |  2.13   |  2.18   | 204–206 MiB |
| kombats-redis         |  4.99   |  5.88   |  4.89   |  23–29 MiB  |
| kombats-jaeger        |  8.42   |  4.47   |  4.89   | 159–171 MiB |
| kombats-rabbitmq      |  3.76   |  3.58   |  3.42   | 138–139 MiB |
| kombats-keycloak      |  0.11   |  0.11   |  0.12   |  518 MiB    |

Steady-state across the window — no container climbs. Battle (max
59 % vs 200 % cap) and Matchmaking (max 42 % vs 150 % cap) have ~3×
headroom.

- **Postgres connections**: 36 / 300 cap (12 %).
- **Redis**: 3 388 ops/sec, DBSIZE 38 338 (active battle/mm state).
- **RabbitMQ**: all listed queues at depth 0 — consumers keeping up.
- **Host swap**: pre `Swapins:0, Swapouts:0`; post `Swapins:0,
  Swapouts:0`. **Zero swap.** Pages free dropped 73 103 → 32 460
  (≈ 159 MiB net), no pressure.

## Comparison to Phase C 25-simul docker stats anchor

| Service      | C avg → S1 avg     | C p95 → S1 max    |
|--------------|--------------------|--------------------|
| battle       | 38.8 → **57.8 %** | 75.1 → 59.5 %     |
| matchmaking  | 28.0 → **39.7 %** | 47.2 → 42.1 %     |
| postgres     | 19.5 → **27.0 %** | 46.2 → 27.9 %     |
| bff          | 18.0 → 17.7 %     | 35.8 → 18.6 %     |
| players      | 10.5 → 11.6 %     | 21.4 → 13.7 %     |
| chat         |  7.9 → 7.6 %      | 16.7 →  9.1 %     |
| redis        |  4.0 → 5.3 %      |  6.6 →  5.9 %     |
| rabbitmq     |  9.0 → **3.6 %**  | 50.4 →  3.8 %     |

The three hot services show ~50 % **higher average** CPU than C but
**lower peaks** than C p95. Explanation: Stage 1 sustained 18.6 RPS vs
C/B1b 17.4 RPS — 5-min hold settles into max sustained throughput,
raising avg while peaks stay bounded by per-container ceiling. Not a
regression. RabbitMQ being lower than C avg (with all queues at depth
0) is likely 10-sample-window noise in C anchor; re-baseline at
Stage 2 if it persists. Memory per container within ±5 % of C anchor.

## Observations for the architect

- **Procedure ran clean on first try.** No re-do, no surprise. The
  RUNBOOK + PHASE_C/D_PREFLIGHT chain is solid as documented.
- **Favorable outlier is steady-state**, not a run-length artefact —
  Stage 2+ should treat any move *toward* Run 0's wider p95 as real
  pressure, not regression to baseline.
- **Postgres at 36 / 300 conns** (12 %). Linear extrapolation: Stage 4
  (200 simul, 2-4 replicas) starts pressing the 300 cap — watch this
  per stage.
- **Battle avg CPU = 58 % of one core** = ~29 % of its 2-core cap.
  Per-replica scaling is the right Stage 3-4 lever, as planned.
- **BattleTimeout signature unchanged.** If Stage 2-4 shows real
  mid-battle timeouts (battle_ms > 0, queue_wait > 6 ms) vs the
  shape-shutdown pattern, that's a real signal.

## How long it took

| Phase                                  | Wall clock |
|----------------------------------------|------------|
| Cold start (down -v → seed-400)        | ~3 min     |
| Step 7 smokes                          | ~30 s      |
| 5-min Locust run                       | 5.5 min    |
| Infra snapshot (overlapped with run)   | 0 (parallel)|
| Aggregate + write report               | ~1 min     |
| **Total**                              | **~10 min** |

Well inside the 30 min budget — Stages 2-6 should each fit comfortably.
