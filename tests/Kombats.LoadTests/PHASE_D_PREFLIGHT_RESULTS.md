# Phase D Pre-flight — Jaeger Bound + Headroom Verification

## Verdict

**GREEN.** Jaeger plateau ~180 MiB (vs ~2.1 GB unbounded); host swap
delta 0/0 across the 25-simul run; 6-metric calibration in band (same
favorable-direction pattern as Phase C). Phase D is cleared to start.

## Jaeger change

- **`MEMORY_MAX_TRACES: "3000"`** added to the `jaeger` service in
  `observability/docker-compose.observability.yml` (alongside the
  existing `COLLECTOR_OTLP_ENABLED`; the override file is service-side
  env routing only — Jaeger lives in the base observability file).
- **Back-of-envelope:** Phase C unbounded → 2.1 GB / ~2000 iter ×
  ~5 traces/iter ≈ ~200 KB per trace; 3000 traces → target ~600 MB.
  Empirical per-trace cost turned out to be ~60 KB (3000 × 60 KB ≈
  180 MB), so the cap is well under the 600-800 MB target and could be
  loosened later if more trace history is wanted. For Phase D this is
  conservatively safe — verification trumps trace volume.
- **Retention horizon at 25 simul:** 3000 traces ≈ 3 min of history at
  steady RPS; ≈ 90 s at 50 simul; ≈ 45 s at 100 simul. Enough to
  inspect a stage's tail latency without crossing stage boundaries.
- **Scope kept minimal:** one env var on the Jaeger service. Sampling
  rates, OTLP collector config, and `.NET` service-side telemetry export
  untouched — full traces still flow during each stage; only retention
  is bounded.

## Headroom under 25 simul

**Iteration log:** `iterations-2026-05-16--14-49-36.jsonl` (2125 rows).

**Jaeger memory (per-second `docker stats` over the 120 s run):**

- pre-load idle: **67 MiB**
- peak during ramp/hold: **184.2 MiB**
- steady-state plateau over hold phase: **~160-180 MiB** (oscillating
  ±10 MB as old traces evict — plateaued, not climbing monotonically)
- post-run: **~162 MiB**

The cap is engaging: under unbounded retention this same workload
reached 2.1 GB in 120 s (Phase C). With `MEMORY_MAX_TRACES=3000` the
working set stabilises at ~10× lower — ample headroom against a
30 min Phase D scan.

**Host swap (`vm_stat` before/after, cumulative counters):**

- Pre-run: `Swapins: 0`, `Swapouts: 0`
- Post-run: `Swapins: 0`, `Swapouts: 0`
- **Delta: 0 / 0.** Zero swap activity during the entire run.

**Total container working set (sample 25, mid-hold @ 14:50:46):**

| Container             | Memory   |
|-----------------------|----------|
| kombats-bff           | 101.0    |
| kombats-chat          | 125.8    |
| kombats-grafana       | 121.7    |
| kombats-players       | 145.5    |
| kombats-matchmaking   | 160.1    |
| kombats-battle        | 222.8    |
| kombats-prometheus    |  77.1    |
| kombats-keycloak      | 560.8    |
| kombats-otel-collector| 108.9    |
| kombats-keycloak-db   |  44.0    |
| kombats-postgres      | 130.2    |
| kombats-redis         |  17.3    |
| kombats-rabbitmq      | 130.4    |
| kombats-jaeger        | 162.4    |
| **Total**             | **~2107 MiB** |

Host budget 7.75 GiB → **~5.6 GiB headroom** at 25 simul. Phase C
unbounded baseline put the same set at ~4.4 GiB working set
(jaeger 1.6-2.1 GB dominated); bounding Jaeger has freed ~1.4-2.0 GiB.

**25-simul calibration vs Run 0 ±5 % band:**

| Metric             | Run 0   | ±5 % band     | Pre-flight  | Δ        | Verdict                |
|--------------------|---------|---------------|-------------|----------|------------------------|
| ok count           | 2 096   | 1 991 - 2 201 | 2 123       | +1.3 %   | ✅ in band             |
| RPS                | 17.47   | 16.60 - 18.34 | 17.69       | +1.3 %   | ✅ in band             |
| queue_wait_ms p50  | 1 014.7 | 964 - 1 065   | 1 014       | -0.1 %   | ✅ in band             |
| queue_wait_ms p95  | 1 519.4 | 1 443 - 1 595 | 1 036       | -31.8 %  | ⚠️ below band (faster) |
| total_ms p50       | 1 106.0 | 1 051 - 1 161 | 1 080       | -2.4 %   | ✅ in band             |
| total_ms p95       | 1 593.4 | 1 514 - 1 673 | 1 213       | -23.9 %  | ⚠️ below band (faster) |

4 of 6 in band; the 2 below-band metrics are on the **favorable side**
and reproduce the exact pattern documented in PHASE_C_RESULTS.md and
PHASE_B1B_RESULTS.md Run #1 ("favorable-direction outlier"). Bounding
Jaeger has not coupled into Kombats latency — as expected.

**Note on 2 BattleTimeouts** (2 / 2125 = 0.09 %): both have
`queue_wait_ms=2` / `battle_ms=0` / `total_ms=89999`, i.e. they were
paired right before locust shape shutdown and their partner stopped
mid-battle. Same end-of-run shape-shutdown edge case the harness has
exhibited intermittently; not a regression, does not move p95.

## Assessment for Phase D

The host is **comfortable for a 6-stage scan**. The two Phase C risks
linked at the head of this task are both retired by the Jaeger bound
alone — no further trimming required:

1. **OOM risk (Jaeger climbing to 2.1 GB across 30 min)** — gone:
   Jaeger plateaus at ~180 MiB regardless of run length.
2. **Memory-headroom tightness (~7.7 GB on 7.75 GB budget at
   Stage 1)** — gone: total working set is now ~2.1 GiB / 7.75 GiB,
   ~5.6 GiB headroom. Even if Stages 2-4 triple per-container working
   sets (already-generous overlay caps should prevent this), the host
   stays comfortably non-swapping.

The remaining Phase C risks are unaffected by this work and remain
in their original posture:

- **Stale-data regression when reusing volumes** — addressed by
  `down -v` between sessions; this pre-flight used `down -v`.
- **Keycloak token throughput at 1000 bots** — Stage 5-6 concern only,
  out of scope for Chapter 4 (Stages 1-4).
- **Favorable-direction outlier** — same as Phase C; if Stage 2-4
  total_ms p95 rises sharply, treat as real signal, not regression
  toward Run 0.

No YELLOW: nothing to trim, no need to drop the Jaeger ceiling
further, no need to compact the observability stack.

## How to reproduce

```bash
# Repo root.

# 1. Cold-start the capacity stack with bounded Jaeger
docker compose \
  -f docker-compose.yml \
  -f observability/docker-compose.observability.yml \
  -f observability/docker-compose.observability.override.yml \
  -f docker-compose.override.yml \
  -f docker-compose.capacity.yml \
  down -v
docker compose \
  -f docker-compose.yml \
  -f observability/docker-compose.observability.yml \
  -f observability/docker-compose.observability.override.yml \
  -f docker-compose.override.yml \
  -f docker-compose.capacity.yml \
  up -d --build
# Then RUNBOOK steps 3-6 (keycloak-bootstrap, migrations, restart 5
# services, seed 400). The Jaeger env change does not alter those steps.
./tests/Kombats.LoadTests/scripts/seed-users.sh --count 400

# 2. Verify the cap applied
docker inspect kombats-jaeger --format \
  '{{range .Config.Env}}{{println .}}{{end}}' | grep MEMORY_MAX
# → MEMORY_MAX_TRACES=3000

# 3. 25-simul calibration smoke (defaults — Run 0 shape, ~120 s)
cd tests/Kombats.LoadTests/locust
./.venv/bin/locust -f locustfile.py --headless --only-summary
../scripts/aggregate-phases.sh ../iteration-logs/iterations-*.jsonl | head -12

# 4. Headroom capture during the run (separate terminal)
#    - vm_stat before, vm_stat after — diff Swapins/Swapouts
#    - per-second docker stats sampling against kombats-jaeger
```
