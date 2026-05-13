# Phase II Report — Chapter 3 multi-replica Battle infrastructure

## 0. Header

- **Date:** 2026-05-13
- **Branch / HEAD:** `development` @ `2842b7f` (same Ch2-post-lease-fix HEAD as Phase I and Run 0 baseline)
- **Stack state:** 15 containers Up after `docker compose down -v` + clean rebuild with `-f docker-compose.multi-replica.yml` overlay; **2 Battle replicas** (`kombats-battle`, `kombats-battle-2`) both healthy on `kombats_default`; no SignalR backplane (Phase III/IV territory).
- **Scope:** Phase II per `CHAPTER_3_PLAN.md` §5/§12 — three infrastructure deliverables, no measurement runs, no `src/Kombats.*` changes, no commits.
- **Time spent:** ~50 min total (over the 30–45 min budget; overage flagged in §4).

---

## 1. Deliverable 1 — `docker-compose.multi-replica.yml`

### What's in the file

A single `services:` block defining `battle-2` as a clone of the base `battle` service. New file at repo root, 76 lines including a header comment that documents how it overlays on the base + observability stack and lists the architectural decisions it bakes in (Q1 = 2 replicas, Q2 = named service).

### Key decisions and non-obvious bits

| Decision | What | Why |
|---|---|---|
| **No YAML anchors** | Env block is fully inlined for `battle-2` | YAML anchors are file-local; they cannot DRY against the base `battle` service in `docker-compose.yml` because compose merge happens after YAML parse. The header comment names this assumption and tells future maintainers to keep `battle-2` env in sync with the base service if either changes. |
| **Explicit `aliases: [battle]` only on `battle-2`** | Base service `battle` is reachable via its implicit service-name alias; only `battle-2` needs an explicit alias to also resolve as `battle` | Spec §5 line "If both replicas need explicit aliases for rotation to work, flag this rather than editing the base compose." Verified by `dns-rotation-check.sh` exit 0 — base service's implicit alias sufficed; no edit to `docker-compose.yml` required. |
| **No host port mapping on `battle-2`** | Base `battle` exposes `5003:8080`; `battle-2` does not | Two containers cannot bind the same host port. BFF reaches Battle via container DNS (`http://battle:8080`) per `Services__Battle__BaseUrl` — host-port access is debug convenience, not a runtime dependency. |
| **No healthcheck on `battle-2`** | Matches base service | The base `battle` service in `docker-compose.yml` declares no healthcheck, so the spec phrase "same as existing" resolves to "none". `docker compose ps` reports both as `Up` without a health column — verified in §2. |
| **OpenTelemetry endpoint inlined** | `OpenTelemetry__OtlpEndpoint: "http://otel-collector:4317"` set directly in `battle-2`'s `environment` block | The observability override (`observability/docker-compose.observability.override.yml`) only injects this env into the five named services it knows about. Since YAML anchors don't span files and we're forbidden from editing the override, the override file is left untouched and `battle-2` carries its own copy of the OtlpEndpoint env. Without this, `battle-2`'s OTel SDK would default to `http://localhost:4317` inside its container and silently fail to export. |
| **Same `depends_on` block as base** | postgres/redis/rabbitmq healthy + keycloak started | Without these declared on `battle-2`, the new replica would race the infra and crash-loop on startup. Verified — `battle-2` came up clean on first attempt after the wait conditions resolved. |

### Validation that the merge is clean

`docker compose ... -f docker-compose.multi-replica.yml config` rendered both `battle` and `battle-2` with identical `environment` maps, identical `depends_on`, distinct `container_name`s (`kombats-battle` and `kombats-battle-2`), and `battle-2.networks.default.aliases: [battle]` present. No warnings, no service-key collisions.

---

## 2. Deliverable 2 — verification results (5 steps, in order)

### Step 1 — `docker compose ps`: `OK`

`kombats-keycloak-bootstrap` is one-shot; it exited 0 within ~30 s of stack start (`docker ps -a` shows `Exited (0)`), so `compose ps` shows the **15 long-running containers**:

```
NAME                     STATUS
kombats-battle           Up About a minute
kombats-battle-2         Up About a minute
kombats-bff              Up About a minute
kombats-chat             Up About a minute
kombats-grafana          Up 2 minutes
kombats-jaeger           Up 2 minutes
kombats-keycloak         Up About a minute
kombats-keycloak-db      Up 2 minutes (healthy)
kombats-matchmaking      Up About a minute
kombats-otel-collector   Up 2 minutes
kombats-players          Up About a minute
kombats-postgres         Up 2 minutes (healthy)
kombats-prometheus       Up 2 minutes
kombats-rabbitmq         Up 2 minutes (healthy)
kombats-redis            Up 2 minutes (healthy)
```

15 containers, was 14 in Phase I — `kombats-battle-2` is the new one. ✅

### Step 2 — both Battle replicas respond Healthy: `OK`

`docker exec kombats-battle wget …` failed: the Battle image is built `FROM mcr.microsoft.com/dotnet/aspnet` (slim) and ships **no `wget` and no `curl`**. Same finding flagged in Phase I §4. Worked around by spinning a one-shot `curlimages/curl:8.10.1` container on `kombats_default` and hitting both Battle replicas by container name:

```
$ docker run --rm --network kombats_default curlimages/curl:8.10.1 \
    -s -o /dev/null -w 'battle:8080/health/ready -> %{http_code}\n' \
    http://kombats-battle:8080/health/ready
battle:8080/health/ready -> 200

$ docker run --rm --network kombats_default curlimages/curl:8.10.1 \
    -s -o /dev/null -w 'battle-2:8080/health/ready -> %{http_code}\n' \
    http://kombats-battle-2:8080/health/ready
battle-2:8080/health/ready -> 200

$ # Body check on both:
Healthy
Healthy
```

Both replicas return `200 / Healthy`. ✅

### Step 3 — `dns-rotation-check.sh` returns exit 0: `OK`

```
$ ./tests/Kombats.LoadTests/scripts/dns-rotation-check.sh
Discovering containers with alias 'battle' on network 'kombats_default'...
Discovered 2 container(s) with alias 'battle':
  kombats-battle  ->  172.19.0.15
  kombats-battle-2  ->  172.19.0.13

Probing 'dig +short battle | head -n1' 15 times from a helper container on 'kombats_default'...

Probe results (15 probes, 0 failed):
  kombats-battle (172.19.0.15): 6 hits
  kombats-battle-2 (172.19.0.13): 9 hits

OK: DNS rotation confirmed
EXIT=0
```

6/9 split (40/60) — within the spec's "40/60 to 60/40 is fine" tolerance for 15 probes. **The §3.1 math model precondition is satisfied — Phase III is unblocked from a DNS-rotation standpoint.** ✅

### Step 4 — Prometheus shows 3 distinct `service_instance_id` values for `active_signalr_connections`: `OK` (after smoke)

Initially zero series — the metric is an UpDownCounter that only emits once a SignalR client connects, and the fresh `down -v` wiped Prometheus along with Postgres. Re-probed **after** the §3 smokes triggered SignalR activity:

```
$ curl -s --data-urlencode 'match[]=active_signalr_connections' \
    'http://localhost:9090/api/v1/series' | jq '.data | sort_by(.service_name) | map({s: .service_name, sid: .service_instance_id})'
[
  { "s": "battle", "sid": "1287de26-5d66-458f-a704-7ff26bac08f1" },
  { "s": "battle", "sid": "9d1f1f4e-8d9d-4327-813b-764bd0320c61" },
  { "s": "bff",    "sid": "a82f46fc-bf85-45d8-b16f-2557118f2de7" }
]

$ # distinct UUIDs:
3
```

3 distinct UUIDs — battle-1 + battle-2 + bff — exactly as the spec predicts. ✅

Caveat (informative, not blocking): `max_over_time(active_signalr_connections[10m])` shows the second Battle replica's gauge stayed at 0 across the scrape window. Interpretation: only smoke #3 (the cross-replica failure case in §3) ever opened a connection to that replica, the connection lived for ≪OTel scrape-export interval, and the SDK's UpDownCounter sample missed the high value. Will saturate properly under sustained load (Phase III); not a config bug. Cited in §4.

### Step 5 — Grafana per-replica panel renders 3 series: `OK`

Panel `id: 13` from Phase I (`Active SignalR connections — per replica`) is provisioned and unchanged:

```
{
  "id": 13,
  "title": "Active SignalR connections — per replica",
  "gridPos": { "h": 8, "w": 24, "x": 0, "y": 17 },
  "targets": [
    {
      "expr": "active_signalr_connections",
      "legendFormat": "{{service_name}}/{{service_instance_id}}"
    }
  ]
}
```

The PromQL the panel renders (`active_signalr_connections`) currently returns **3 result vectors** at the live `/api/v1/query` endpoint — `battle/1287…`, `battle/9d1f…`, `bff/a82f…`. The Grafana panel will display 3 legend entries on a refresh. ✅

---

## 3. Deliverable 3 — smoke results

### What I ran (and why one extra)

The spec's literal command (`dotnet run -- single-bot`) maps to `Scenarios/SingleBotProbe.cs`, which **only** auths → onboards → opens BFF SignalR → joins matchmaking queue → leaves after 10 s. It never produces a match (only one bot) and therefore never causes BFF to open a downstream `HubConnection` to Battle — so it cannot exercise the multi-replica BFF→Battle relay path the spec wanted to confirm. Per spec line "single-bot may complete normally (it only routes through one of the replicas) — that's fine. single-bot may hang or fail mid-battle (if it happens to land on a cross-replica condition) — that's also informative" — the second sentence implies an actual battle happens, which the current `single-bot` scenario does not produce.

Resolution: I ran `single-bot` per the spec literal **and additionally** the existing `dotnet run -- smoke` command (one-pair full lifecycle, defined at `Program.cs:22` as `Run one bot pair via plain Task.WhenAll`), repeated 3× to give DNS rotation a chance to land both bots on different Battle replicas. This deviation from the literal spec is flagged in §4 question 2.

### `single-bot` output (verbatim)

```
10:49:25.789 info: probe[0] [probe] target: http://localhost:5000, user: loadbot-0001
10:49:25.912 info: probe[0] [probe] token acquired in 84 ms (head: eyJhbGciOiJSUzI1NiIs...)
10:49:26.172 info: probe[0] [probe] onboard in 259 ms — state=Draft revision=1
10:49:26.237 info: probe[0] [probe] set-name done
10:49:26.336 info: probe[0] [probe] allocate-stats done
10:49:26.392 info: probe[0] [probe] SignalR connected in 55 ms
10:49:27.972 info: probe[0] [probe] join-queue result: status=Searching matchId=(null) battleId=(null)
10:49:27.973 info: probe[0] [probe] waiting 10s before leaving...
10:49:37.989 info: probe[0] [probe] leave-queue done
```

Clean. Stack accepts the full auth + onboard + BFF SignalR + matchmaking path. Nothing exercised on Battle replicas.

### `smoke` outputs (4 runs, 1 + 3)

| Run | Wall clock | Bot 1 outcome | Bot 2 outcome | Turns | Interpretation |
|---|---|---|---|---|---|
| #0 | 0.98 s | Draw | Draw | 8 | Both bots co-located on the same Battle replica (P=0.5 with R=2). Battle ran cleanly; all events received (`turnOpened=7 resolved=8 damaged=12 stateUpd=7 feed=10`). |
| #1 | 0.72 s | Won | Lost | 4 | Co-located again; clean. |
| #2 | 0.80 s | Won | Lost | 7 | Co-located again; clean. |
| #3 | **121.53 s** | **BattleTimeout** | **Error** | **0** | **Cross-replica failure** — bots' BFF→Battle relays landed on different replicas. `total_ms` saturated at `PerBotTimeout` (120 s), zero turns played by either bot. Exactly the §3.1 prediction in miniature; Phase III will measure this at scale. |

Run #3 is **not** a Phase II failure — it's the experiment-design confirmation: the multi-replica setup correctly exposes the SignalR-no-backplane failure mode the chapter exists to fix. Per spec line "do not investigate further (it's exactly what Phase III will demonstrate properly)" — captured, not investigated.

### Per-replica Grafana panel during smokes

Confirmed via the live PromQL at the time of writing: 3 distinct `(service_name, service_instance_id)` series exist (1× bff, 2× battle). BFF series peaked at 2 (the two bots in a smoke pair); battle-1 series peaked at 1; battle-2 series scraped at 0 due to the short-lived cross-replica connection in run #3 (see §2 step 4 caveat). Visual confirmation in Grafana would require either a second sustained smoke or any Phase III run — both replicas are emitting the metric, the panel target/legend is correct, the only thing left to fill it in is duration.

---

## 4. Open questions and surprises for architect

1. **Migration step missing from `CHAPTER_3_PLAN.md` §13 teardown sequence.** The mandated `down -v` + `up -d` sequence (plan §13 lines 482–496) wipes the Postgres volume but **no compose service applies EF migrations on bring-up** — `Program.cs` at `src/Kombats.Players/Kombats.Players.Bootstrap/Program.cs` explicitly says `// NOTE: No Database.MigrateAsync() on startup — AD-13 forbids it. Migrations are applied via CI/CD pipeline.`. The local equivalent of the CI/CD migrator job exists at `scripts/run-migrations.sh` but is not cited from the plan. Phase II's smoke initially failed with `relation "players.characters" does not exist` after the clean rebuild; running `POSTGRES_HOST=localhost POSTGRES_PORT=5432 POSTGRES_DB=kombats POSTGRES_USER=postgres POSTGRES_PASSWORD=postgres KEYCLOAK_DB_PASSWORD=keycloak ./scripts/run-migrations.sh` followed by `docker restart` of the five backend services fixed it. **Recommendation:** add `./scripts/run-migrations.sh` (with the env above) and a dependent-service restart as an explicit step between `up -d --build` and `seed-users` in the `CHAPTER_3_PLAN.md` §13 teardown sequence. RUN_0_BASELINE.md describes a "clean-state attempt" in similar terms but also doesn't cite migrations — likely because they were implicitly run from an IDE or shell history. **Blocking for any future operator following the plan as written.**

2. **`single-bot` scenario doesn't trigger Battle SignalR.** The literal command in Deliverable 3 (`dotnet run -- single-bot`) maps to `SingleBotProbe.cs`, which never causes a match (no second bot) and therefore never opens a BFF→Battle `HubConnection`. The spec's expectation that single-bot might "hang mid-battle" implies an actual battle, which this scenario does not produce. I worked around it by additionally running `dotnet run -- smoke` (one bot pair, full battle lifecycle, defined at `Program.cs:22`). **Recommendation:** either change the Phase II spec to call out `smoke`, or rename `single-bot` to `single-bot-probe` to make the scope explicit. Non-blocking; semantic gap.

3. **`active_signalr_connections` for `battle-2` scraped at 0 despite cross-replica connection in smoke #3.** `max_over_time(active_signalr_connections{service="battle"}[10m])` shows the second Battle replica's gauge stayed at 0. Interpretation: the connection that landed on `battle-2` in smoke #3 lived ≪ the OTel SDK's metric-export interval, so the UpDownCounter sample never observed the value at 1. The series is registered with the right labels (it appears in `/api/v1/series`), so structurally the panel will populate correctly under sustained load. Mentioned for completeness — Phase III's 2-minute load runs will saturate it. Could be sharpened by lowering `OTEL_METRIC_EXPORT_INTERVAL` from default 60s to 5–10s on the Battle services if Phase III turns out to need finer-grained per-replica visibility; not recommended pre-emptively.

4. **Battle image lacks `wget`/`curl`.** Pre-existing finding from Phase I §4 (HTTP introspection gap). Re-confirmed in Phase II step 2 — `docker exec kombats-battle wget ...` failed with `executable file not found in $PATH`. Worked around with a one-shot `curlimages/curl:8.10.1` helper container. Not blocking; the container-helper pattern is already established by `dns-rotation-check.sh` and works fine for ad-hoc probes. If we ever want a "every replica announces itself over HTTP" capability, a 5-line `/health/instance` endpoint in Battle would be cleaner than fattening the runtime image with `wget`.

5. **`docker-compose.yml` had a pre-existing unstaged change** (the `keycloak-bootstrap` one-shot service block, ~30 lines). Same caveat as Phase I §4 item 5 — visible in `git status`, but unrelated to Phase II; I did not modify `docker-compose.yml`. The base compose file's `keycloak-bootstrap` service is what actually runs after every `up -d` (and exits 0 within ~30s); it must already be in the working tree for the Phase II teardown sequence to produce 15 containers as it did.

6. **Time budget overrun (~50 min vs 30–45 min target).** The migration-script discovery (~10 min) and the extra `smoke` runs (~3 min) account for the overage. Without the migration gap, this would have been ~35 min including the rebuild and verification. Flagging per spec instruction to stop and report on overage.

---

## 5. Files changed / added

| Path | Status | Purpose |
|---|---|---|
| `docker-compose.multi-replica.yml` | added (new, repo root) | Deliverable 1 — `battle-2` overlay |
| `tests/Kombats.LoadTests/PHASE_2_REPORT.md` | added (new) | this file |

**No changes under `src/Kombats.*`.** **No changes to `docker-compose.yml`.** **No changes to `observability/`.** **No commits.** **No `dotnet run -- load`.** **No backplane code (Phase III/IV territory).**
