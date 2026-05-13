# Phase I Report — Chapter 3 dev-ops preparation

## 0. Header

- **Date:** 2026-05-13
- **Branch / HEAD:** `development` @ `2842b7f` (same Ch2-post-lease-fix HEAD as Run 0 baseline)
- **Stack state:** single-replica Battle, no SignalR backplane (`docker ps` shows the 14 containers from RUN_0_BASELINE.md still Up; no rebuild was needed for Phase I — all three deliverables are read-only relative to `src/Kombats.*`).
- **Scope:** Phase I per `CHAPTER_3_PLAN.md` §5/§7/§12 — three deliverables, no load runs, no commits.

---

## 1. Deliverable 1 — `tests/Kombats.LoadTests/scripts/dns-rotation-check.sh`

### Method chosen

**DNS-level first-IP rotation probe via a helper alpine container.** From a one-shot `docker run --rm --network kombats_default alpine:3.20`, the script runs `dig +short battle | head -n1` 15 times in a single inner loop and tallies which container served the first A-record across probes.

### Why this method (vs. the alternatives the spec lists)

- **`getent hosts battle` from inside an existing container** also works at the DNS level, but BFF's container has no `getent`-grade resolver tooling and adding probes via `docker exec` into BFF would be coupled to BFF's image (currently a slim .NET image with no `curl`/`wget`/`dig` — confirmed: `docker exec kombats-bff sh -c "which curl wget"` returns nothing). A dedicated helper container avoids the coupling.
- **Repeated curl to a Battle endpoint that exposes per-instance identity.** Battle does not currently expose its `service.instance.id` over any HTTP endpoint — `/health/ready` returns plain `Healthy`. Inventing such an endpoint would require a `src/Kombats.*` change, which Phase I forbids. Flagged as a finding in §4.
- **`first-IP` semantics specifically.** Docker's embedded DNS always returns all A records in *some* order — what rotates is the order. .NET's `HttpClient` (and therefore `BattleHubRelay.JoinBattleAsync` at `src/Kombats.Bff/Kombats.Bff.Application/Relay/BattleHubRelay.cs:55`) opens a fresh socket per `HubConnection.StartAsync` and picks the first resolved address; this is the address whose distribution we actually care about. Counting distinct IPs in the full return set would always give `R` (trivial) and tell us nothing about whether BFF spreads load.

### Discovery is dynamic (Phase II ready)

The script does **not** hardcode `battle-1` / `battle-2`. It walks `docker ps`, inspects each running container's network aliases on `kombats_default`, and keeps any container that advertises the configured service alias (`BATTLE_SERVICE_NAME`, default `battle`). Both Phase II layout options from `CHAPTER_3_PLAN.md` §5 — named services with `aliases: [battle]` or `docker compose up --scale battle=2` — work without modification.

### Exit-code contract

| Code | Meaning | Last line of output |
|---|---|---|
| 0 | ≥2 distinct instances served first-IP probes | `OK: DNS rotation confirmed` |
| 1 | Exactly 1 instance served all probes (single-replica OR sticky DNS) | `STOP: only 1 instance detected (single-replica or non-rotating DNS)` |
| 2 | ≥50 % of probes failed, or helper container/docker errored | `ERROR: probe failures, cannot determine` |

When exit 1 fires with >1 known replicas discovered, the script prints an explicit warning that "N replicas exist but only 1 served any probe — DNS is not rotating" before the `STOP:` line — this is the §5 stop-condition the plan enforces.

### Bash 3.2 portability

macOS ships `/bin/bash` 3.2 with no associative-array support. The script avoids `declare -A` entirely; IP→name and IP→hit-count are kept as parallel indexed arrays with a small linear lookup helper. Confirmed runnable under `/bin/bash` 3.2.

### Permissions

`chmod +x tests/Kombats.LoadTests/scripts/dns-rotation-check.sh` already applied. Runnable as `./tests/Kombats.LoadTests/scripts/dns-rotation-check.sh` from the repo root.

### Representative run output (single-replica state, current)

```
Discovering containers with alias 'battle' on network 'kombats_default'...
Discovered 1 container(s) with alias 'battle':
  kombats-battle  ->  172.19.0.14

Probing 'dig +short battle | head -n1' 15 times from a helper container on 'kombats_default'...

Probe results (15 probes, 0 failed):
  kombats-battle (172.19.0.14): 15 hits

STOP: only 1 instance detected (single-replica or non-rotating DNS)
```

Exit code `1`. This is the **expected Phase I close-out state** per the spec ("On single-replica Battle (current state): script should report '1 instance observed' with exit code that's not considered a failure for Phase I — single-replica is the current truth"). The script's value lives in Phase II when 2 replicas come up and we need a hard gate on whether the §3.1 math model still applies.

---

## 2. Deliverable 2 — per-replica Grafana panel

### Dashboard file modified

`observability/grafana/dashboards/kombats-overview.json` — confirmed authoritative source (Grafana provisioner mounts the host directory read-only at `/var/lib/grafana/dashboards`; UI saves cannot overwrite it, so the JSON edit survives any stack restart).

### New panel

- **id:** 13
- **Title:** `Active SignalR connections — per replica`
- **Placement:** end of the Hot Path section, full-width (`gridPos: x=0, y=17, w=24, h=8`), immediately below the existing `Active SignalR connections (Battle vs BFF)` panel. The Infra section and everything below were shifted down by `+8` rows so nothing overlaps.
- **Panel type / styling:** matches the adjacent aggregated SignalR panel — `timeseries`, `stepAfter` line interpolation, `fillOpacity: 20`, `short` unit, table-format legend.

### PromQL query (one line, no aggregation)

```
active_signalr_connections
```

Legend format:

```
{{service_name}}/{{service_instance_id}}
```

Returns one series per `(service_name, service_instance_id)`. No `sum by (service)` — that's the point. Battle, BFF, and Chat are not filtered; each that emits the metric produces its own line per replica.

### Sanity check (single-replica)

- Aggregated panel query `sum by (service) (active_signalr_connections)` returns 2 series: `battle=0`, `bff=0`.
- New per-replica query `active_signalr_connections` returns 2 series: `battle/<uuid>=0`, `bff/<uuid>=0`.
- Series counts match (Chat does not emit the counter — `SIGNALR_SURFACE_MAP.md` §B.3 notes Chat's `OnConnectedAsync` does not increment `ActiveSignalRConnections`; flagged in §4 below as a pre-existing inventory gap, not introduced here).
- Grafana confirmed it picked up the new panel via `GET /api/dashboards/uid/kombats-overview` — `id: 13`, title `Active SignalR connections — per replica`, `gridPos: {x:0,y:17,w:24,h:8}`, target `active_signalr_connections`, legend `{{service_name}}/{{service_instance_id}}`. Auto-provisioned reload triggered within ~10 s of the file edit.

Phase II expectation: with `R = 2` Battle replicas the panel will show 2 distinct `battle/<uuid>` lines + 1 `bff/<uuid>` line, and Q2's "are connections actually spread?" question becomes one glance at this panel.

---

## 3. Deliverable 3 — `service.instance.id` propagation verification

### Prometheus probe (verbatim, run 2026-05-13)

```
$ curl -s 'http://localhost:9090/api/v1/series?match[]=active_signalr_connections' | jq .
```

Returned two series with the following resource-derived labels:

| Label | Battle series | BFF series |
|---|---|---|
| `service_name` | `battle` | `bff` |
| `service_instance_id` | `e73acc7d-f608-4517-ba89-795c4e1b3da4` | `016f4e9d-471d-4cd2-8116-6822385073f9` |
| `service_namespace` | `kombats` | `kombats` |
| `deployment_environment` | `Development` | `Development` |
| `telemetry_sdk_name` | `opentelemetry` | `opentelemetry` |
| `telemetry_sdk_version` | `1.15.0` | `1.15.0` |

### Verdict

**No fallback applied — `service.instance.id` is present as Prometheus label `service_instance_id`.**

The .NET OTel SDK 1.15.0 auto-populates `service.instance.id` to a per-process UUID at startup. The OTel Collector's `resource_to_telemetry_conversion: enabled: true` setting (`observability/otel-collector/config.yaml:44`) promotes it to a metric label, and the Prometheus exporter renames `.` → `_`. End result: every Kombats service exposes a stable-per-process `service_instance_id` label, which is exactly what the Deliverable 2 panel needs to distinguish replicas.

No `docker-compose.yml` changes were required. Plan §12 Q5's "verify first, fallback if needed" resolved in the verify-only branch.

(`docker-compose.yml` does have an unrelated unstaged change from earlier sessions, visible in `git status`; nothing in it relates to Phase I.)

---

## 4. Open questions and surprises for architect (Section 5)

1. **No HTTP-exposed per-instance identity on Battle.** The DNS rotation script proves rotation at the DNS layer, which is the correct primitive for the §3.1 math model — but if Phase II's load runs ever need to attribute a specific *request* to a specific replica (e.g. "this stuck battle resolved on replica A while the player's connection was on replica B"), there's no HTTP introspection path today. Workarounds: structured logging already includes `service.instance.id` (visible in Grafana Loki / Jaeger via resource attributes); or add a 5-line `/health/instance` endpoint in a follow-up that returns `{instanceId, hostname}`. **Not blocking Phase II.** Flagged because the plan §5 Step 0 language ("hit `http://battle:8080/health/ready` ten times and cross-reference responses with each replica's logged `service.instance.id`") implicitly assumes such cross-referencing is doable from a script, and as currently written it requires log scraping — DNS-level probe is a strictly stronger primitive and what the deliverable now uses.

2. **Chat does not emit `active_signalr_connections`.** Pre-existing inventory gap documented in `SIGNALR_SURFACE_MAP.md` §B.3 — `Bff.Api.Hubs.ChatHub.OnConnectedAsync` increments the counter but `Chat.Api.Hubs.InternalChatHub.OnConnectedAsync` does not. The new per-replica panel will therefore never show a `chat/<uuid>` series until that gap closes. Not introduced by Phase I; not in Chapter 3 scope per plan §11; mentioning so the architect knows the panel's "Chat" label is silent by design, not by misconfiguration.

3. **Dashboard layout shift.** Adding the new panel required shifting `gridPos.y` of every panel from Infra onwards down by `+8`. The shift is mechanical and reversible, but reviewers comparing the dashboard JSON against the pre-Phase-I version will see far more diff than the one new panel block. Considered acceptable because the alternative (cramming a per-replica panel into a 6-column slot) would have made the legend unusable for the UUID-format `service_instance_id` values.

4. **Service-instance UUIDs in legend are 36 characters wide.** Two-replica legend will read e.g. `battle/e73acc7d-f608-4517-ba89-795c4e1b3da4`. Readable in a full-width panel; would be ugly in a 6-column one (which is why §3 above happened). If the architect prefers a shorter label after seeing it live in Phase II, the fix is one `label_replace(...)` wrap in the PromQL — non-blocking, cosmetic.

5. **No load-test run was performed** as Phase I explicitly forbids it. All sanity checks were against the currently-running stack which has been idle since Run 0 baseline (zero active SignalR connections at probe time — all panels read `0`). Phase II Run 2 will be the first time the per-replica panel shows non-zero data; if it shows `1` line instead of `2` at that point, the dns-rotation-check.sh exit code will already have indicated the problem at Step 0 and the run will not have started.

---

## 5. Files changed / added

| Path | Status | Purpose |
|---|---|---|
| `tests/Kombats.LoadTests/scripts/dns-rotation-check.sh` | added (chmod +x) | Deliverable 1 |
| `observability/grafana/dashboards/kombats-overview.json` | modified | Deliverable 2 (new panel id 13 + grid shift) |
| `tests/Kombats.LoadTests/PHASE_1_REPORT.md` | added | this file |

No changes under `src/Kombats.*`. No commits. No `docker-compose.yml` changes (Deliverable 3 verification only — no fallback needed).
