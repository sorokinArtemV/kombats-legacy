# Run 5 Setup Log — Chapter 2.5 Sustainable Run (Redis state TTL hardening)

## 0. Header

- **Date / time:** 2026-05-14, setup window 13:36 → 13:56 +05:00 (Asia/Yekaterinburg) / 08:36 → 08:56 UTC.
- **Branch:** `feat/redis-ttl-hardening`.
- **HEAD commit at setup start:** `7faf81d3dcb103a9d38a42a445c8923443d709c7` (`docs: document canonical docker compose chains`).
  HEAD chain (latest 5):
  ```
  7faf81d docs: document canonical docker compose chains                                  ← D3
  eded768 feat(battle, observability): wire Redis state TTL + OTLP defensive WARN         ← D1 + D2
  3d30c71 feat(battle): SignalR Redis backplane + skip-negotiation — Chapter 3
  120ceef docs(loadtests): Run 2 — multi-replica without backplane (failure proof)
  1958389 docs: chapter 3 planning and investigation reports
  ```
- **Working-tree state at setup start:** clean (no uncommitted changes).
- **Working-tree state at setup end:** one untracked file — `docker-compose.ttl-override.yml` (transient, see §3). No source-code edits, no committed changes.
- **Stack composition at setup completion:** 14 long-running containers Up + 1 one-shot `kombats-keycloak-bootstrap` (Exited 0). Matches Run 0 / Run 4 single-replica Mode A composition.
- **Purpose:** Phase C — prepare the stack for the architect-driven Sustainable Run: 4 sequential 25-bot load runs, no `down -v` between them, TTL overridden from production 1 h to 60 s, 90 s sleep between runs. Setup phase only (steps 1–9); load measurement is the architect's manual Phase D step.
- **Things NOT done in this setup** (per Phase-C prompt's explicit "will-not" list):
  - No NBomber / load test runs.
  - No source-code edits in `src/Kombats.*`.
  - No edits to `appsettings.json` (production default stays at `01:00:00`).
  - No commits.
  - No modifications to the canonical Mode A compose chain in `README.md`.
  - No `BattleRecoveryWorker` threshold changes.

---

## 1. Step-by-step timestamps and outputs

| Step | Action | Time (Asia/Yekaterinburg, +0500) | Result |
|---|---|---|---|
| 1 | `git rev-parse --abbrev-ref HEAD` / `git log --oneline -5` / `git status --short` | 13:36 | ✅ `feat/redis-ttl-hardening`, expected D1+D2 / D3 at top of log, clean tree |
| 2 | Grep TTL fix in `RedisBattleStateStore.cs` + `appsettings.json` | 13:36 | ✅ `KeyExpireAsync` at line 277 gated on `EndedNow && StateTtlAfterEnd.HasValue` (line 275). `appsettings.json:42` shows `"StateTtlAfterEnd": "01:00:00"` (production default, not test override) |
| 3 | `docker compose ... down -v` with **canonical Mode A chain** (4 files) | 13:37 → 13:38 | ✅ All containers + 5 volumes + network removed. `docker ps -a --filter name=kombats` empty afterwards |
| 4 | Create `docker-compose.ttl-override.yml` at repo root | 13:38 | ✅ File created, untracked, not gitignored (per prompt: transient — manually removed after chapter closes) |
| 5 | `docker compose ... up -d --build` with **augmented Mode A chain** (5 files incl. ttl-override) | 13:38 → 13:47 | ✅ 14 long-running containers + 1 one-shot bootstrap (exited 0). Battle container's `StartedAt` after the §6 restart: `2026-05-14T08:47:13Z` |
| 6 | `./scripts/run-migrations.sh` (standard env) → `restart bff players matchmaking battle chat` | 13:46 → 13:47 | ✅ `=== All migrations applied successfully ===`; 5-service restart returned each container `Started` |
| 7 | Keycloak bootstrap verify + `dotnet run -- seed-users` | 13:48 | ✅ `[keycloak-bootstrap] done.`; master-realm token endpoint HTTP 200; kombats realm `.well-known/openid-configuration` HTTP 200; `created=50, existed=0, failed=0`; manifest 50 entries (25 pairs) |
| 8.a | HTTP smoke (5-service `/health/ready` + `single-bot` end-to-end) | 13:53 → 13:54 | ✅ Players/Matchmaking/Battle/Chat `/health/ready` = 200; BFF `/health` = 200; `single-bot` clean: auth 64 ms, onboard 294 ms, SignalR connect 52 ms, join-queue OK, leave-queue OK |
| 8.b | Observability smoke (D6 protection — Grafana + D2 WARN absence) | 13:54 | ✅ Grafana `/api/health` 200; otel-collector `:8889/metrics` shows all 5 services (`bff`, `players`, `matchmaking`, `battle`, `chat`) exporting; `[WARN] Kombats.Observability` count = 0 across all 5 services |
| 8.c | TTL env override visible to Battle container | 13:54 | ✅ `docker exec kombats-battle env \| grep StateTtl` → `Battle__Redis__StateTtlAfterEnd=00:01:00` |
| 8.d | Redis clean baseline | 13:55 | ✅ `DBSIZE = 0`; `battle:state:*` count = 0; `mm:player:*` count = 0 |
| 9 | Write this setup log | 13:56 | ✅ |

---

## 2. TTL fix verification (Phase 2)

### 2.1 RedisBattleStateStore.cs

```
$ grep -n "KeyExpireAsync\|StateTtlAfterEnd" \
    src/Kombats.Battle/Kombats.Battle.Infrastructure/State/Redis/RedisBattleStateStore.cs
275:        if (commitResult == EndBattleCommitResult.EndedNow && _options.StateTtlAfterEnd.HasValue)
277:            await db.KeyExpireAsync(key, _options.StateTtlAfterEnd.Value);
```

Surrounding context at `RedisBattleStateStore.cs:268–278`:

```csharp
// State TTL applied as a separate call after Lua SET, not atomically inside the script.
// Rationale: Ended battles are terminal — no reader depends on the TTL being present
// the instant SET completes. The brief non-atomic window (< 1 RTT) is acceptable
// because TTL here is housekeeping (Redis-side cleanup), not consistency.
// Atomic alternative would require extending EndBattleAndMarkResolvedScript with a
// sentinel for nullable TTL — deferred as unnecessary complexity.
// See CHAPTER_2_5_REPORT.md "Architectural decision — TTL via C# wrapper".
if (commitResult == EndBattleCommitResult.EndedNow && _options.StateTtlAfterEnd.HasValue)
{
    await db.KeyExpireAsync(key, _options.StateTtlAfterEnd.Value);
}
```

Both gates required:
- `commitResult == EndedNow` — TTL applied only on the first end (idempotent re-end is silent).
- `_options.StateTtlAfterEnd.HasValue` — TTL is opt-in; absent value means no Redis-side cleanup (Battle keeps state until external mechanism removes it).

### 2.2 appsettings.json — production default

```
$ grep -n "StateTtlAfterEnd" src/Kombats.Battle/Kombats.Battle.Bootstrap/appsettings.json
42:      "StateTtlAfterEnd": "01:00:00"
```

Production default = 1 hour. **Source-of-truth file untouched** by this setup; the Sustainable Run 60-second override is injected at the docker-compose env-var layer only (§3).

---

## 3. TTL override mechanism — `docker-compose.ttl-override.yml`

### 3.1 Why a transient compose overlay (not appsettings.json edit)

The Sustainable Run needs `StateTtlAfterEnd = 60 s` so the TTL fires within the measurement window (4 × ~5–7 minute runs with 90 s sleeps). Editing `src/Kombats.Battle/Kombats.Battle.Bootstrap/appsettings.json` would:
1. Pollute the production default into the git history (risk of merge-time bleed).
2. Require a revert commit after the chapter closes.
3. Break the architectural intent: the production value (1 h) is correct for production and should remain visible in `appsettings.json` as the canonical source of truth.

A docker-compose env-var overlay is precisely the right level: .NET `Microsoft.Extensions.Configuration` reads env vars on top of `appsettings.*.json` files, with the env layer winning. The JSON path `Battle:Redis:StateTtlAfterEnd` maps to the env var `Battle__Redis__StateTtlAfterEnd` (the `:` → `__` convention).

### 3.2 File contents

`docker-compose.ttl-override.yml` (at repo root, untracked):

```yaml
# TRANSIENT FILE — Chapter 2.5 Sustainable Run only.
#
# Purpose: override Battle:Redis:StateTtlAfterEnd from production default 01:00:00
# down to 00:01:00 (60s) so the Redis TTL fix can be observed firing within the
# measurement window of 4 sequential 25-bot runs.
#
# The source-of-truth value in src/Kombats.Battle/Kombats.Battle.Bootstrap/appsettings.json
# stays at "01:00:00" — only this compose override is changed.
#
# This file must NOT be committed. Delete after the Chapter 2.5 measurement closes.
# Add to the Mode A chain at the END so it wins last-write merge:
#   docker compose -f docker-compose.yml \
#     -f observability/docker-compose.observability.yml \
#     -f observability/docker-compose.observability.override.yml \
#     -f docker-compose.override.yml \
#     -f docker-compose.ttl-override.yml \
#     up -d --build
services:
  battle:
    environment:
      - Battle__Redis__StateTtlAfterEnd=00:01:00
```

### 3.3 Verification of merge semantics

```
$ docker compose -f docker-compose.yml \
    -f observability/docker-compose.observability.yml \
    -f observability/docker-compose.observability.override.yml \
    -f docker-compose.override.yml \
    -f docker-compose.ttl-override.yml \
    config | grep -E "StateTtl|OtlpEndpoint" | head -2
Battle__Redis__StateTtlAfterEnd: "00:01:00"
OpenTelemetry__OtlpEndpoint: http://otel-collector:4317
```

Both the TTL override and the OTel endpoint (from the observability override) merge cleanly into the Battle service environment. Note the TTL override file is **last in the chain**, so any prior file's `Battle__Redis__StateTtlAfterEnd` would be overwritten — currently no prior file sets that key, so the override is additive, but the ordering is defensive.

### 3.4 Transience and cleanup

- Untracked: `git status --short` returns only `?? docker-compose.ttl-override.yml`.
- Not gitignored: `git check-ignore docker-compose.ttl-override.yml` exits 1 (no match).
- Per Phase-C prompt: do not commit, do not add to `.gitignore`. **Manual removal expected after the chapter closes.**

---

## 4. Compose chain used (Phase 3 + Phase 5)

Identical to Mode A from `README.md` lines 11–18, plus the TTL override file appended at the end:

```bash
docker compose \
  -f docker-compose.yml \
  -f observability/docker-compose.observability.yml \
  -f observability/docker-compose.observability.override.yml \
  -f docker-compose.override.yml \
  -f docker-compose.ttl-override.yml \
  up -d --build
```

Teardown (Phase 3) used the same chain minus the ttl-override file (the override file wasn't yet created at teardown time, but it produces no services — including or excluding it for `down -v` is equivalent).

---

## 5. Smoke check results (Phase 8)

### 5.1 — 8.a HTTP smoke (PASS)

```
--- Players health/ready ---       HTTP 200
--- Matchmaking health/ready ---   HTTP 200
--- Battle health/ready ---        HTTP 200
--- Chat health/ready ---          HTTP 200
--- BFF /health ---                HTTP 200
```

End-to-end via `dotnet run -- single-bot`:

```
13:53:47.142 [probe] target: http://localhost:5000, user: loadbot-0001
13:53:47.250 [probe] token acquired in 64 ms
13:53:47.544 [probe] onboard in 294 ms — state=Draft revision=1
13:53:47.614 [probe] set-name done
13:53:47.716 [probe] allocate-stats done
13:53:47.769 [probe] SignalR connected in 52 ms
13:53:49.386 [probe] join-queue result: status=Searching matchId=(null) battleId=(null)
13:53:59.413 [probe] leave-queue done
```

**Verdict: PASS.** Auth + onboard + SignalR + matchmaking queue path all clean. No exceptions, no aborts.

(Note: BFF surface is `/health` rather than `/health/ready` — the BFF host registers a single health endpoint. Other services use `/health/ready`. Both forms return 200; no functional difference for smoke.)

### 5.2 — 8.b Observability smoke (PASS — D6 protection holds)

```
--- Grafana /api/health ---
{"database":"ok","version":"11.4.0","commit":"b58701869e1a11b696010a6f28bd96b68a2cf0d0"}
```

otel-collector `:8889/metrics` confirms all 5 services pushing telemetry (verbatim labels from the scrape output):

```
service_name="bff",          service_instance_id="3099ce77-…"
service_name="players",      service_instance_id="5e68d8ec-…"
service_name="matchmaking",  service_instance_id="0497a5b6-…"
service_name="battle",       service_instance_id="4e7892d9-…"
service_name="chat",         service_instance_id="37ede45b-…"
```

D2 WARN audit across all 5 .NET services:

```
$ for svc in kombats-bff kombats-players kombats-matchmaking kombats-battle kombats-chat; do
    echo "$svc: $(docker logs $svc 2>&1 | grep -c 'WARN.*Kombats.Observability')"
  done
kombats-bff:          0
kombats-players:      0
kombats-matchmaking:  0
kombats-battle:       0
kombats-chat:         0
```

**Verdict: PASS.** Compose chain is correct (override file `observability/docker-compose.observability.override.yml` is in the chain), so `OpenTelemetry:OtlpEndpoint` is populated, the OTLP exporter attaches, and D2's defensive WARN stays silent — exactly the expected behavior on a correct config. The D6 protection is validated: the WARN would fire on a wrong chain (e.g., the Run 1–4 misconfiguration documented in `RUN_4_SETUP_LOG.md` §11), and its silence here proves the chain is right.

### 5.3 — 8.c TTL override env var smoke (PASS)

```
$ docker exec kombats-battle env | grep -i statettl
Battle__Redis__StateTtlAfterEnd=00:01:00
```

**Verdict: PASS.** The override file's env var reached the Battle container as the test value (`00:01:00`), not the production default (`01:00:00`). Confirms the docker-compose merge wired through and the .NET configuration provider will see `60 s` when binding `_options.StateTtlAfterEnd`.

### 5.4 — 8.d Redis empty-state smoke (PASS)

```
$ docker exec kombats-redis redis-cli DBSIZE
0
$ docker exec kombats-redis redis-cli --scan --pattern 'battle:state:*' | wc -l
       0
$ docker exec kombats-redis redis-cli --scan --pattern 'mm:player:*' | wc -l
       0
```

**Verdict: PASS.** Truly clean baseline — not just zero `battle:state:*` keys, but zero keys total. `down -v` in §3 removed the Redis volume, and nothing has populated the DB since (the smoke `single-bot` doesn't enter battle, only queue, and queue cleanup leaves no `mm:player:*` lease keys behind).

### 5.5 — Smoke pass/fail count

| # | Smoke | Result |
|---|---|---|
| 8.a | HTTP + single-bot end-to-end | ✅ PASS |
| 8.b | Grafana healthy + 5 services exporting + 0 D2 WARN | ✅ PASS |
| 8.c | TTL env override visible to Battle container | ✅ PASS |
| 8.d | Redis clean baseline (DBSIZE=0, battle:state:*=0) | ✅ PASS |

**4/4 PASS.**

---

## 6. Stack state at setup completion

`docker ps -a --filter name=kombats --format 'table {{.Names}}\t{{.Status}}\t{{.Ports}}'`:

| Container | Status | Ports |
|---|---|---|
| kombats-bff | Up 18 minutes | 5000→8080 |
| kombats-chat | Up 18 minutes | 5004→8080 |
| kombats-players | Up 18 minutes | 5001→8080 |
| kombats-battle | Up 18 minutes | 5003→8080 |
| kombats-matchmaking | Up 18 minutes | 5002→8080 |
| kombats-grafana | Up 28 minutes | 3001→3000 |
| kombats-prometheus | Up 28 minutes | 9090→9090 |
| kombats-keycloak | Up 28 minutes | 8080→8080 |
| kombats-otel-collector | Up 28 minutes | 4317–4318, 8889 |
| kombats-postgres | Up 28 minutes (healthy) | 5432→5432 |
| kombats-keycloak-db | Up 28 minutes (healthy) | 5433→5432 |
| kombats-redis | Up 28 minutes (healthy) | 6379→6379 |
| kombats-rabbitmq | Up 28 minutes (healthy) | 5672, 15672 |
| kombats-jaeger | Up 28 minutes | 16686→16686 |
| kombats-keycloak-bootstrap | Exited (0) 28 minutes ago | — (one-shot) |

14 long-running containers + 1 one-shot bootstrap (Exited 0 = success). Matches Run 0 baseline composition. The "18 vs 28 minutes" split reflects that bff/chat/players/battle/matchmaking were the 5 services restarted after migrations (§6 of timeline); infrastructure containers (postgres/redis/keycloak/observability) kept their original start time.

### 6.1 Battle service health post-restart

- `StartedAt`: `2026-05-14T08:47:13Z` (13:47 +05:00).
- Errors before `StartedAt`: irrelevant (initial pre-migration startup; container restarted after migrations).
- Errors at exactly `08:47:13` (≤ 1 second post-restart): 2 transient race events — `TurnDeadlineWorker` `TaskCanceledException` (worker shutting down with old token) and one `InboxCleanupService` query that fired during the schema-transition instant. These are documented background-worker startup race patterns; same pattern observed in prior chapter runs.
- **Errors after `08:47:30Z` (15 s post-restart): 0.** Steady-state Battle service is clean.
- WARNs after `StartedAt`: 3, all environmental dev warnings — `DataProtection-Keys` ephemeral storage (in-container, expected), `HTTP_PORTS` override (expected ASPNETCORE_URLS precedence), `Failed to determine the https port for redirect` (expected — no HTTPS in dev). **None of them are `[WARN] Kombats.Observability`** — D2 stays silent as expected.

---

## 7. Ready-for-measurement statement

**The stack is ready for the architect-driven Sustainable Run measurement.**

Preconditions satisfied:
- [x] On `feat/redis-ttl-hardening`, HEAD = `7faf81d` (`docs: document canonical docker compose chains`), tree clean (only untracked `docker-compose.ttl-override.yml`).
- [x] D1 TTL fix verified in `RedisBattleStateStore.cs:275–277` (KeyExpireAsync gated on `EndedNow && StateTtlAfterEnd.HasValue`).
- [x] D2 OTLP defensive WARN verified silent (correct compose chain → `OpenTelemetry:OtlpEndpoint` populated → exporter attached → WARN does not fire).
- [x] Stack rebuilt with augmented Mode A chain (canonical 4 files + `docker-compose.ttl-override.yml`), 14 long-running + 1 bootstrap (Exited 0).
- [x] Migrations applied, 5-service post-migration restart clean.
- [x] Keycloak realm `kombats` live, 50 loadbot users seeded (`created=50, existed=0, failed=0`), manifest 50 entries.
- [x] HTTP smoke (single-bot end-to-end) PASS.
- [x] Observability smoke PASS — Grafana healthy, all 5 services exporting via otel-collector, 0 D2 WARN across all services.
- [x] TTL override env var verified at container level (`Battle__Redis__StateTtlAfterEnd=00:01:00`).
- [x] Redis clean baseline: `DBSIZE=0`, `battle:state:*=0`, `mm:player:*=0`.

Architect can proceed directly to NBomber Phase D (4 × 25-bot runs, 90 s sleep between, no `down -v` between).

---

## 8. Anomalies / surprises

### 8.1 `CHAPTER_2_5_PLAN.md` referenced by the Phase-C prompt does not exist

The Phase-C prompt says:

> Full context lives in `tests/Kombats.LoadTests/CHAPTER_2_5_PLAN.md` if you need it. Read it now before doing anything else — it has all the architectural decisions, pass criteria, and the explicit "things I will not do" list.

But the file does not exist:

```
$ find . -name "CHAPTER_2_5*" -type f
(no output)
$ ls tests/Kombats.LoadTests/CHAPTER_*.md
tests/Kombats.LoadTests/CHAPTER_3_PLAN.md
tests/Kombats.LoadTests/CHAPTER_3_REPORT.md
```

Forward references to `CHAPTER_2_5_REPORT.md` also exist in code (`RedisBattleStateStore.cs:274` comment) — that file likewise does not exist yet, presumably authored after measurement closes.

**Impact on this setup:** none — the Phase-C prompt itself contains the architectural decisions (TTL override mechanism, 4×25-bot runs, 60 s TTL, 90 s sleep, "expected oscillate ~0 → ~1053 per run"), the pass criteria ("no monotonic growth"), and the explicit "will-not" list. Setup proceeded purely from the Phase-C prompt without needing the missing plan file.

**Recommendation for the architect:** if the missing plan file was meant to be checked in alongside this measurement, that's a pre-measurement document to author; otherwise consider it implied-by-prompt and remove the forward references in the `RedisBattleStateStore.cs` comment when authoring `CHAPTER_2_5_REPORT.md` post-measurement.

### 8.2 Battle service's initial pre-migration startup errors (cosmetic, identical to all prior chapter runs)

At first container start (before migrations were applied at step 6), Battle logs show `relation "battle.outbox_state" does not exist` / `relation "battle.battles" does not exist` errors stamped `08:36:51`. These are the EF Core background workers querying schema that doesn't exist yet — known and previously documented in `PHASE_2_REPORT.md` §192 and `RUN_3_SETUP_LOG.md`. The post-migration 5-service restart cleared them; logs after `08:47:30Z` are clean.

This is not new for Chapter 2.5, but flagging because a reader auditing `docker logs kombats-battle` will see these red errors near the top of the log. They are pre-restart, pre-migration artefacts.

### 8.3 BFF health endpoint differs from other services

Players / Matchmaking / Battle / Chat expose `/health/ready` (returns 200). BFF returns 404 on `/health/ready` but 200 on `/health`. Probably a small registration difference (single `MapHealthChecks("/health")` in the BFF host vs `MapHealthChecks("/health/ready")` elsewhere). Documented here so the architect's automation knows which to probe. **Not blocking** — BFF responds and serves traffic correctly; the end-to-end `single-bot` smoke validates the BFF service is healthy at the protocol level (token relay, SignalR negotiate skip-disabled path, queue API).

### 8.4 D6 cross-validation: this run is the first to bring the chain home

Per `RUN_4_SETUP_LOG.md` §11, all of Chapter 3's Runs 1–4 ran with a broken compose chain (missing `observability/docker-compose.observability.override.yml`), which silently dropped OTel metrics for the base services. Run 4 §11 added the override file to the chain and validated the fix mid-chapter. Run 5 is the first Sustainable Run starting from a *corrected* documented chain (now codified in `README.md` "Mode A"), so this run also serves as a downstream check that the corrected chain holds across a `down -v` + fresh rebuild cycle. The Phase 8.b smoke confirms: 0 D2 WARN, all 5 services pushing telemetry. **No regression; D6 protection working as designed.**

---

## 9. Files touched

| Path | Change | Tracked? |
|---|---|---|
| `tests/Kombats.LoadTests/RUN_5_SETUP_LOG.md` | New — this file | Untracked (per prompt, no commits) |
| `docker-compose.ttl-override.yml` | New — transient TTL override | Untracked (per prompt, transient — manual removal after chapter closes) |

**Zero edits to `src/Kombats.*`, zero edits to `appsettings.json`, zero edits to canonical Mode A compose files, zero commits.**

---

## 10. Hand-off to architect for Phase D

Run the architect-driven Sustainable Run when ready:

- 4 sequential 25-bot NBomber load runs.
- 90 s sleep between runs.
- **No** `docker compose down -v` between runs (the whole point — exercise TTL across reuse).
- Expected: `battle:state:*` count oscillates between ~0 (post-TTL-expiry) and ~1053 (peak in-run), with no monotonic growth across runs.
- Watch `docker exec kombats-redis redis-cli --scan --pattern 'battle:state:*' | wc -l` during the 90 s gaps to observe TTL expiry firing.
- Watch Grafana / `:8889/metrics` for live telemetry.
- Capture iteration log (per Chapter 3 convention `tests/Kombats.LoadTests/iteration-logs/iterations-*.jsonl`).

When the measurement closes:
1. Delete `docker-compose.ttl-override.yml`.
2. Remove the forward `CHAPTER_2_5_REPORT.md` reference in `RedisBattleStateStore.cs:274` (or author the report and keep the reference).
3. Run `docker compose ... down -v` with the canonical Mode A chain.
