# Observability diagnosis — post-smoke-test "No data"

Investigated 2026-05-11 against the live stack the user left running.
Working tree: `fix/observability-compose-paths` at commit `b2b38fd`.

---

## Section A — Service health snapshot

`docker compose -f docker-compose.yml -f observability/docker-compose.observability.yml -f observability/docker-compose.observability.override.yml ps`:

| Service | Status | Notes |
|---|---|---|
| postgres | `Up 16 minutes (healthy)` | OK |
| keycloak-db | `Up 16 minutes (healthy)` | OK |
| keycloak | `Up 16 minutes` | OK; no healthcheck |
| rabbitmq | `Up 16 minutes (healthy)` | OK |
| redis | `Up 16 minutes (healthy)` | OK |
| bff | `Up 15 minutes` | `/health/ready` returns **404** (not mapped in Bff Program.cs — minor, pre-existing) |
| players | `Up 15 minutes` | `/health/ready` 200 |
| matchmaking | `Up 15 minutes` | `/health/ready` 200 |
| battle | `Up 15 minutes` | `/health/ready` 200 |
| chat | `Up 15 minutes` | `/health/ready` 200 |
| otel-collector | `Up 16 minutes` | logs clean |
| jaeger | `Up 16 minutes` | UI 200 |
| prometheus | `Up 16 minutes` | scrape `up{job=otel-collector}=1` |
| grafana | `Up 16 minutes` | `/api/health` `{"database":"ok",...}` |

The Battle service log confirms the user actually played a battle:

```
[06:29:02 INF] Client disconnected: ConnectionId: DokIKgoflzuS-AlzXtuKWA … RequestPath: /battlehub
[06:29:02 INF] Client disconnected: ConnectionId: ekmLydttJcBzzQ33168e7w … RequestPath: /battlehub
```

— two `/battlehub` connections opened and closed cleanly. BFF logs mirror this with `Frontend vFHB6ZM16z0lXi7cksFHdA disconnected` / `Downstream Battle connection closed`. So the SignalR connect → disconnect code paths in `OnConnectedAsync`/`OnDisconnectedAsync` (Battle and BFF) **did run** during the smoke test.

No migration errors, no auth errors, no crash loops. All services are healthy in the conventional sense.

---

## Section B — Metric flow per category

### Category 1 — Custom Kombats metrics

The four target metrics: `active_battles`, `active_signalr_connections`, `downstream_hub_connections`, `turn_resolution_duration_ms`.

| Step | Result |
|---|---|
| Code emits it? | **Yes** in the working tree — `src/Kombats.Common/Kombats.Observability/KombatsMetrics.cs:43-72` defines all four instruments; `BattleHub.cs:38,84` (Battle), `Hubs/BattleHub.cs:17,46` (BFF), `BattleHubRelay.cs:244,313,371` (BFF), `BattleTurnAppService.cs:163,178` and `BattleLifecycleAppService.cs:135` record them. **No try/catch swallows `.Add(...)`**. `KombatsMetrics` is `services.AddSingleton(new KombatsMetrics(serviceName))` so the instance and Meter are process-wide singletons. |
| Reached collector? | **No.** `curl -s http://localhost:8889/metrics` returns **0 bytes** for application data. Only `diagnostic_counter_total{...}` 42 appears, and it's a synthetic OTLP HTTP push I made for diagnosis — there are zero metric families with prefix `kombats_`, `active_`, `turn_`, `downstream_`. |
| Reached Prometheus? | **No.** `curl http://localhost:9090/api/v1/label/__name__/values` returns only `["diagnostic_counter_total","scrape_duration_seconds","scrape_samples_post_metric_relabeling","scrape_samples_scraped","scrape_series_added","up"]` — six total, all of them either my probe or scrape meta. |
| Dashboard query correct? | The queries `active_battles`, `sum by (service) (active_signalr_connections)`, `sum by (service) (downstream_hub_connections)`, `histogram_quantile(0.95, sum(rate(turn_resolution_duration_ms_bucket[1m])) by (le))` are syntactically right and would match the metric names emitted by `KombatsMetrics`. Not the bottleneck. |
| Bottom-line | Failure is at **stage (a) instrumentation in service code** — but not because the source code is wrong; because the **running Docker images don't contain the source code** (see Section C, root cause #1). |

### Category 2 — Runtime metrics

`process_cpu_*`, `process_memory_*`, `dotnet_gc_*`, `dotnet_thread_pool_*`.

| Step | Result |
|---|---|
| Code emits it? | **Yes** in the working tree — `KombatsObservabilityExtensions.cs:67-68` calls `.AddRuntimeInstrumentation()` and `.AddProcessInstrumentation()` inside `WithMetrics`. |
| Reached collector? | **No.** Same as category 1: zero exposition. |
| Reached Prometheus? | **No.** |
| Dashboard query correct? | Yes — already corrected once. Queries reference `process_cpu_time_seconds_total`, `process_cpu_count`, `process_memory_usage_bytes`, `dotnet_gc_collections_total{gc_heap_generation="gen2"}`, `dotnet_thread_pool_queue_length_total`. All match the actual OTel emitter names (proven by my prior Players-from-host smoke test). |
| Bottom-line | Same as category 1 — root cause #1 (image staleness). |

### Category 3 — MassTransit metrics

`messaging_masstransit_publish_count`, `messaging_masstransit_consume_count`.

| Step | Result |
|---|---|
| Code emits it? | **Yes** in the working tree — `KombatsObservabilityExtensions.cs:70` calls `.AddMeter("MassTransit")`. MassTransit 8.5.9 emits `messaging.masstransit.*` family natively. |
| Reached collector? | **No.** |
| Reached Prometheus? | **No.** |
| Dashboard query correct? | Likely correct; `messaging_masstransit_publish_count` / `_consume_count` are the standard MassTransit 8 metric names. Cannot fully verify until metrics actually flow. |
| Bottom-line | Same as category 1 — root cause #1. |

### Category 4 — Distributed traces

The user reported "only single-span Postgres traces from Kombats.Battle". My investigation contradicts this in part:

- `curl http://localhost:16686/api/services` returns **all six services**:
  `["Kombats.Chat", "Kombats.Battle", "Kombats.Matchmaking", "Kombats.Bff", "jaeger-all-in-one", "Kombats.Players"]`. The user just didn't open the service dropdown wide enough.
- Per-service trace samples (`/api/traces?service=X&limit=1`):
  - `Kombats.Bff` — 1 trace, single span `GET` (Scalar docs or similar HTTP probe).
  - `Kombats.Battle` / `Players` / `Chat` / `Matchmaking` — single span `postgresql`.

So traces flow from every backend, but **no trace contains spans from more than one service**. The user's "BFF → Battle → Postgres tree" is missing.

| Step | Result |
|---|---|
| Code emits spans? | Yes (instrumentation registered for AspNetCore + HttpClient + Npgsql in the running binaries — see Section C root cause #1; also MassTransit + Redis + Kombats source after rebuild). |
| Reached collector? | Yes (Jaeger has 6 services). |
| Reached Jaeger? | Yes. |
| Trace context propagation across services? | **No, for SignalR.** `BattleHubRelay.cs:62-67` injects `traceparent`/`tracestate` headers into the **initial WebSocket negotiate** HTTP request via `options.Headers["traceparent"] = traceparent`, which is **once per connection**. After negotiate, every `connection.InvokeAsync("SubmitTurnAction", ...)` SignalR call sends a hub-method message frame **with no headers** — SignalR doesn't have header semantics inside a connection, only at negotiate time. The Battle hub then starts a fresh trace for each SubmitTurnAction with no parent. |
| Bottom-line | Failure is at **stage (a) — service code does not propagate trace context for SignalR hub-method invocations**. This is a separate, pre-existing limitation independent of the image-staleness issue. Even after a rebuild the multi-service trace tree the user wants will not appear without explicit propagation work. |

---

## Section C — Root causes

### Root cause #1 — Backend Docker images are stale; missing the observability foundation entirely

**Evidence:**

1. Image creation timestamps:
   ```
   kombats-bff           2026-05-06 12:48:55
   kombats-battle        2026-05-06 11:28:35
   kombats-matchmaking   2026-05-06 11:28:30
   kombats-players       2026-05-06 11:28:26
   kombats-chat          2026-05-06 11:28:22
   ```
   The observability commit `2f4e17d` was made **today, 2026-05-11**. The images predate it by five days.

2. The shared library assembly is absent from the image:
   ```
   $ docker run --rm --entrypoint /bin/sh kombats-battle -c "ls /app | grep -i Kombats.Observ"
   (empty)
   ```
   `Kombats.Observability.dll` does not exist in the running container.

3. Direct binary inspection of `Kombats.Battle.Bootstrap.dll` extracted from the image (`docker cp` to host, then `grep -a`):
   - Strings **present** (old code): `AddOpenTelemetry`, `ConfigureResource`, `AddAspNetCoreInstrumentation`, `AddHttpClientInstrumentation`, `AddOtlpExporter`, `AddSource`, `Kombats.Battle`.
   - Strings **absent**: `AddKombatsObservability`, `KombatsMetrics`, `active_battles`, `active_signalr_connections`, `turn_resolution_duration_ms`, `WithMetrics`, `AddRuntimeInstrumentation`, `AddProcessInstrumentation`, `AddRedisInstrumentation`.

4. Jaeger reports `service.name="Kombats.Battle"` (capitalized), matching the **old** `Program.cs:157` `ConfigureResource(resource => resource.AddService("Kombats.Battle"))`. The new code emits `service.name="battle"` (lowercase) via `KombatsObservabilityExtensions.cs:42` `AddService(serviceName: serviceName, serviceNamespace: ServiceNamespace)`. This confirms the running binary is pre-foundation.

5. The collector's `:8889/metrics` exposition is 0 bytes (after the diagnostic synthetic-push line is excluded). I verified the metrics pipeline itself is healthy by pushing `diagnostic_counter` over OTLP HTTP and observing it appear at `:8889`. So the failure is **services not emitting metrics**, not the collector dropping them. Since the old binary has **no `.WithMetrics(...)` block at all**, this is consistent.

**Affected metrics / traces:**

- All 4 custom Kombats metrics.
- All runtime / process / HTTP / MassTransit / Redis metrics.
- Indirectly, Jaeger service-name labels would have been `battle`/`bff`/etc. (lowercase) after rebuild instead of `Kombats.Battle`/etc.

**Diagnosis confidence**: very high. Single fix (rebuild) lifts categories 1-3 simultaneously.

### Root cause #2 — SignalR hub-method invocations do not propagate W3C trace context

**Evidence:** `src/Kombats.Bff/Kombats.Bff.Application/Relay/BattleHubRelay.cs:53-67`:

```csharp
Activity? activity = Activity.Current;
string? traceparent = activity?.Id;
string? tracestate = activity?.TraceStateString;

var hubBuilder = new HubConnectionBuilder()
    .WithUrl(battleHubUrl, options =>
    {
        options.AccessTokenProvider = () => Task.FromResult<string?>(accessToken);
        if (!string.IsNullOrEmpty(traceparent))
        {
            options.Headers["traceparent"] = traceparent;
            ...
        }
    });
```

This injects `traceparent` **only on the WebSocket negotiate HTTP request** (one-time at connection startup). After negotiation, SignalR message frames sent via `connection.InvokeAsync("SubmitTurnAction", ...)` (`BattleHubRelay.cs:334`) have no header mechanism in the protocol; the trace context cannot ride along on the frame.

Battle's `BattleHub.SubmitTurnAction` (`BattleHub.cs:66`) starts a new Activity for each call with no parent (because the AspNetCore instrumentation only fires on raw HTTP requests, not on SignalR hub-method dispatch — and even when SignalR's own instrumentation runs, there's no incoming `traceparent` to extract).

**Affected**: every cross-service trace involving SignalR. The "BFF → Battle → Postgres" tree the user expected does not exist.

**Diagnosis confidence**: high. Confirmed by reading code and matching against Jaeger's actual per-service single-span traces.

### Root cause #3 — Dashboard expects Npgsql metrics but the extension never registers the Npgsql meter

**Evidence:** `KombatsObservabilityExtensions.cs:62-75` — the `WithMetrics(...)` block lists `AddAspNetCoreInstrumentation`, `AddHttpClientInstrumentation`, `AddRuntimeInstrumentation`, `AddProcessInstrumentation`, `AddMeter(meterName)`, `AddMeter("MassTransit")`. There is **no** `AddMeter("Npgsql")`.

`Npgsql.OpenTelemetry` (pinned at 10.0.0) emits the `db.client.operation.duration` family (Prom: `db_client_operation_duration_seconds_*`) when its meter is subscribed via `AddMeter("Npgsql")`. Without that, no Postgres metrics flow.

Dashboard panel "Postgres command duration p95" (`kombats-overview.json` line containing `db_client_operation_duration_seconds_bucket{db_system="postgresql"}`) will continue to show **No data** even after rebuild.

**Affected**: Postgres command-duration panel.

**Diagnosis confidence**: high (code-read only).

### Root cause #4 — Redis instrumentation is trace-only; the Redis panel will always be empty

**Evidence:** Already documented in `observability/README.md:162-163`: *"Redis metrics: `OpenTelemetry.Instrumentation.StackExchangeRedis` is trace-only as of `1.12.0-beta.1`."*

The dashboard panel "Redis trace rate" uses `db_client_operation_duration_seconds_count{db_system="redis"}` — a metric that does not exist in OTel .NET as of the pinned versions. This is a known limitation, but the panel is wired up to a query that will never return data, which contributes to the user's "no data" impression.

**Affected**: Redis panel only.

**Diagnosis confidence**: high (pre-documented).

---

## Section D — Recommended fixes (DO NOT APPLY YET)

| # | Fix | Files |
|---|---|---|
| 1 | **Rebuild the backend Docker images** so they contain the observability foundation. After `git checkout fix/observability-compose-paths` (or whatever branch carries `2f4e17d`+`b2b38fd`), run `docker compose -f docker-compose.yml -f observability/docker-compose.observability.yml -f observability/docker-compose.observability.override.yml build` then `up -d --force-recreate`. Alternatively use `--build` on `up`. This fixes root cause #1 — every category-1/2/3 panel will populate, and Jaeger service names normalise to lowercase. | (operational, no code change) |
| 2 | **Add `AddMeter("Npgsql")`** to the metrics pipeline so Postgres command duration metrics flow. | `src/Kombats.Common/Kombats.Observability/KombatsObservabilityExtensions.cs:70` — append `.AddMeter("Npgsql")` after `.AddMeter("MassTransit")`. |
| 3 | **Propagate trace context for SignalR hub-method calls** so BFF → Battle traces stitch into one tree. Two options: (a) Add a custom SignalR `IHubFilter` on both Battle and BFF hubs that reads `traceparent` from the first argument or a side-channel and continues the trace. (b) Manually start a child Activity around each `connection.InvokeAsync("SubmitTurnAction", ...)` and pass the trace context as an extra parameter, then have the Battle hub method read it and start a child. Both require coordinated code on both ends. | `src/Kombats.Bff/Kombats.Bff.Application/Relay/BattleHubRelay.cs` (invoke side) and `src/Kombats.Battle/Kombats.Battle.Infrastructure/Realtime/SignalR/BattleHub.cs` (receive side). |
| 4 | **Remove the misleading Redis panel** or relabel it explicitly as "Redis traces — see Jaeger" with no Prometheus query. As-is the panel implies metrics exist when they don't. | `observability/grafana/dashboards/kombats-overview.json` — replace the `Redis trace rate (spans/s) — placeholder` panel with a `text` panel that links to Jaeger search filter `service=battle db.system=redis`. |
| 5 | **(Optional, smaller polish)** Map `/health/ready` in BFF Program.cs so the standard `curl :5000/health/ready` doesn't 404. Not on the observability critical path but the diagnostic showed it. | `src/Kombats.Bff/Kombats.Bff.Bootstrap/Program.cs` |

Fix #1 is the unblocker. Fixes #2-4 are independent polish items the user can prioritise separately. **Fix #1 alone takes the dashboard from "no data" on 8+ panels to "data flowing" on every panel except Postgres command duration and Redis trace rate.**

---

## Section E — Open questions

1. **Was the image staleness caused by user expectation or by missing automation?** `docker compose up -d` (without `--build`) re-uses local images if they exist. The user's smoke-test command did not include `--build`. Should we add a `build` step to the README's Mode B invocation, or document that rebuilding is required when the C# code changes? Both are sustainable; the README currently does neither.

2. **Does the user want SignalR hub-method trace propagation in this iteration or as a follow-up?** Fix #3 is a meaningful code change (filters on both sides). The "foundation" commit message did not commit to this, and `observability/README.md:135` says "`AddSource("Kombats.{serviceName}")` — reserved for our own custom ActivitySource (none defined yet)" — implying multi-service stitching is deferred. Confirm before any work begins.

3. **Is "no data" on the Postgres panel acceptable until fix #2 is taken?** It's a 1-line change but it's not strictly part of the diagnosis user reported.

4. **Was the apparent service-name capitalization (`Kombats.Battle` in Jaeger) something the user wants to keep, or replace with the lowercase `battle` form the foundation introduced?** Affects dashboard reading habits, Jaeger search bookmarks, and the eventual Application Insights `cloud.role` mapping. Foundation went lowercase by default but it's worth a sanity check before users build muscle memory.

5. **Are there any other consumers of the old `Kombats.Battle`-style service name in the deployed Bicep / App Insights query collection that I haven't found?** `RECON_REPORT.md` reported no metrics existed before this work, but if there are existing alert rules or saved searches keyed on the capitalized form, they need re-pointing after the rebuild.

---

*All findings collected against running containers at 2026-05-11 06:42 UTC; backend image SHAs from the same `docker compose images` listing.*
