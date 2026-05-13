# Kombats Metrics Cookbook

A task-oriented reference for the Prometheus metrics emitted by the Kombats stack. Each section starts with a question you'd actually ask during a load test and gives the PromQL query that answers it, plus what the answer means.

For the catalogue of every available instrument and where it's defined in code, see [`observability/README.md`](../../observability/README.md). This document assumes you already know the metric exists — it's about getting useful numbers out of it.

> **Quick reminder on names.** The OpenTelemetry Prometheus exporter does NOT prepend the Meter name (`Kombats.battle`) to instrument names. Use bare names like `active_battles`, not `kombats_active_battles`. Histograms with `unit: "ms"` get a `_milliseconds` suffix appended automatically.

---

## Table of contents

1. [Quick health check](#quick-health-check)
2. [Battle hot path — latency and throughput](#battle-hot-path)
3. [Game state — battles, connections, players](#game-state)
4. [HTTP layer — request rate, errors, latency](#http-layer)
5. [Database — Postgres command performance](#database)
6. [Messaging — MassTransit publish/consume](#messaging)
7. [.NET runtime — CPU, memory, GC, thread pool](#runtime)
8. [Cross-cutting load test recipes](#load-test-recipes)
9. [Investigating an anomaly](#investigating-an-anomaly)

---

## Quick health check

Before a load test, run these three queries to confirm the pipeline is alive.

### Is Prometheus actually scraping?

```promql
up{job="otel-collector"}
```

Returns `1` if Prometheus successfully reached the collector on its last scrape. `0` means the collector is down or unreachable. Anything else (no result) means Prometheus isn't configured to scrape it.

### Are all five backend services emitting?

```promql
count by (service_name) (process_cpu_time_seconds_total)
```

You should see five rows: `battle`, `bff`, `players`, `matchmaking`, `chat`. Missing service = either it crashed or its OTLP env var isn't set.

### Did metrics actually arrive recently?

```promql
scrape_samples_scraped{job="otel-collector"}
```

Should be in the thousands once services have been running for a minute. If it's `0`, you caught the scrape between OpenTelemetry SDK push cycles — wait ~60s and try again. The SDK's `PeriodicExportingMetricReader` defaults to a 60-second push interval.

---

## Battle hot path

The two questions that matter most under load: how fast are turns resolving, and how many are resolving per second.

### What's the p50/p95/p99 turn resolution latency?

```promql
histogram_quantile(0.50, sum(rate(turn_resolution_duration_milliseconds_bucket[5m])) by (le))
histogram_quantile(0.95, sum(rate(turn_resolution_duration_milliseconds_bucket[5m])) by (le))
histogram_quantile(0.99, sum(rate(turn_resolution_duration_milliseconds_bucket[5m])) by (le))
```

Returns milliseconds because of the `_milliseconds` unit suffix. On a quiet local stack expect p99 in the 5–50ms range. Under load, this is the metric whose growth tells you the engine is saturating. The `[5m]` window smooths transient spikes; use `[1m]` for a more reactive view during active investigation.

### How many turns per second are resolving?

```promql
sum(rate(turn_resolution_duration_milliseconds_count[1m]))
```

Total turn throughput across the cluster. Multiply by ~2 for "actions per second" if each turn is two player submissions.

### What's the average turn time?

```promql
rate(turn_resolution_duration_milliseconds_sum[5m])
/
rate(turn_resolution_duration_milliseconds_count[5m])
```

Average is a weaker signal than p95/p99 (it hides tail latency), but useful for trend comparison across long runs.

### Is latency growing over the last hour?

```promql
histogram_quantile(0.95, sum(rate(turn_resolution_duration_milliseconds_bucket[5m])) by (le))
  -
histogram_quantile(0.95, sum(rate(turn_resolution_duration_milliseconds_bucket[5m] offset 1h)) by (le))
```

Positive = degrading. Negative = improving. Use this when comparing two phases of a sustained load test.

---

## Game state

How many battles and players are live right now.

### How many battles are in progress?

```promql
active_battles
```

Current value. Goes up on battle creation, down on battle end.

### Peak concurrent battles over the last hour

```promql
max_over_time(active_battles[1h])
```

Useful for capacity reporting: "we sustained N battles".

### How many SignalR clients are connected?

```promql
sum by (service_name) (active_signalr_connections)
```

Split by `battle` (game clients) and `bff` (frontend clients). They should be roughly equal — each frontend connection produces a downstream connection to Battle. Big divergence means a leak somewhere.

### Are BFF's downstream connections tracking its frontend connections?

```promql
active_signalr_connections{service_name="bff"} - downstream_hub_connections
```

Should be ~0. Non-zero means BFF accepted frontend connections but didn't create corresponding downstream connections to Battle (or vice versa — failed to clean up).

---

## HTTP layer

For the REST surface — `/health`, `/auth`, matchmaking endpoints, etc.

### Request rate per service

```promql
sum by (service_name) (rate(http_server_request_duration_seconds_count[1m]))
```

Requests per second, summed across all routes. Spikes here correspond to player actions hitting the API.

### Request rate per route on a single service

```promql
sum by (http_route) (rate(http_server_request_duration_seconds_count{service_name="bff"}[1m]))
```

Useful when one service is hot — figure out which endpoint is taking the traffic.

### p95 HTTP latency per service

```promql
histogram_quantile(0.95,
  sum by (service_name, le) (rate(http_server_request_duration_seconds_bucket[5m]))
)
```

Result is in **seconds**, not milliseconds — `http_server_request_duration_seconds` is the unit. Multiply by 1000 if you want a ms display in Grafana.

### Error rate per service (% of 4xx + 5xx)

```promql
sum by (service_name) (rate(http_server_request_duration_seconds_count{http_response_status_code=~"4..|5.."}[1m]))
/
sum by (service_name) (rate(http_server_request_duration_seconds_count[1m]))
```

A clean baseline shows ~0. Above 0.01 (1%) under load is worth investigating — usually a downstream timeout or auth misconfiguration.

### Specifically 5xx (real server errors)

```promql
sum by (service_name) (rate(http_server_request_duration_seconds_count{http_response_status_code=~"5.."}[1m]))
```

These are unambiguous bugs. 4xx might be the client's fault; 5xx is yours.

### Slowest routes right now

```promql
topk(5,
  histogram_quantile(0.95,
    sum by (http_route, service_name, le) (rate(http_server_request_duration_seconds_bucket[5m]))
  )
)
```

Top 5 routes by p95 latency across the cluster. Quick way to find what's slow.

---

## Database

Postgres command duration via the Npgsql instrumentation. Requires `AddMeter("Npgsql")` to be registered.

### p95 Postgres command duration per service

```promql
histogram_quantile(0.95,
  sum by (service_name, le) (rate(db_client_operation_duration_seconds_bucket{db_system="postgresql"}[5m]))
)
```

In seconds. A Postgres command going from 2ms to 50ms p95 under load almost always means connection pool exhaustion or contention on a hot table.

### p95 broken down by operation type

```promql
histogram_quantile(0.95,
  sum by (db_operation, le) (rate(db_client_operation_duration_seconds_bucket{db_system="postgresql"}[5m]))
)
```

Separates SELECT, INSERT, UPDATE. If INSERT p95 climbs but SELECT stays flat, you're seeing write contention — typical of bursty battle-end events all hitting one table.

### How many Postgres commands per second per service?

```promql
sum by (service_name) (rate(db_client_operation_duration_seconds_count{db_system="postgresql"}[1m]))
```

Counts commands, not transactions. Roughly proportional to load.

---

## Messaging

MassTransit publish/consume metrics. Available because `Meter("MassTransit")` is registered.

### Message publish rate per service

```promql
sum by (service_name) (rate(messaging_masstransit_publish_duration_count[1m]))
```

Messages published per second. Battle-end events, player profile changes, etc.

### Message consume rate per service

```promql
sum by (service_name) (rate(messaging_masstransit_consume_duration_count[1m]))
```

Messages consumed. Should track publish rate at steady state — divergence means a queue is filling up.

### p95 consume duration

```promql
histogram_quantile(0.95,
  sum by (service_name, le) (rate(messaging_masstransit_consume_duration_bucket[5m]))
)
```

Time spent inside one consumer handler. Growth here under load means the handler is doing too much sync work; consider moving heavy work out of the consumer.

### Publish vs consume gap (lag indicator)

```promql
sum(rate(messaging_masstransit_publish_duration_count[1m]))
-
sum(rate(messaging_masstransit_consume_duration_count[1m]))
```

Sustained positive value = consumers can't keep up. Look at the consumer side: prefetch, concurrency, handler latency.

---

## Runtime

.NET CLR internals. These metrics tell you whether the runtime itself is the bottleneck before the application code is.

### CPU utilization per service (fraction of one core)

```promql
sum by (service_name) (rate(process_cpu_time_seconds_total[1m]))
```

Returns the rate of CPU seconds consumed per real second. A value of `1.0` means one core fully saturated. Above `0.8` consistently is a strong signal of CPU-bound work.

### CPU utilization normalized to all cores available to the process

```promql
sum by (service_name) (rate(process_cpu_time_seconds_total[1m]))
/
on (service_name) avg by (service_name) (process_cpu_count)
```

Returns 0.0 to 1.0 regardless of core count. Cleaner for dashboards.

### Memory usage per service in MiB

```promql
process_memory_usage_bytes / 1024 / 1024
```

Working set memory. Watch for steady upward drift during a sustained load test — that's a leak. A short spike during ramp-up is normal as ASP.NET Core warms up.

### Memory growth rate (leak detector)

```promql
deriv(process_memory_usage_bytes[10m]) * 60
```

Bytes-per-minute trend over the last 10 minutes. A positive sustained value during steady-state load (no new connections being added) is a leak.

### Gen2 GC collections per second

```promql
sum by (service_name) (rate(dotnet_gc_collections_total{gc_heap_generation="gen2"}[1m]))
```

Gen2 collections are expensive — they pause everything for several ms. Anything above ~0.1/s under steady load is suspicious and worth a memory-allocation profile.

### Allocation rate in MiB/s

```promql
sum by (service_name) (rate(dotnet_gc_heap_total_allocated_bytes[1m])) / 1024 / 1024
```

High allocation rate causes frequent GCs. Common culprits: LINQ allocations in hot paths, string concatenation, unnecessary `ToList()` calls, captured closures.

### Thread pool queue length

```promql
dotnet_thread_pool_queue_length
```

Non-zero is bad. Sustained non-zero means the thread pool can't keep up — work items are queueing for an available thread. Almost always indicates blocked I/O (`.Result`, `.Wait()`, sync DB calls) starving the pool.

### Thread pool thread count

```promql
dotnet_thread_pool_thread_count
```

Should stabilize around 2× core count under load. If it climbs continuously, the runtime is spawning threads to compensate for blocked work — same root cause as queue length growth.

---

## Load test recipes

Multi-metric queries useful for specific load-test scenarios.

### Are we CPU-bound or I/O-bound?

Run these side by side:

```promql
# CPU utilization
sum by (service_name) (rate(process_cpu_time_seconds_total[1m]))

# Thread pool queue
dotnet_thread_pool_queue_length

# HTTP p95
histogram_quantile(0.95, sum by (service_name, le) (rate(http_server_request_duration_seconds_bucket[5m])))
```

Pattern interpretation:
- CPU high (≥0.8), queue low, latency high → **CPU-bound**. Scale out or optimize hot path.
- CPU low, queue high, latency high → **I/O-bound** with blocked threads. Find the sync wait.
- CPU low, queue low, latency high → **External dependency slow** (DB, Redis, downstream service). Check those metrics.

### Where is the time going in a battle turn?

Turn latency = engine + Redis ops + DB ops + SignalR broadcast. Compare:

```promql
# Turn end-to-end
histogram_quantile(0.95, sum(rate(turn_resolution_duration_milliseconds_bucket[5m])) by (le))

# Postgres calls during a turn (in ms)
histogram_quantile(0.95, sum by (le) (rate(db_client_operation_duration_seconds_bucket{db_system="postgresql",service_name="battle"}[5m]))) * 1000
```

If Postgres p95 climbs but turn p95 stays roughly flat, the engine is absorbing Postgres slowness via something async. If both climb together, Postgres is on the critical path.

For finer breakdown (which sub-operation in the turn is slow), Prometheus can't help — switch to Jaeger and inspect a real trace.

### How many concurrent players can one BFF replica handle?

During a ramp-up, watch:

```promql
active_signalr_connections{service_name="bff"}
```

against:

```promql
rate(process_cpu_time_seconds_total{service_name="bff"}[1m])
```

The point where BFF CPU hits 0.8+ and stays there is the per-replica ceiling. Note the `active_signalr_connections` value at that moment — that's your capacity number.

### Pool exhaustion detection

```promql
# Postgres command duration p95 over time
histogram_quantile(0.95,
  sum by (service_name, le) (rate(db_client_operation_duration_seconds_bucket{db_system="postgresql"}[1m]))
)
```

Watch this over a load ramp. A characteristic "knee" — where the curve goes from flat to nearly vertical — is connection pool exhaustion. The pool size is 20 per service in this repo (see `RECON_REPORT.md` Section 9). Beyond that, requests queue waiting for a connection.

---

## Investigating an anomaly

When you see a spike or degradation on the dashboard, this is the path I'd walk:

1. **Confirm the metric is real**, not a stale buffer. Run `scrape_samples_scraped` — if low, wait 60s.
2. **Localize to a service.** Whatever the symptom (high latency, errors, memory), add `by (service_name)` to the query. One service is usually the source.
3. **Localize within the service.** For HTTP issues: `by (http_route)`. For DB: `by (db_operation)`. For MassTransit: `by (messaging_destination_name)`.
4. **Switch to Jaeger.** Once you know which service and roughly when, find a slow trace at `http://localhost:16686`. Filter by service and by duration (Min Duration field). One slow trace tells you more than a thousand metric data points.
5. **Read logs for that window.** `docker compose logs <service> --since=5m` filtered to the time of the anomaly.

Metrics tell you **what** is wrong and **where**. Traces tell you **why**. Logs tell you **why exactly**. Use them in that order — going straight to logs without a metric in mind is how you lose hours.

---

## Reference: label names

Every metric carries these resource labels:

| Label | Values | Use |
|---|---|---|
| `service_name` | `battle`, `bff`, `players`, `matchmaking`, `chat` | Primary filter |
| `service_namespace` | `kombats` | Constant; ignore |
| `deployment_environment` | `Development`, `Production` | Filter across envs sharing a Prometheus |

Additional labels on specific metric families:

| Metric family | Extra labels |
|---|---|
| `http_server_request_duration_seconds_*` | `http_route`, `http_request_method`, `http_response_status_code` |
| `db_client_operation_duration_seconds_*` | `db_system`, `db_operation`, `db_collection_name` |
| `messaging_masstransit_*` | `messaging_destination_name`, `messaging_operation` |
| `dotnet_gc_collections_total` | `gc_heap_generation` (`gen0`/`gen1`/`gen2`) |
| `process_cpu_time_seconds_total` | `state` (`user`/`system`) |
