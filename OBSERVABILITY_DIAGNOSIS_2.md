# Observability diagnosis #2 — post-rebuild "empty kombats_* queries"

Investigated 2026-05-11 against the rebuilt stack the user left running.
Working tree: `fix/observability-compose-paths` at commit `b2b38fd`.
Stack uptime at investigation time: 37 minutes since rebuild.

---

## Section A — Resolution of the contradiction

**None of hypotheses (a)–(e) is correct.** The "contradiction" is illusory: the
backend services are exporting metrics, the collector is receiving them, and
Prometheus is scraping them successfully right now. The user's smoke-test
observations are consistent with **two unrelated user-side mistakes**:

1. **The PromQL query `{__name__=~"kombats_.*"}` doesn't match anything because no instrument name starts with `kombats_`.** OpenTelemetry .NET emits the *instrument* name unchanged — `active_battles`, `active_signalr_connections`, `downstream_hub_connections`, `turn_resolution_duration_ms_milliseconds`. The string "Kombats" appears only in the *Meter* name (`Kombats.battle`), and the Meter name is **not** prepended to instrument names in the Prometheus exposition. Running `{__name__=~"active_.*|turn_.*|downstream_.*"}` returns 22 series across battle and bff with the expected `service` labels. Evidence:
   ```
   curl -sG http://localhost:9090/api/v1/query \
     --data-urlencode 'query={__name__=~"kombats_.*"}'
   → { "data": { "result": [] } }

   curl -sG http://localhost:9090/api/v1/query \
     --data-urlencode 'query={__name__=~"active_.*|turn_.*|downstream_.*"}'
   → 22 series; values e.g. active_signalr_connections{service="bff"}=2,
                              turn_resolution_duration_ms_milliseconds_count{service="battle"}=2
   ```

2. **The earlier `{__name__=~".+"} → 5 series only` observation was a transient state** taken before the first OpenTelemetry SDK metric-push reached the collector. The OpenTelemetry .NET `PeriodicExportingMetricReader` default `ExportIntervalMilliseconds` is **60000 ms** (`KombatsObservabilityExtensions.cs:62-76` does not override it). On rebuild, services take ~3-15s to become reachable on `/health/ready`; the first metric push lands at the first 60s boundary after the SDK starts. Prometheus default scrape interval is 5s here (`prometheus.yml`). Between rebuild and the first SDK push, every Prometheus scrape returns `scrape_samples_scraped=0` and `__name__=~".+"` returns only the 5 scrape-meta series Prometheus emits about itself. Current state (37 min later): `scrape_samples_scraped=2776`, `90` metric names indexed, exposition is `1.4 MB / 2896 lines` (Step 4 evidence).

Hypothesis-by-hypothesis verdict:

| Hypothesis | Verdict | Evidence |
|---|---|---|
| (a) Grafana shows historical data; current scrapes empty | **FALSE** | `scrape_samples_scraped=2776` right now; 90 metric names indexed; sample at 12:34:23 and 12:35:02 both 1.4MB exposition. |
| (b) Bursts with scrape gaps | **FALSE** | Collector `prometheusexporter` `metric_expiration` defaults to 5 min and persists last-known values; sample-1 and sample-2 byte counts are identical (1 406 173 bytes). |
| (c) players/matchmaking/chat never got OTLP env var | **FALSE** | All 5 backends `docker exec ... env` show `OpenTelemetry__OtlpEndpoint=http://otel-collector:4317`; all 5 `service_name=` labels appear in the exposition. |
| (d) OTLP exporter failing silently for some services | **FALSE** | All 5 backends have `RestartCount=0`; no OTel/OTLP error lines in any service log; all 5 service_name labels emit `process_cpu_time_seconds_total`, `process_memory_usage_bytes`, MassTransit, AspNetCore metrics. |
| (e) Collector pipeline dropping metrics | **FALSE** | Synthetic push test from prior diagnosis (and now sustained live exposition) confirms the otlp→resource→batch→prometheus pipeline is healthy. |

---

## Section B — Service-by-service metric flow table

| Service | Container Up? | OTLP env set? | Logs show export activity? | `service_name` label in collector? | kombats custom metrics emitted? |
|---|---|---|---|---|---|
| bff | Up 36 min, restart=0 | yes (`http://otel-collector:4317`) | no OTel-related errors; one benign `TaskCanceledException` from outbound HttpClient | yes — `service_name="bff"` | yes — `active_signalr_connections{service="bff"}=2`, `downstream_hub_connections{service="bff"}=0` |
| players | Up 36 min, restart=0 | yes | no OTel lines (clean) | yes — `service_name="players"` | n/a (no custom kombats metrics defined for Players in `KombatsMetrics.cs`) — emits process/HTTP/runtime/MassTransit fine |
| matchmaking | Up 36 min, restart=0 | yes | no OTel lines (clean) | yes — `service_name="matchmaking"` | n/a (pairing_duration_ms / queued_players are registered but not yet wired per `observability/README.md:117-118`) |
| battle | Up 36 min, restart=0 | yes | no OTel lines (clean) | yes — `service_name="battle"` | yes — `active_battles{service="battle"}=0`, `active_signalr_connections{service="battle"}=0`, `turn_resolution_duration_ms_milliseconds_count{service="battle"}=2` |
| chat | Up 36 min, restart=0 | yes | no OTel lines (clean) | yes — `service_name="chat"` | n/a (no custom kombats metrics defined for Chat) — emits everything else fine |

Per-service `process_cpu_time_seconds_total` and `process_memory_usage_bytes` query results from Step 7's Prometheus instant query (cited verbatim):

```
process_memory_usage_bytes
  service=chat       282 521 600
  service=bff        187 596 800
  service=matchmaking 291 164 160
  service=players    281 604 096
  service=battle     281 907 200

process_cpu_time_seconds_total (10 series — 2 per service, split by `state="user"`/`"system"`)
  service=chat        2.72 + 14.29
  service=bff         0.98 + 5.55
  service=matchmaking 3.67 + 18.85
  service=players     2.09 + 12.96
  service=battle      3.11 + 17.43
```

So the user's observation "Runtime panels show data for battle/bff but NOT for players/matchmaking/chat" was a transient — taken while the latter three services hadn't yet pushed their first 60-second metric batch (or hadn't yet been added to a default 1-hour view that started before their first push).

---

## Section C — Root causes

### Root cause #1 — User's `kombats_*` PromQL prefix is wrong

The instrument names defined in `src/Kombats.Common/Kombats.Observability/KombatsMetrics.cs:43-72` are unprefixed:
- `turn_resolution_duration_ms` (l. 43)
- `active_battles` (l. 50)
- `active_signalr_connections` (l. 55)
- `downstream_hub_connections` (l. 60)
- `pairing_duration_ms` (l. 65)
- `queued_players` (l. 70)

OpenTelemetry's Prometheus exporter does **not** prepend the Meter name (`Kombats.battle`) to instrument names — that's a common misconception. The user's PromQL needs to match these literal names. Affects: the user's query alone, no code/config impact.

### Root cause #2 — Histogram unit suffix `_milliseconds` mangles the metric name

The histogram declared as `_meter.CreateHistogram<double>(name: "turn_resolution_duration_ms", unit: "ms", description: ...)` in `KombatsMetrics.cs:43-46` is exported to Prometheus as **`turn_resolution_duration_ms_milliseconds`** because the SDK's Prometheus exporter expands the OTel unit `ms` to the Prometheus-style suffix `_milliseconds` and appends it to the instrument name. Confirmed by curl:

```
# HELP turn_resolution_duration_ms_milliseconds Battle turn resolution latency (engine + Redis commit + broadcast).
# TYPE turn_resolution_duration_ms_milliseconds histogram
turn_resolution_duration_ms_milliseconds_count{service="battle",...} 2
```

The dashboard query in `observability/grafana/dashboards/kombats-overview.json:41,46,51` references `turn_resolution_duration_ms_bucket` (without `_milliseconds`):

```
"expr": "histogram_quantile(0.50, sum(rate(turn_resolution_duration_ms_bucket[1m])) by (le))",
"expr": "histogram_quantile(0.95, sum(rate(turn_resolution_duration_ms_bucket[1m])) by (le))",
"expr": "histogram_quantile(0.99, sum(rate(turn_resolution_duration_ms_bucket[1m])) by (le))",
```

These queries will return empty until the dashboard is updated to `turn_resolution_duration_ms_milliseconds_bucket` OR the instrument is renamed so the name doesn't already contain the unit (then the suffix harmlessly appears once: e.g. instrument name `turn_resolution_duration` + unit `ms` → `turn_resolution_duration_milliseconds`).

Affects: the **"Turn resolution latency (ms)"** panel — it is currently the only obvious "No data" panel that has a *real* schema mismatch reason.

### Root cause #3 — Time-range artefact made the user think 3 services were silent

The user's "Runtime panels show data for battle and bff but NOT for players, matchmaking, chat" observation is a snapshot bias from the OpenTelemetry SDK's 60-second push cadence vs Prometheus's 5-second scrape:

- After rebuild, services come up over ~5-15s depending on dependencies.
- The PeriodicExportingMetricReader (`OpenTelemetry.Metrics.MetricReaderOptions.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds` — default `60000`) pushes its first batch 60s after the SDK starts.
- Battle and BFF were likely first to push because their hubs serve traffic immediately (which warms internal AspNetCore counters and causes the meter to register).
- Players/Matchmaking/Chat may have pushed their first batch later (after their MassTransit consumer received its first message, or after `BackgroundService.StartAsync` completed).
- Grafana's default time range `Last 1 hour` shows whatever the dashboard has, so the user may have refreshed before those batches landed.

37 minutes after rebuild, every service has multiple cycles of metrics in Prometheus. The "missing" panels would fill if the user refreshes now.

Affects: user perception only — no fix in code/config.

### Root cause #4 — Pre-existing dashboard fact: `bff` has no custom `active_battles`/`turn_resolution_*` metrics

Per `KombatsMetrics.cs` the custom counters `active_battles` and `turn_resolution_duration_ms` are emitted **only by the Battle service** (they get incremented from `BattleLifecycleAppService.cs:135` and `BattleTurnAppService.cs:178` which live in the Battle assembly). Similarly `downstream_hub_connections` is BFF-only. The dashboard panels for these will show data for exactly one service each — that is by design and not a bug.

### Root cause #5 (carried over from previous diagnosis, still unfixed) — `AddMeter("Npgsql")` missing

`KombatsObservabilityExtensions.cs:64-70` registers `AddMeter("MassTransit")` but not `AddMeter("Npgsql")`. The Postgres command duration panel cannot populate without it. Verified absent in current exposition: no `db_client_operation_duration_seconds*` family with `db_system="postgresql"` label appears in the 84 metric families served by the collector.

---

## Section D — Recommended fixes (do not apply yet)

| # | Fix | Files | Expected impact |
|---|---|---|---|
| 1 | Educate the user on the actual instrument names. Update `observability/README.md` "What is emitted" section to make explicit that PromQL queries must match `active_battles`, `active_signalr_connections`, etc. — no `kombats_` prefix. | `observability/README.md` | Removes the user-side query-prefix confusion. |
| 2 | Rename `turn_resolution_duration_ms` to `turn_resolution_duration` (drop the redundant `_ms` from the instrument name) and keep the `unit: "ms"` argument so the SDK appends `_milliseconds` exactly once. **And** update the three dashboard queries to reference `turn_resolution_duration_milliseconds_bucket`. | `src/Kombats.Common/Kombats.Observability/KombatsMetrics.cs:43-46` (rename arg `name`); `observability/grafana/dashboards/kombats-overview.json:41,46,51` (rename query) | Turn latency panel populates. |
| 3 | Add `.AddMeter("Npgsql")` to the `WithMetrics(...)` block. | `src/Kombats.Common/Kombats.Observability/KombatsObservabilityExtensions.cs:70` (append after `.AddMeter("MassTransit")`) | "Postgres command duration p95" panel populates. |
| 4 | Trim 1-hour default time range to 15 min OR add a note to the dashboard description that all 5 services need ≥1 minute of runtime + first OTLP push before steady-state data appears. | `observability/grafana/dashboards/kombats-overview.json` `"time"` block | Avoids the "post-rebuild empty panels" surprise. |
| 5 | (Pre-existing — already on the previous fix list) Wire SignalR trace context propagation so multi-service traces stitch into one tree. | `src/Kombats.Bff/Kombats.Bff.Application/Relay/BattleHubRelay.cs` + `src/Kombats.Battle/Kombats.Battle.Infrastructure/Realtime/SignalR/BattleHub.cs` | BFF→Battle traces become a single tree. |

Fixes #1, #2, #3, #4 are pure docs/dashboard/text edits with no behavioural risk. Fix #2 is the only one that requires a service rebuild because it changes a name baked into the binary.

---

## Section E — Open questions

1. **Why is `active_signalr_connections{service="bff"}=2` when Battle's is `=0`?** The user said they "played one complete battle" and presumably closed the browsers. If the BFF side's OnDisconnectedAsync didn't fire, the counter is stuck high. Possible causes: WebSocket disconnect not propagating to the SignalR transport (browser still has the page tab open behind the scenes), or a race in `BattleHub.OnDisconnectedAsync` (`src/Kombats.Bff/Kombats.Bff.Api/Hubs/BattleHub.cs`). Worth checking — if `BattleHubRelay.DisconnectAsync` removed the entry but the BFF Hub's metric `.Add(-1)` didn't run, we have a divergence between `downstream_hub_connections` (correctly 0) and `active_signalr_connections` (still 2). Recommend deferring to a separate ticket.
2. **Why does the user's Prometheus snapshot say `scrape_samples_scraped=0` and 5 series total, when my snapshot a few minutes later says `2776` and 90?** Likely the user took the snapshot in the first ≤60s window post-rebuild before the first SDK push. Confirm by asking the user for the timestamp of their first observation vs the stack's first-up time.
3. **Do we want the `_milliseconds` Prometheus suffix at all?** Some users prefer no auto-suffix because it makes querying surprising. Alternative is to remove the OTel `unit` argument entirely (services emit `turn_resolution_duration_ms` directly without a unit label or suffix). Trade-off: losing the unit means losing the Prometheus exposition `# UNIT` line and the OTLP-level normalization. User preference.
4. **Is the user planning a recurring smoke-test routine?** If yes, an automatic post-deploy "wait 90s, then query Prometheus for non-zero scrape_samples_scraped" check would catch the post-rebuild transient before any human gets confused.

---

*Live evidence collected 2026-05-11 12:34:23 – 12:35:02 +05; backend stack age 37 minutes since rebuild; Prometheus uptime same.*
