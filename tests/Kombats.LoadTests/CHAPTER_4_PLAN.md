# Chapter 4 — Capacity Test + Locust Migration — Plan

## 1. Goal & scope

**Primary goal:** find the first concrete bottleneck of the current single-host stack, and quantify the effect of horizontal Battle scaling at that break point.

**Concrete deliverable for the portfolio:**
> "System sustains N concurrent battles (2N players) on single host. First bottleneck: X at concurrency M, root cause file:line. Scaling Battle to K replicas extends ceiling to N′. Path to 1000 concurrent battles requires Y (named, scoped, out of Chapter 4)."

**In scope:**
- Migrate load tool from NBomber to Locust (Python).
- Infrastructure config: Postgres tuning, Battle replicas, resource limits.
- Capacity scan: 6 ramp stages, stop on first break.

**Out of scope:**
- Any change in `src/Kombats.*` (code fixes belong to Chapter 5+).
- BFF horizontal scaling (process-local `_connections` map blocks this — Chapter 8+).
- Matchmaking horizontal scaling (unsurveyed risk; defer).
- Cloud migration (Chapter 5+ if host ceiling hits first).

## 2. Phases A → F

### Phase A — Plan (this document)

### Phase B0 — `signalrcore` spike (~3h, GATING)

Single Python script. One bot. Against running stack (Mode A). End-to-end:
auth → SignalR connect → `JoinBattle` invoke-with-result → 1 turn submit → BattleEnded.

**Acceptance criterion:** the spike returns a real `BattleSnapshotRealtime` from `JoinBattle` and successfully submits one `SubmitTurnAction` that produces a `TurnResolved` event. No need to play full battle.

**Gate decision after spike:**
- ✅ Works clean → Phase B1 (full migration).
- ⚠️ Works but `invoke_with_result` helper is brittle → Phase B1 with extra hardening time budget.
- ❌ Fundamental incompatibility → STOP, escalate to architect, decide between custom WebSocket implementation or pivot to k6.

### Phase B1 — Full Locust migration (~2 days, conditional on B0 green)

Port `VirtualPlayer.cs` (~380 lines C# orchestrator) + 3 client classes (`KeycloakTokenClient`, `BffHttpClient`, `BattleHubClient`) + `PlayerBehavior.cs` to Python.

**Acceptance criterion:** Stage 1 (25 simul) Locust run produces metrics within ±5% of Run 0 NBomber baseline on: ok count, RPS, total_ms p50/p95, queue_wait p50/p95. If drift > 5% — debug before proceeding.

**Critical port items (do not lose in fresh-eyes port):**
- TurnOpened-before-snapshot race (`VirtualPlayer.cs:130-134`) — seed `turn_ready` from snapshot BEFORE entering wait loop.
- 7-step retry backoff on `Queue.NotReady` / `NoCombatProfile` (`BffHttpClient.cs:86-125`).
- 8-step retry on `JoinBattle` for transient "battle not found".
- Heartbeat loop must be killed in `finally` to prevent leaked greenlets across iterations.

**JSONL output schema:** byte-identical to current (`ts`, `username`, `battle_id`, `outcome`, `error`, `turns_played`, `auth_ms`, `onboard_ms`, `connect_ms`, `queue_wait_ms`, `join_battle_ms`, `battle_ms`, `total_ms`). `aggregate-phases.sh` stays unchanged.

### Phase C — Infrastructure scaling config (D3)

No code changes in `src/Kombats.*`. Compose + config only.

1. **Postgres tuning:** `command: postgres -c max_connections=300` in `docker-compose.yml` (or new override). Current default 100 saturates at battle-2; will block at 3+ replicas.
2. **Battle replica scaling:** small Python generator script `scripts/generate-battle-replicas.py N` that emits `docker-compose.capacity.yml` with N Battle replicas (named services, not `--scale`, to preserve log determinism — matches Chapter 3 decision).
3. **Resource limits:** add `deploy.resources.limits` (CPU + memory) per service in a new `docker-compose.capacity.yml`. Without limits, capacity-scan failures are ambiguous between code-slow and host-contention.

**Smoke test after C:** Mode B + capacity overlay + 4 Battle replicas, run Stage 1 (25 simul). Confirm `aggregate-phases.sh` still produces baseline ±5%. If degraded — infra config introduced regression, debug before scan.

### Phase D — Capacity scan (architect-driven, agent co-pilot)

Run ramp stages manually with observation snapshots between stages. Methodology in §3 below.

### Phase E — Analysis

Write `RUN_6_RESULTS.md`: per-stage table, named bottleneck with file:line or metric reference, scaling-effect measurement.

### Phase F — Chapter close

Short `CHAPTER_4_REPORT.md` (50-80 lines). PR with squash merge into `development`.

## 3. Capacity ramp methodology (also D2 deliverable)

This methodology lives in `tests/Kombats.LoadTests/CAPACITY_METHODOLOGY.md` across all capacity chapters, not just Chapter 4.

### Stages

| Stage | Concurrent battles | Bots | Duration | Battle replicas |
|---|---|---|---|---|
| 1 | 25 | 50 | 5 min | 1 (baseline validation) |
| 2 | 50 | 100 | 5 min | 1 |
| 3 | 100 | 200 | 5 min | 2 |
| 4 | 200 | 400 | 5 min | 2-4 |
| 5 | 500 | 1000 | 5 min | 4 (if reached) |
| 6 | 1000 | 2000 | 5 min | 4 (almost certainly not reached on Mac) |

Each stage = **5 min sustained**, not 2-min burst. Capacity = endurance, not peak.

### Break criteria (stop on FIRST trip)

Code-side breaks:
- **Latency cliff:** `total_ms` p95 > 3× baseline (= > 4.8s).
- **Error rate spike:** fail rate > 5%.
- **Throughput collapse:** matches/sec falls despite increased bot count.

Host-side breaks (different story — "Mac ran out, not code"):
- **Docker Desktop CPU = 100% sustained > 30s.**
- **Available system memory < 500MB / heavy swap.**

If host-side trips first: document as "single-host limit at N simul, code ceiling unknown above this." Cloud migration becomes Chapter 5+ topic.

### At break point — scaling experiment

Before declaring chapter done: re-run the breaking stage with increased Battle replicas (e.g., 1→4). Measure whether scaling helps. Two possible outcomes:

- **Scaling helps:** "Stage N broke on 1 replica, sustains on K replicas." → measure new ceiling.
- **Scaling doesn't help:** "Bottleneck is upstream of Battle (BFF / Postgres / Matchmaking)." → that's the named bottleneck.

Either outcome is valid Chapter 4 result.

### What to monitor each stage (Grafana + docker stats)

- Locust: RPS, latency percentiles, error rate.
- Custom metrics: `active_battles` gauge, queue depth, matches/sec.
- Containers: CPU % and memory per service (`docker stats`).
- Postgres: active connections, slow query log if available.
- Redis: ops/sec, memory.
- RabbitMQ: queue depth, consumer lag.
- Host: Docker Desktop resource usage, swap.

Snapshot all of these between stages (1-2 min cool-down). Co-pilot agent gathers via `docker stats`, Prometheus queries, Postgres `pg_stat_activity`.

## 4. Infrastructure changes (D3 detail)

### New files

- `docker-compose.capacity.yml` — Postgres `max_connections=300`, resource limits per service.
- `scripts/generate-battle-replicas.py` — generator for N Battle replica blocks.
- `tests/Kombats.LoadTests/locust/` — new directory:
  - `locustfile.py` — main scenario + JSONL emitter
  - `virtual_player.py` — bot lifecycle logic
  - `bff_client.py` — REST client
  - `hub_client.py` — SignalR client + `invoke_with_result` helper
  - `behavior.py` — turn action selection
  - `requirements.txt` — `locust`, `signalrcore`, `httpx`, `pyjwt`

### Compose chain for capacity work

```bash
docker compose \
  -f docker-compose.yml \
  -f observability/docker-compose.observability.yml \
  -f observability/docker-compose.observability.override.yml \
  -f docker-compose.override.yml \
  -f docker-compose.multi-replica.yml \
  -f docker-compose.capacity.yml \
  up -d --build
```

Add to README "Running the stack" once stable.

## 5. Success criteria

Chapter 4 is closed when:
1. **Ceiling is known** — first break point identified by concrete metric.
2. **Cause is named** — `file:line` reference if code-related, metric/config reference if infra-related.
3. **Scaling effect is measured** — quantified delta from Battle replica scaling at the break point.
4. **Next chapter is scoped** — the named bottleneck has a sized fix sketched ("push notifications, ~3 weeks, Chapter 5 scope").

Numbers will look something like: "200 simul on single Battle replica → break on BFF polling p95 = 5.2s; scaling Battle to 4 replicas does not help (bottleneck is upstream); fix = push-based queue notifications, Chapter 5."

Not "we hit 1000 simul." That was never the goal of Chapter 4.

## 6. Risk register

| ID | Risk | Mitigation |
|---|---|---|
| H1 | `signalrcore` `invoke_with_result` semantics gap on `JoinBattle` | Phase B0 spike validates before Phase B1 commits 2 days |
| H2 | TurnOpened-before-snapshot race lost in fresh-eyes Python port | Stage 1 ±5% acceptance gate catches this — deadlocks/missed turns would show as queue_wait/battle_ms drift |
| H3 | Host ceiling hits before code ceiling | Documented as valid outcome ("single-host limit reached, cloud migration = Chapter 5+") |
| H4 | Multiple metrics break simultaneously at break stage | Isolate hardest, defer others to subsequent chapters. Resist "fix N things in one chapter" |
| H5 | Keycloak ROPC throughput unmeasured at high concurrency | If auth fails appear at Stage 3-4, treat as separate finding ("Keycloak is the bottleneck, not the game services") |
| H6 | Token cache reentry on `signalrcore` reconnect — Python dict not thread-safe like `ConcurrentDictionary` | Cover in Phase B1 with explicit `threading.Lock` around cache lookup/refresh |

## 7. Out of scope (defer to later chapters)

- BFF push-based notifications (likely Chapter 5).
- BFF horizontal scaling refactor (`_connections` → Redis, Chapter 8+).
- Matchmaking horizontal scaling (Chapter 6+).
- PgBouncer / Postgres read replicas (Chapter 6+ if connection ceiling persistent).
- Redis cluster mode (Chapter 6+ if single-instance ops/sec saturates).
- Cloud migration (Chapter 5+ if host ceiling).

## 8. Ceremony budget

Hard limits on chapter ceremony (lesson from Chapter 2.5):
- This plan: ≤ 200 lines (current: ~170).
- `RUN_6_RESULTS.md`: ≤ 150 lines.
- `CHAPTER_4_REPORT.md`: ≤ 80 lines.
- One recon agent (already done, pre-plan).
- One Phase B0 spike script (single file).
- One Phase B1 migration agent.
- One Phase C infra agent (or architect-direct if small enough).
- Architect drives Phase D scan manually with co-pilot snapshots.

If any deliverable starts exceeding its budget — stop, ask "is this necessary or overkill?" before continuing.
