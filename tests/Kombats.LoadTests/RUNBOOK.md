# Kombats Operations Runbook

Single source of truth for bringing up the Kombats stack for measurement
runs. Supersedes the scattered setup instructions across individual
`RUN_*_SETUP_LOG.md` files (which remain as historical artefacts).

## Audience and use cases

For a new agent doing Chapter 4 capacity work, future Artem after months
of context loss, or a new team member onboarding.

- **Cold-start** — stack up from `docker compose down -v` state.
- **Verification** — confirming a running stack is healthy for measurement.
- **Teardown** — cleanly stopping the stack between sessions.

## Prerequisites

- Docker Desktop running. ~6 GB memory, ~4 CPU recommended.
- macOS bash 3.2 (no associative arrays — the `dns-rotation` script is
  written portably).
- Repo cloned at `/Users/artemsorokin/Desktop/k/Kombats/`.
- `/etc/hosts` contains `127.0.0.1 keycloak` — one-time setup:
  `echo "127.0.0.1 keycloak" | sudo tee -a /etc/hosts`.
- All `docker compose` invocations run from the **repo root**. The
  observability stack uses `${PWD}/observability/...` for volume mounts
  and breaks if invoked from elsewhere.

## Compose chain selection

The three modes are documented in `README.md` "Running the stack"
§A/B/C — pick the chain there. This runbook uses `<chain>` to mean the
Mode A file list; substitute Mode B (adds `docker-compose.multi-replica.yml`)
when running multi-replica. Mode C is host-IDE only, not for measurement.

### Compose gotcha — `docker-compose.override.yml` must be listed explicitly

Compose auto-loads `docker-compose.override.yml` **only when no `-f` is
passed**. The moment any `-f` appears, auto-load is disabled. Mode A
and Mode B always pass multiple `-f` files, so the override MUST be
listed explicitly or its contents silently don't apply. README's chains
already list it — preserve the ordering.

## Cold-start sequence (Mode A)

### Step 1 — Teardown previous state

```bash
docker compose <chain> down -v
```

`-v` wipes named volumes (`postgres_data`, `keycloak_db`, `redis_data`,
`prometheus_data`, `grafana_data`). Required for a new measurement
session. See "Teardown sequence" for when `down` alone suffices.

### Step 2 — Bring up the full stack

```bash
docker compose <chain> up -d --build
```

14 long-running containers + 1 one-shot `kombats-keycloak-bootstrap`
(exits 0 when done). `depends_on: condition: service_healthy` blocks
app services until postgres/redis/rabbitmq report healthy — no manual
wait needed.

`infra/postgres/init/` is mounted at `/docker-entrypoint-initdb.d/` but
is currently empty; EF migrations (Step 4) own all schema creation.

### Step 3 — Wait for Keycloak bootstrap

```bash
docker logs kombats-keycloak-bootstrap | tail -3
# Expect last line: [keycloak-bootstrap] done.
```

Sets `sslRequired=NONE` on Keycloak's `master` realm so `seed-users`
can reach the admin REST API from the host. Background:
`KEYCLOAK_BOOTSTRAP.md`.

### Step 4 — Run database migrations

```bash
# Local-dev defaults — override for other environments.
export POSTGRES_HOST=localhost
export POSTGRES_PORT=5432
export POSTGRES_DB=kombats
export POSTGRES_USER=postgres
export POSTGRES_PASSWORD=postgres
export KEYCLOAK_DB_PASSWORD=keycloak

./scripts/run-migrations.sh
```

Success indicator: `=== All migrations applied successfully ===`.
Applies EF Core migrations for Players → Matchmaking → Battle → Chat in
order. Idempotent; safe to re-run.

### Step 5 — Restart app services post-migration

```bash
docker compose <chain> restart bff players matchmaking battle chat
```

Clears the cosmetic pre-migration startup errors
(`TurnDeadlineWorker` / `InboxCleanupService` racing against schema
before migrations applied). See "Common operational mistakes".

### Step 6 — Seed test users

```bash
./tests/Kombats.LoadTests/scripts/seed-users.sh --count 50
```

Expected: `created=50, existed=0, failed=0`. Writes
`tests/Kombats.LoadTests/users-manifest.json` with 50 `loadbot-*`
records. Idempotent. Ad-hoc alternative:
`cd tests/Kombats.LoadTests && dotnet run -- seed-users --count 50`.

### Step 7 — Smoke checks

Run all four — they verify distinct layers (HTTP, end-to-end,
observability, Redis baseline).

```bash
# 7a — HTTP layer (all 200; BFF uses /health, others /health/ready)
curl -s -o /dev/null -w 'players:     %{http_code}\n' http://localhost:5001/health/ready
curl -s -o /dev/null -w 'matchmaking: %{http_code}\n' http://localhost:5002/health/ready
curl -s -o /dev/null -w 'battle:      %{http_code}\n' http://localhost:5003/health/ready
curl -s -o /dev/null -w 'chat:        %{http_code}\n' http://localhost:5004/health/ready
curl -s -o /dev/null -w 'bff:         %{http_code}\n' http://localhost:5000/health

# 7b — End-to-end via bot harness
cd tests/Kombats.LoadTests
dotnet run -- single-bot   # auth + onboard + SignalR + queue + leave (~10s)
dotnet run -- smoke        # one full bot pair battle (~1s clean)
cd ../..

# 7c — Observability (D6 protection)
curl -s http://localhost:3001/api/health                                          # Grafana 200
docker run --rm --network kombats_default curlimages/curl:8.10.1 \
  -s http://otel-collector:8889/metrics | grep -oE 'service_name="[^"]+"' | sort -u
# Expect all 5: bff, players, matchmaking, battle, chat

# D2 defensive WARN audit — must be silent on a correct chain
for svc in kombats-bff kombats-players kombats-matchmaking kombats-battle kombats-chat; do
  echo "$svc: $(docker logs $svc 2>&1 | grep -c '\[WARN\] Kombats.Observability' || true)"
done
# All zero. Any non-zero → compose chain is wrong (missing observability override).

# 7d — Redis clean baseline (all zero after Step 1's down -v)
docker exec kombats-redis redis-cli DBSIZE
docker exec kombats-redis redis-cli --scan --pattern 'battle:state:*' | wc -l
docker exec kombats-redis redis-cli --scan --pattern 'mm:player:*' | wc -l
```

### Step 8 — Mode B only: DNS rotation gate

```bash
./tests/Kombats.LoadTests/scripts/dns-rotation-check.sh
```

Exit 0 = rotation confirmed (≥2 distinct Battle replica IPs across 15
probes). Exit 1 = only 1 replica answered (wiring broken). Exit 2 =
probe failures. Hard gate for Mode B before measurement.

### Step 9 — Ready signal

Ready for measurement when all of: `[keycloak-bootstrap] done.`,
migrations applied, 5-service restart complete, 50 users seeded, all
Step 7 smokes pass, Redis baseline = 0. Mode B additionally requires
Step 8 exit 0.

## Tuning hooks

Insertion points for common config changes.

- **Postgres tuning** — `docker-compose.capacity.yml` (Chapter 4 Phase
  C) sets `command: postgres -c max_connections=300` on the postgres
  service (image default = 100). Add the overlay between Step 2 (up)
  and Step 4 (migrations); migrations run against the higher-cap
  Postgres without further changes. Verify after Step 2:
  `docker exec kombats-postgres psql -U postgres -c 'SHOW max_connections;'`
  returns 300. To override further, add a later-in-chain compose file.
- **Multi-replica scaling — 1 to 4 Battle replicas** —
  `scripts/generate-battle-replicas.py N` (Chapter 4 Phase C) emits
  `docker-compose.capacity-replicas.yml` with N named Battle services
  (deterministic logs — NOT `docker compose --scale`). Stage 3+ of the
  capacity scan uses N≥2. Run BEFORE Step 2; append the file to the
  compose chain. Cap is N=4; Stages 5-6 (>4 replicas) belong to
  Chapter 5. `docker-compose.multi-replica.yml` (Chapter 3) is the
  hand-written precursor for N=2 — equivalent to `generate-battle-replicas.py 2`.
- **Resource limits** (CPU / memory caps) — `docker-compose.capacity.yml`
  (Chapter 4 Phase C) sets `deploy.resources.limits` on bff, players,
  matchmaking, battle, chat, postgres, redis, rabbitmq. Sizing baseline
  is captured in `PHASE_C_RESULTS.md`. Apply BEFORE Step 2 by appending
  the overlay; verify per-container via
  `docker inspect --format '{{.HostConfig.NanoCpus}} {{.HostConfig.Memory}}' <name>`.
- **Capacity-scan compose chain** — full chain for Phase D measurement:
  ```
  docker compose \
    -f docker-compose.yml \
    -f observability/docker-compose.observability.yml \
    -f observability/docker-compose.observability.override.yml \
    -f docker-compose.override.yml \
    -f docker-compose.capacity.yml \
    [-f docker-compose.capacity-replicas.yml] \
    up -d --build
  ```
  Append `docker-compose.capacity-replicas.yml` once `generate-battle-replicas.py`
  has produced it for the desired N.
- **Capacity scan ramp parameters** — `tests/Kombats.LoadTests/locust/locustfile.py`
  reads `RAMP_USERS` / `RAMP_SECONDS` / `HOLD_SECONDS` env vars
  (defaults 25/30/90 reproduce Run 0 calibration). Phase D stages use
  `HOLD_SECONDS=300` (5 min sustained) and `RAMP_USERS={25,50,100,200}`
  per `CAPACITY_METHODOLOGY` in `CHAPTER_4_PLAN.md` §3.
- **Telemetry endpoint** — `OpenTelemetry__OtlpEndpoint` env var per
  service. Base value (`http://otel-collector:4317`) from
  `observability/docker-compose.observability.override.yml`. Point
  elsewhere via later-in-chain overlay.
- **Jaeger trace retention** — `MEMORY_MAX_TRACES` env var on the
  `jaeger` service in `observability/docker-compose.observability.yml`
  (current `3000` ≈ ~180 MB plateau / ~3 min of history at 25 simul;
  prevents the unbounded in-memory growth that hit 2.1 GB during the
  Phase C calibration). Lower to tighten host budget, raise for longer
  trace history; rationale in `PHASE_D_PREFLIGHT_RESULTS.md`.
- **Battle Redis state TTL** — `Battle__Redis__StateTtlAfterEnd` env
  var on the `battle` service. Production default `01:00:00`
  (`appsettings.json:42`). Override pattern: `RUN_5_SETUP_LOG.md` §3.

## Teardown sequence

### Between runs in the same session — do nothing

The whole point of Chapter 2.5 D1. The TTL fix on `battle:state:*` keys
plus Postgres `battle.battles` row carry-over make sequential runs
viable without intervening teardown. Sequence: load → sleep 90s (lets
TTL fire) → load → ... See `CHAPTER_2_5_REPORT.md` for the proof.

### Soft stop (`down`, preserve volumes)

```bash
docker compose <chain> down
```

Rare. Pausing work with state preserved for debugging. Volumes survive;
containers and the bridge network do not.

### Hard teardown (`down -v`, wipe volumes)

```bash
docker compose <chain> down -v
```

Start of a new measurement session. Wipes all named volumes. Mode B
teardown must include `-f docker-compose.multi-replica.yml` in the
chain so `battle-2` is removed cleanly.

### What survives vs what gets wiped

- **Survives `down`**: all named volumes (Postgres, Keycloak DB, Redis,
  Prometheus, Grafana), iteration logs on the host
  (`tests/Kombats.LoadTests/iteration-logs/*.jsonl`),
  `users-manifest.json`.
- **Wiped by `down -v`**: all named volumes. After this, Step 4
  recreates schemas; Step 6 recreates users; the manifest gets fresh
  `sub` UUIDs → old JWTs become invalid (`KEYCLOAK_BOOTSTRAP.md`
  recovery section).

## Common operational mistakes

### D6 — missing observability override file

**Symptom:** Grafana empty; `otel-collector:8889/metrics` has no `.NET`
`service_name` labels; Prometheus `process_cpu_count` returns `[]`.
**Cause:** `observability/docker-compose.observability.override.yml`
missing from the chain → empty `OpenTelemetry__OtlpEndpoint` → silent
discard of all telemetry. **Fix:** add the file (full Mode A chain in
Step 2), `down -v` + clean rebuild (`RUN_4_SETUP_LOG.md` §11 used a
Level 3 repair). Chapter 2.5 D2 added a startup WARN that smoke 7c
picks up.

### Worker race with migrations (cosmetic)

**Symptom:** `relation "battle.outbox_state" does not exist` /
`relation "battle.battles" does not exist` in Battle logs near
container start. **Cause:** EF background workers query schema on
startup; if Step 4 runs after Step 2 (which it does), schema is missing
for ~60s. Step 5's restart clears it. **Worry only if** errors continue
after the Step 5 restart — that's a real defect.

### Stale stack (pre-Chapter-2.5 — mitigated)

**Symptom (historical):** throughput decays monotonically from baseline
peak to near-zero within ~60s. **Cause:** Redis `battle:state:*` keys
accumulated without TTL across prior runs (`RUN_0_BASELINE.md` §5,
`RUN_0_DRIFT_INVESTIGATION.md`). **Mitigated by Chapter 2.5 D1.**
Should not recur unless the TTL fix is disabled.

### BFF /health vs /health/ready

`/health/ready` is the convention for Players/Matchmaking/Battle/Chat.
BFF registers a single `MapHealthChecks("/health")`. Step 7a probes
each at its correct path.

### Sticky `battle:state:*` keys (accepted housekeeping cost)

~0.26% of ended battles leak past TTL because `KeyExpireAsync` was
cancelled via `TaskCanceledException` before reaching Redis. Predicted
non-atomic-window cost of the C# wrapper architectural decision (vs
Lua). See `CHAPTER_2_5_REPORT.md` "Architectural decision". Accepted;
do not "fix" without re-reading the rationale.

## Verification commands quick reference

```bash
# Container inventory (Mode A: 14 Up + 1 Exited 0; Mode B: 15 Up + 1 Exited 0)
docker ps -a --filter name=kombats --format 'table {{.Names}}\t{{.Status}}'

# Redis health
docker exec kombats-redis redis-cli DBSIZE
docker exec kombats-redis redis-cli --scan --pattern 'battle:state:*' | wc -l
docker exec kombats-redis redis-cli --scan --pattern 'mm:player:*' | wc -l

# SignalR backplane (Mode B) — NUMSUB=2 expected
docker exec kombats-redis redis-cli PUBSUB NUMSUB \
  Kombats.Battle.Infrastructure.Realtime.SignalR.BattleHub:all \
  Kombats.Battle.Infrastructure.Realtime.SignalR.BattleHub:internal:groups

# TTL inspection (sample any battle:state:* key)
docker exec kombats-redis redis-cli --scan --pattern 'battle:state:*' | head -1 | \
  xargs -I{} docker exec kombats-redis redis-cli TTL {}

# Battle service env (verify TTL override applied)
docker exec kombats-battle env | grep -i statettl

# Prometheus series presence
curl -s --data-urlencode 'query=process_cpu_count' http://localhost:9090/api/v1/query | \
  jq -r '.data.result[].metric.service_name' | sort -u

# Grafana
curl -s http://localhost:3001/api/health
```

## What this runbook does NOT cover

- Production deployment to Azure (`pipelines/*.yml`, `infra/main.bicep`).
- Specific measurement methodology — see `CAPACITY_METHODOLOGY.md` when
  it exists.
- NBomber `load` command — being replaced by Locust for Chapter 4. The
  `smoke` and `single-bot` harness commands remain canonical for Step 7b.
- Per-chapter setup deviations — see historical `RUN_*_SETUP_LOG.md`
  for run-specific anomalies, repairs (e.g. Run 4 §11 D6), and one-time
  overrides (e.g. Run 5 §3 TTL override).
