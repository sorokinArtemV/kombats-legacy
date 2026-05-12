# Kombats observability stack (local only)

Local OpenTelemetry collector + Prometheus + Jaeger + Grafana for inspecting
the .NET services in `src/Kombats.*`. The Azure Bicep deploy (`infra/`) is
not touched by anything in this folder.

## What's here

- `docker-compose.observability.yml` — the four-container observability stack
  (otel-collector, jaeger, prometheus, grafana). Standalone-runnable.
- `docker-compose.observability.override.yml` — adds the
  `OpenTelemetry__OtlpEndpoint` env var to each backend service in the root
  `docker-compose.yml`. Only meaningful when merged with both files.
- `otel-collector/config.yaml` — OTLP in (4317 gRPC, 4318 HTTP), Prometheus
  scrape exporter out (`:8889`), OTLP→Jaeger trace exporter out.
- `prometheus/prometheus.yml` — single scrape config pointed at the collector.
- `grafana/provisioning/datasources/datasources.yml` — provisions Prometheus
  and Jaeger datasources.
- `grafana/provisioning/dashboards/dashboards.yml` — file-based dashboard
  provider.
- `grafana/dashboards/kombats-overview.json` — the one dashboard, three rows:
  Hot Path, Infra, Runtime.

## Why

`RECON_REPORT.md` Section 7 noted that the services already configure
`OpenTelemetry.AddOpenTelemetry()` with an OTLP exporter, but the endpoint
was empty so all traces went to `/dev/null` and there were zero application
metrics. The shared library `src/Kombats.Common/Kombats.Observability/`
adds metrics + the custom Kombats meter; this stack receives them.

## How to start

> **All `docker compose` commands must be run from the repo root.** Volume
> mounts in `docker-compose.observability.yml` are anchored to
> `${PWD}/observability/...` so they resolve correctly whether this file is
> loaded alone or merged with the root `docker-compose.yml`. Running from a
> different working directory will cause Docker to fail to mount
> `otel-collector/config.yaml`, `prometheus.yml`, and the Grafana
> provisioning directories.

There are two run modes depending on where the backend services are running.

### Mode A — backends run from your IDE (Rider/VS) against root infra

The default in this repo. Start only the observability stack from the repo
root:

```bash
docker compose -f observability/docker-compose.observability.yml up -d
```

Each service's `appsettings.Development.json` ships
`OpenTelemetry:OtlpEndpoint=http://localhost:4317`, so the collector picks
up traffic as soon as you F5 a service.

### Mode B — everything in Docker

From the repo root, merge all three compose files into one project. The
`.override.yml` file injects
`OpenTelemetry__OtlpEndpoint=http://otel-collector:4317` into each backend
service so they reach the collector via its container name.

```bash
docker compose \
  -f docker-compose.yml \
  -f observability/docker-compose.observability.yml \
  -f observability/docker-compose.observability.override.yml \
  up -d
```

If you run only `docker compose -f docker-compose.yml up -d` (the existing
default) the overrides are not applied and the services emit to nothing,
exactly the same behaviour as before this stack landed.

## URLs

| UI | URL | Notes |
|----|-----|-------|
| Grafana | http://localhost:3001 | Port 3001 because the React client (`src/Kombats.Client/`) pins Vite to 3000. Anonymous access enabled with Admin role; login as `admin`/`admin` if you want to edit. |
| Jaeger | http://localhost:16686 | Search by service `battle`, `bff`, `players`, `matchmaking`, `chat`. |
| Prometheus | http://localhost:9090 | Targets at `/targets`; metrics explorer at `/graph`. |
| OTLP gRPC | http://localhost:4317 | Inbound from services. |
| OTLP HTTP | http://localhost:4318 | Inbound from services. |
| Collector metrics | http://localhost:8889 | Raw Prometheus exposition; useful if a metric is missing in Grafana. |

## How to stop

Without losing volumes:

```bash
docker compose -f observability/docker-compose.observability.yml down
```

Or, with the merged stack:

```bash
docker compose \
  -f docker-compose.yml \
  -f observability/docker-compose.observability.yml \
  -f observability/docker-compose.observability.override.yml \
  down
```

## Reset volumes

```bash
docker compose -f observability/docker-compose.observability.yml down -v
```

Prometheus retention is also capped at 24h via `--storage.tsdb.retention.time`
in the compose file.

## What is emitted, and where it's defined

### Custom Kombats metrics

Defined in `src/Kombats.Common/Kombats.Observability/KombatsMetrics.cs`. One
`Meter` per process named `Kombats.{serviceName}` (e.g. `Kombats.battle`).

> **Important — building PromQL queries:** the OpenTelemetry Prometheus
> exporter does **not** prepend the Meter name to instrument names, so PromQL
> queries on `kombats_*` will always return empty. Use the bare instrument
> names below. Histograms additionally append `_<unit>` (e.g. unit `"ms"` →
> `_milliseconds`) and the standard `_bucket` / `_count` / `_sum` suffixes
> per Prometheus convention. UpDownCounters appear under the bare name.

> **Important — export interval:** the OpenTelemetry .NET SDK's
> `PeriodicExportingMetricReader` defaults to a **60-second** export interval.
> With short-lived state (e.g. a load-test battle that starts and ends in
> ~1 s), an UpDownCounter like `active_battles` rises and falls inside a
> single export window so the SDK pushes a steady 0 to Prometheus and the
> transition is invisible. To unblock short tests, the local-dev configs
> override this via `appsettings.Development.json`:
> ```json
> "OpenTelemetry": { "MetricExportIntervalMs": 5000 }
> ```
> Wired in `KombatsObservabilityExtensions.cs` via the two-arg
> `AddOtlpExporter((opt, readerOpt) => ...)` overload — applied to the
> metrics pipeline only. Production `appsettings.json` does NOT set the key
> and falls back to the SDK default of 60 s. Histograms / counters
> (`turn_resolution_duration_milliseconds_*`) are unaffected by the interval —
> they aggregate values across the window, so all observations land
> regardless.

**Custom instruments (Prometheus names):**

| Prometheus name | Type | Emitted by |
|---|---|---|
| `active_battles` | UpDownCounter (gauge) | battle — +1 in `BattleLifecycleAppService.HandleBattleCreatedAsync` when Turn 1 opens, -1 in `BattleTurnAppService.CommitAndNotifyBattleEnded` on the `EndedNow` branch |
| `active_signalr_connections` | UpDownCounter (gauge) | battle, bff — `OnConnectedAsync` / `OnDisconnectedAsync` in `Kombats.Battle.Infrastructure.Realtime.SignalR.BattleHub` and `Kombats.Bff.Api.Hubs.BattleHub` |
| `downstream_hub_connections` | UpDownCounter (gauge) | bff — `BattleHubRelay` add/remove inside `_connections` |
| `turn_resolution_duration_milliseconds_{bucket,count,sum}` | Histogram | battle — `BattleTurnAppService.ResolveTurnAsync` stopwatch. Instrument is declared as `turn_resolution_duration` with `unit: "ms"`, exported with the `_milliseconds` suffix. |
| `pairing_duration_ms_milliseconds_{bucket,count,sum}` | Histogram | matchmaking — registered, not yet recorded. (Instrument declared `pairing_duration_ms` with `unit: "ms"` so currently produces a doubly-unit-suffixed name; will be cleaned up the same way `turn_resolution_duration` was.) |
| `queued_players` | UpDownCounter (gauge) | matchmaking — registered, not yet recorded |

### Auto-instrumented metrics

Registered in `src/Kombats.Common/Kombats.Observability/KombatsObservabilityExtensions.cs`:

- `OpenTelemetry.Instrumentation.AspNetCore` — `http.server.request.duration` (s), kestrel_*, aspnetcore_authentication_*, etc.
- `OpenTelemetry.Instrumentation.Http` — `http.client.request.duration` (s).
- `OpenTelemetry.Instrumentation.Runtime` — `dotnet.gc.*`, `dotnet.thread_pool.*`, `dotnet.jit.*`, `dotnet.assembly.count`, etc.
- `OpenTelemetry.Instrumentation.Process` — `process.cpu.time` (`process_cpu_time_seconds_total` in Prom), `process.memory.usage`, `process.threads`, `process.cpu.count`.
- `Meter("MassTransit")` — `messaging.masstransit.publish.duration`, `messaging.masstransit.consume.duration`, related counts.
- `Meter("Npgsql")` — `db.client.commands.executing`, `db.client.commands.duration`, `db.client.connections.*`. Use the `db_system="postgresql"` label to filter.

### Traces

- AspNetCore + HttpClient instrumentation.
- `AddSource("Npgsql")` — Postgres command spans.
- `AddRedisInstrumentation()` — StackExchange.Redis spans (per `RECON_REPORT.md`, Redis is the primary battle-state store, so these spans matter).
- `AddSource("MassTransit")` — every consume/publish.
- `AddSource("Kombats.{serviceName}")` — reserved for our own custom ActivitySource (none defined yet).

## Verification

1. Start the stack in your preferred mode above.
2. With backends running, hit a service: e.g. `curl -i http://localhost:5001/health/ready` against Players.
3. Open Grafana → "Kombats overview". The "HTTP request rate" panel should
   light up within ~10s.
4. Open Jaeger → search service `players` → at least one trace should
   appear from that health probe.
5. To see `turn_resolution_duration_milliseconds_*` on the dashboard, drive
   a real battle through (queue two players, both submit actions, wait for
   the resolution). `active_battles` should rise to 1 and fall back to 0
   when the battle ends.

If a panel is stuck empty after manual traffic, check, in order:
- `http://localhost:8889` — does the metric exist in the raw exposition?
- `http://localhost:9090/targets` — is `otel-collector` `UP`?
- Service logs — search for `OpenTelemetry` to see exporter errors.

## Limitations / open questions

- **`pairing_duration_ms` and `queued_players`** are registered but not yet
  wired into Matchmaking code. The shared `KombatsMetrics` class declares
  them so the Meter is uniform across services; the panels will simply be
  empty until those hooks are added in a follow-up.
- **Redis metrics**: `OpenTelemetry.Instrumentation.StackExchangeRedis` is
  trace-only as of `1.12.0-beta.1`. Use Jaeger for per-command latency.
- The Bicep deploy (`infra/`) is intentionally not touched. To send Azure
  Container Apps telemetry into a managed collector, the
  `OpenTelemetry__OtlpEndpoint` env var needs to be added to `commonBackendEnv`
  in `infra/workload.bicep:247–284` — out of scope for this stack.
