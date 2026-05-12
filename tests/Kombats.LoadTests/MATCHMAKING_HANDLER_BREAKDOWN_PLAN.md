# Matchmaking Handler Breakdown — Plan

## Question we're answering

`MATCHMAKING_PAIRING_NOTES.md` derives **~1.25 s of per-pair handler work** from
measured throughput (67 battles / 90 s ⇒ 1.35 s cycle time ⇒ minus the 100 ms
fixed sleep ⇒ ~1.25 s inside `ExecuteMatchmakingTickHandler.HandleAsync`). That
is ~10× slower than a back-of-envelope expectation for the I/O involved on
local Postgres + Redis. We need a per-step breakdown of that 1.25 s to know
which lever from the notes (combine Redis writes? cache profiles? move outbox
off path?) is worth pursuing first.

## Justification for touching `src/Kombats.Matchmaking`

This is the **first time** we will modify code under `src/Kombats.*` in this
load-testing investigation. Framing matters:

- **What this is:** purely additive instrumentation. We add a static
  `ActivitySource` and wrap each I/O step with `using var activity =
  _source.StartActivity("...")`. Spans are no-ops when no listener is
  attached, so the code path is unchanged at runtime when OTel isn't
  exporting.
- **What this is not:** a logic change, a refactor, a re-ordering of
  operations, or a fix attempt. None of Levers 1–4 from the notes are
  implemented here. Behavior is byte-identical from the bot's perspective.
- **Why the matchmaking handler specifically:** because the wait model
  identifies this exact method (`ExecuteMatchmakingTickHandler.HandleAsync`)
  as containing the entire ~1.25 s gap, and we cannot see inside it from
  outside the service. ASP.NET Core auto-instrumentation gives us request
  latency, but this code runs in a `BackgroundService` tick — there is no
  request span to attach child spans to.
- **Risk:** essentially zero. The `ActivitySource` infrastructure is part of
  `System.Diagnostics`, has been the .NET tracing primitive since .NET 5,
  and is already used implicitly by the auto-instrumentation packages we
  ship. We're just creating one more named source.

**Expected modifications — exactly two files:**

1. `src/Kombats.Matchmaking/Kombats.Matchmaking.Application/UseCases/ExecuteMatchmakingTick/ExecuteMatchmakingTickHandler.cs`
   — add an `internal static readonly ActivitySource` field, wrap each
   distinct I/O step with `_source.StartActivity("matchmaking.pair.<step>")`,
   and wrap the entire `HandleAsync` body in one parent span
   `matchmaking.pair.tick`. Expected diff: **~15-25 lines added**, no lines
   removed, no logic changed.

2. *(Not strictly required)* `src/Kombats.Matchmaking/Kombats.Matchmaking.Application/Kombats.Matchmaking.Application.csproj`
   — add a `<PackageReference Include="System.Diagnostics.DiagnosticSource" />`
   only if this project doesn't already transitively reference it. Almost
   certainly already pulled in via ABP / OpenTelemetry, so this is a
   verify-then-skip step. Expected diff: **0 lines** in the realistic case.

**Not modified:** `KombatsObservabilityExtensions.cs` already registers
`AddSource("Kombats.matchmaking")` (see
`src/Kombats.Common/Kombats.Observability/KombatsObservabilityExtensions.cs:59`,
via `meterName = KombatsMetrics.MeterPrefix + serviceName` =
`"Kombats." + "matchmaking"`). Any `ActivitySource` named
`"Kombats.matchmaking"` is automatically exported to OTLP. **No DI wiring,
no Program.cs edits, no appsettings keys.**

## Mechanism — choosing among (a) / (b) / (c)

The three options on the table:

| | What | Where the data lives | Effort |
|---|---|---|---|
| (a) | `Stopwatch` + structured `ILogger` log line per pair | `docker logs kombats-matchmaking \| jq` | small |
| (b) | OTel spans via `ActivitySource` | Jaeger UI, queryable by name | smallest |
| (c) | Custom histogram `matchmaking_pair_step_duration_seconds` with `step` label | Prometheus / Grafana | medium |

### Recommendation: (b) OTel spans. Concur with the user's lean.

Reasons:

1. **The wiring is already done.** `AddSource("Kombats.matchmaking")` is in
   place; Jaeger is already accepting traces from this service; the OTLP
   exporter is already configured. The cost of adding spans is *one
   `static readonly ActivitySource` field plus seven `StartActivity` calls*.
   Options (a) and (c) require new log parsing or new metric definitions
   respectively.

2. **Jaeger gives a per-pair flame graph for free.** This is exactly the
   visualization we want: nested spans under a parent `matchmaking.pair.tick`,
   sorted by start time, with min/max/avg per step across all sampled
   traces. The exact question we're asking is "what's slow inside this
   method" — that's the canonical Jaeger use case.

3. **Spans carry context for the next debugging step.** If
   `save-changes` turns out to be the dominant span, we can drill *into* it
   — Postgres auto-instrumentation (`AddSource("Npgsql")` at
   `KombatsObservabilityExtensions.cs:57`) means we'll see the underlying
   SQL command latencies as child spans automatically. Options (a) and (c)
   give us a number; (b) gives us a number and the SQL command that
   produced it. That's the difference between "the commit is slow" and
   "the commit is slow because the outbox INSERT competes with the queue
   write".

4. **MassTransit instrumentation already attaches.** `AddSource("MassTransit")`
   is registered (line 58). The `_battlePublisher.PublishAsync` call already
   emits a span. Our parent span will adopt it as a child automatically.
   Free correlation between our app-level step and the framework-level
   outbox write.

### Why not (a)

Container-log aggregation works, but: (i) we'd duplicate what Jaeger does;
(ii) `docker logs` time correlation across many pairs is fiddlier than a
Jaeger filter; (iii) we'd ship a parsing script that becomes dead weight.
The one case where (a) wins is if Jaeger isn't running for some reason — and
in that case we have a bigger problem.

### Why not (c)

Histograms are the right primitive for **production dashboards** of an
*ongoing* concern. Here we're answering a one-time diagnostic question.
Building seven histograms for a question we hope to resolve in one
investigation is over-engineering. We may want to *upgrade* one or two
spans to histograms after we know what we're looking at — but starting
with histograms is putting the cart before the horse.

## Steps to measure (verified against the actual handler)

Source: `Kombats.Matchmaking.Application/UseCases/ExecuteMatchmakingTick/ExecuteMatchmakingTickHandler.cs:37-99`.

The shape proposed in the user message matches the code with one nuance:
between the two SetMatched calls and the SaveChanges, the SetMatched calls
happen *after* SaveChanges, not in between. Verified list, in code order:

| Span name | Source line(s) | What it covers |
|---|---|---|
| `matchmaking.pair.tick` *(parent)* | wraps the whole method | full `HandleAsync` wall clock — equivalent to `total-handler-ms` |
| `matchmaking.pair.try-pop` | `:41` | `_queueStore.TryPopPairAsync` (the Lua script) |
| `matchmaking.pair.profile-lookup-1` | `:50` | first `GetByIdentityIdAsync` (Postgres SELECT) |
| `matchmaking.pair.profile-lookup-2` | `:51` | second `GetByIdentityIdAsync` (Postgres SELECT) |
| `matchmaking.pair.publish-create-battle` | `:85` | `_battlePublisher.PublishAsync` — outbox INSERT prep |
| `matchmaking.pair.save-changes` | `:88` | `_unitOfWork.SaveChangesAsync` — transaction commit (the EF `Add(match)` row + the outbox row, atomic) |
| `matchmaking.pair.set-matched-1` | `:91` | first `SetMatchedAsync` (Redis write) |
| `matchmaking.pair.set-matched-2` | `:92` | second `SetMatchedAsync` (Redis write) |

Two minor adjustments to the user's proposed list:

- Added **`try-pop`**. The Lua script is part of per-pair work — even
  though it's a fixed cost, we want to confirm it isn't lurking as a
  surprise contributor.
- Renamed **`match-entity-build`** → **`publish-create-battle`**. The EF
  `Add` is in-memory and dwarfed by the outbox publish; what we actually
  want to time is the publisher call. Pure naming clarity.

The fail path (when either combat profile is null,
`ExecuteMatchmakingTickHandler.cs:53-64`) will not be instrumented in this
pass — we already get a parent span on that path, and re-queue latency is
not the question.

## How aggregation works

1. Run a load test: same profile as Phase 2 — 25 pairs × 120 s.
2. Open Jaeger at `http://localhost:16686`.
3. **Service:** `matchmaking`. **Operation:** `matchmaking.pair.tick`.
   Filter by **lookback = last 30 minutes**. Expect ~60-70 matching traces
   (one per pair formed in the steady-state window).
4. Click *Find Traces* → results sorted by duration descending. The first
   traces will be outliers; the bulk in the middle is steady state.
5. For each child span name, eyeball the duration distribution by clicking
   into 5-10 traces and reading the durations off the flame graph.
6. For a cleaner numeric view: in the Jaeger UI, **Trace Statistics** view
   shows min/avg/max per span name for any selected set of traces. That's
   the table we want — span name × stats — comparable directly to the
   per-phase tables in `PHASE_BREAKDOWN_REPORT.md`.
7. If we want PromQL-style aggregation, we can also export the traces via
   Jaeger's JSON API:

   ```bash
   curl -s 'http://localhost:16686/api/traces?service=matchmaking&operation=matchmaking.pair.tick&lookback=30m&limit=200' \
     | jq '[.data[].spans[] | {op: .operationName, dur_us: .duration}] | group_by(.op) | map({op: .[0].op, count: length, p50_ms: ([.[].dur_us] | sort | .[length/2|floor] / 1000), p95_ms: ([.[].dur_us] | sort | .[length*95/100|floor] / 1000)})'
   ```

   …which gives us a markdown-friendly table without leaving the terminal.

## Rollback plan

The whole change is one PR / one file's worth of additions. Three options
in order of preference:

1. **Just revert the commit.** Cleanest. We're on a feature branch, no
   downstream callers depend on the spans, no schema migration to undo.
2. **Strip the instrumentation by hand.** Delete the `ActivitySource`
   field, delete the seven `using var activity = ...` statements. Diff
   should be exactly the inverse of the introduction PR.
3. **Feature-flag it.** *Discouraged — see below.*

### On the feature flag

The user asked whether to feature-flag the instrumentation, default off,
on for load test runs. My recommendation: **don't**.

Reasons:
- `ActivitySource.StartActivity` is **already a no-op when no listener is
  attached**. The OTel SDK does add a listener, but the per-call overhead
  is on the order of 100 nanoseconds — eight of these in a 1.25-second
  handler is **0.0001 %** of the budget. There is no production cost to
  pay for and no reason to gate it.
- Adding an `IOptions<>` for "should we instrument" is *more* code than
  the instrumentation itself and introduces a real branch (`if (enabled)
  using var activity = ...`) that we'd then have to remove. We'd be
  adding tech debt to enable optional debt cleanup.
- Spans are already sampled by the OTLP exporter / collector. If volume
  becomes a real concern (it won't at our scale), the right knob is the
  sampler config in `KombatsObservabilityExtensions.cs`, not a per-source
  on/off in handler code.

So: ship the spans unconditionally, revert the PR if we ever want them
gone. If a later production reality changes the math, we add the flag
then.

## Sanity check (will be in the Phase-2 report of this investigation)

After the instrumented run:

1. **Sum of child span p50 ≈ parent span p50.** With the seven child spans
   covering all measurable I/O in `HandleAsync`, the parent
   `matchmaking.pair.tick` p50 should equal the sum of children p50 plus
   bookkeeping (object construction, the `Guid.NewGuid()` calls, logger
   call at line 94). If there's a > 100 ms unaccounted gap, we've missed
   a phase and need to instrument more finely.

2. **Parent p50 ≈ 1.25 s.** This is the figure
   `MATCHMAKING_PAIRING_NOTES.md` derives from measured throughput. If the
   actual handler shows p50 of, say, 300 ms, then our model in the notes
   was wrong about where the wait comes from — and we have a different
   investigation on our hands (most likely a re-examination of whether
   the worker is actually getting CPU time, or whether the lease lock
   acquire/release path is more expensive than assumed).

3. **Cross-check against measured wait.** From the existing iteration log,
   `queue_wait_ms` p50 for successful iterations = 19 806.8 ms. With 25
   pairs queued and parent-span p50 ≈ 1.25 s, the expected median wait
   is ~12.5 × (1.25 s + 100 ms) = ~16.9 s. The 2.9 s gap from 19.8 s is
   accounted for by poll lag (~250 ms) plus queue depth being slightly
   above 25 pairs during peak (more bots in queue than completing
   battles in the same instant). If the gap is much larger, we have
   another contributor.

## Out of scope

- Implementing the spans. **Plan only.**
- Any of Levers 1–4 from `MATCHMAKING_PAIRING_NOTES.md`.
- Multi-replica / Redis backplane work.
- Changing the OTel sampler or exporter configuration.
- Adding metrics for production dashboards (we may upgrade to metrics
  later if the answer turns out to be ongoing).

## Stop condition

Stop after writing this plan. Wait for review before implementing.
