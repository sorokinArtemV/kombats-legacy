# Phase C — Infrastructure Scaling Config Results

## Status

**COMPLETE.** Capacity overlay (resource limits + Postgres
`max_connections=300`) brought up cleanly on a cold-start Mode A stack;
25-simul calibration produced 2 117 successful iterations / 0 errors;
all five tasks verified. Phase D (capacity scan) is unblocked.

## Task results

- **Parametric shape (Task 1)** — `locustfile.py` `Run0Shape` reads
  `RAMP_USERS` / `RAMP_SECONDS` / `HOLD_SECONDS` env vars (defaults
  25/30/90 — Run 0 calibration values, preserved). Verified:
  `RAMP_USERS=50 RAMP_SECONDS=30 HOLD_SECONDS=60` → shape parses to
  `users=50, ramp=30, hold=60, total=90`. With no env vars set the
  shape stays bit-for-bit equivalent to the B1b calibration shape.

- **Resource limits (Task 2)** — `docker-compose.capacity.yml`. Sized
  from the 25-simul `docker stats` baseline (§ next): memory ~3×
  observed-max, CPU ~4× observed-p95 (capped where the host budget
  required). Total budget 10.5 cores / 4 608 MB (host: 12 cores / 7.75
  GB; ~3 GB consumed by unlimited containers). At 25 simul under the
  overlay no container approaches its cap (battle p95 = 0.66 cores vs
  2.0 cap → 3× headroom); limits are GENEROUS at Stage 1 by design,
  PRESENT so a single-container ceiling becomes visible at Stage 3-4.

  | Service     | CPU | Memory | p95 under overlay   |
  |-------------|-----|--------|---------------------|
  | bff         | 1.0 | 512 MB | 0.49 cores / 104 MB |
  | players     | 0.75| 512 MB | 0.28 cores / 146 MB |
  | matchmaking | 1.5 | 512 MB | 0.48 cores / 162 MB |
  | battle      | 2.0 | 768 MB | 0.66 cores / 227 MB |
  | chat        | 0.75| 512 MB | 0.18 cores / 128 MB |
  | postgres    | 2.0 | 768 MB | 0.40 cores / 138 MB |
  | redis       | 1.0 | 256 MB | 0.07 cores / 24 MB  |
  | rabbitmq    | 1.5 | 768 MB | 0.40 cores / 200 MB |

  All 8 limits verified via `docker inspect HostConfig.NanoCpus/.Memory`.

- **Postgres `max_connections=300` (Task 3)** — included in
  `docker-compose.capacity.yml`. Verified after up:
  `docker exec kombats-postgres psql -U postgres -c 'SHOW max_connections;'`
  → `300`. Image default was 100; 4 services × pool 20 = 80 at single-replica,
  Battle replicas cross 100 fast.

- **Replica generator (Task 4)** — `scripts/generate-battle-replicas.py`.
  Verified:
  - N=2 → 47-line overlay; `diff` against the existing
    `docker-compose.multi-replica.yml battle-2:` block reports
    **IDENTICAL**.
  - N=1 → no-op overlay (`services: {}`); the base `battle` service
    provides the single replica.
  - N=4 → 115 lines with 3 extra blocks (battle-2, battle-3, battle-4),
    each carrying the `battle` network alias for DNS round-robin.
  - N=5 → error exit (2) with message pointing to Chapter 5 scope.

- **Seed 400 (Task 5)** — `seed-users.sh --count 400` ran twice:
  incrementally `created=350, existed=50, failed=0` in ~9.5 s, then
  on a cold stack `created=400, existed=0, failed=0` similar runtime.
  Manifest: 400 entries, all `loadbot-*`, indices 0001–0400. Locust
  pool picks them up cleanly. **No Keycloak admin API throttling at
  this count** — ~0.024 s per user. Stage 5-6 (500-1000) is 16-32×
  this — a separate benchmark, see Risks.

## Capacity-overlay smoke

Cold-start Mode A + `docker-compose.capacity.yml`. Single-replica
Battle. 25-simul calibration (defaults: RAMP_USERS unset = 25,
HOLD_SECONDS unset = 90). Iteration log:
`iteration-logs/iterations-2026-05-16--14-31-52.jsonl` — 2 117 rows,
**0 errors / 0 BattleTimeouts**.

| Metric             | Run 0   | ±5 % band       | Overlay run | Δ        | Verdict                |
|--------------------|---------|-----------------|-------------|----------|------------------------|
| ok count           | 2 096   | 1 991 – 2 201   | 2 117       | +1.0 %   | ✅ in band             |
| RPS                | 17.47   | 16.60 – 18.34   | 17.64       | +1.0 %   | ✅ in band             |
| queue_wait_ms p50  | 1 014.7 | 964 – 1 065     | 1 019       | +0.4 %   | ✅ in band             |
| queue_wait_ms p95  | 1 519.4 | 1 443 – 1 595   | 1 052       | −30.8 %  | ⚠️ below band (faster) |
| total_ms p50       | 1 106.0 | 1 051 – 1 161   | 1 111       | +0.5 %   | ✅ in band             |
| total_ms p95       | 1 593.4 | 1 514 – 1 673   | 1 382       | −13.3 %  | ⚠️ below band (faster) |

4 of 6 metrics in band; the 2 out-of-band metrics are on the
**favorable side** (faster than Run 0). This is the same
favorable-direction outlier pattern documented in `PHASE_B1B_RESULTS.md`
for Run #1 ("every metric was either within band or BELOW the lower
bound = faster than Run 0"). The overlay does not throttle the system
at 25 simul — gate passed.

## docker stats baseline at 25 simul

Captured during the pre-overlay calibration (Mode A, no limits — gives
the honest "what does 25 simul actually cost?" number that the limits
were sized from). 10-sample hold-phase window:

| Container             | CPU avg | CPU p95 | Mem avg | Mem max |
|-----------------------|---------|---------|---------|---------|
| kombats-battle        | 38.8 %  | 75.1 %  | 219 MB  | 233 MB  |
| kombats-matchmaking   | 28.0 %  | 47.2 %  | 166 MB  | 172 MB  |
| kombats-bff           | 18.0 %  | 35.8 %  | 139 MB  | 141 MB  |
| kombats-chat          |  7.9 %  | 16.7 %  | 147 MB  | 154 MB  |
| kombats-players       | 10.5 %  | 21.4 %  | 156 MB  | 168 MB  |
| kombats-postgres      | 19.5 %  | 46.2 %  | 128 MB  | 131 MB  |
| kombats-redis         |  4.0 %  |  6.6 %  |  23 MB  |  27 MB  |
| kombats-rabbitmq      |  9.0 %  | 50.4 %  | 168 MB  | 205 MB  |
| kombats-jaeger        |  2.1 %  |  6.0 %  | 1600 MB | 2101 MB |
| kombats-keycloak      |  0.1 %  |  0.2 %  | 1123 MB | 1128 MB |

CPU % is per `docker stats` (one core = 100 %). Sum across services:
~140 % of one core total at 25 simul, well under the 12-core host.
Memory: ~4.4 GB working set total, dominated by jaeger (~1.6 GB
in-memory traces) and keycloak (~1.1 GB JVM) — both unlimited by
design (out of measurement scope; their cost is the host's, not the
app's). This is the reference baseline for Phase D — every later
stage compares per-container CPU/mem against this anchor.

## Surprises / risks for Phase D

- **Stale-data regression when reusing volumes.** A first overlay
  attempt via `down` + `up` (volumes preserved) produced 101 JoinBattle
  "Battle X not found" errors / p95 total_ms = 8 060 ms. Root cause:
  orphaned battle rows from the prior calibration + flushed Redis
  caused TurnDeadlineWorker to collide with newly assigned battles.
  **Not a capacity-overlay regression** — pre-overlay calibration on
  the same dirty state would behave identically. **For Phase D,
  between measurement sessions use `down -v`**, or the Chapter 2.5 D1
  "load → sleep 90s → load" pattern for back-to-back runs.
- **Memory headroom is tight.** Limited services (4.6 GB) + jaeger
  (~2.1 GB peak) + keycloak (~535 MB, grows under token load) +
  observability stack (~500 MB) ≈ 7.7 GB on a 7.75 GB host. Stage 3-4
  should watch `vm_stat` for swap.
- **Keycloak token throughput at 1000 bots is uncharacterized.**
  Seed-400 is admin REST (one call per user, ~0.024 s) — fast at this
  count. Stage 5-6 ROPC grants (500-1000 concurrent token requests)
  is a different workload. Plan §H5 already flags this risk.
- **Jaeger memory growth is unbounded** — reached 2.1 GB in 120 s of
  measurement. Phase D = 30 min total; consider `MEMORY_MAX_TRACES`
  or restarting jaeger between stages to avoid OOM.
- **Favorable-direction outlier.** Smoke's 2 below-band metrics
  (queue_wait p95 −30.8 %, total_ms p95 −13.3 %) reproduce B1b Run
  #1's pattern. If Stage 2-4 total_ms p95 rises sharply, it's the
  real signal — not regression toward Run 0.
- **BFF CPU p95 38 % → 49 %** between no-overlay baseline and clean
  overlay (other containers within ±10 %). Plausibly noise on a
  10-sample window; re-baseline at Stage 2 if BFF scales unexpectedly.

## How to reproduce

```bash
# 1. Cold-start the capacity stack (single-replica Battle)
docker compose \
  -f docker-compose.yml \
  -f observability/docker-compose.observability.yml \
  -f observability/docker-compose.observability.override.yml \
  -f docker-compose.override.yml \
  -f docker-compose.capacity.yml \
  up -d --build
# Then RUNBOOK steps 3-6 (keycloak-bootstrap, migrations, restart 5
# services, seed). The overlay does not change those steps.
./tests/Kombats.LoadTests/scripts/seed-users.sh --count 400

# 2. 25-simul calibration smoke (defaults — Run 0 shape)
cd tests/Kombats.LoadTests/locust
./.venv/bin/locust -f locustfile.py --headless --only-summary
../scripts/aggregate-phases.sh ../iteration-logs/iterations-*.jsonl | head -12

# 3. Phase D stages — env-driven shape, same locustfile
RAMP_USERS=50  HOLD_SECONDS=300 ./.venv/bin/locust -f locustfile.py --headless --only-summary
RAMP_USERS=100 HOLD_SECONDS=300 ./.venv/bin/locust -f locustfile.py --headless --only-summary

# 4. Replica generator for Stage 3-4 (N=2..4)
python3 scripts/generate-battle-replicas.py 2
# Append docker-compose.capacity-replicas.yml to the chain.
```
