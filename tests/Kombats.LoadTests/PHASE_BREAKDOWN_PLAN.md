# Phase Breakdown Plan — Where Do the 21 Seconds Go?

## Question we're answering

The last load run (25 pairs × 2 min, commit `95af2d7`) reported NBomber p50 iteration
latency = **21 086 ms**, while the actual battle is **~44–105 ms** (`battleMs` in the
status-code message). So roughly **20 980 ms per iteration is unaccounted for**.

We need a per-iteration breakdown across the bot's phases (auth, onboard, connect,
queue wait, joinBattle, battle, cleanup) for every iteration of a load run, so we
can compute p50/p95/p99 per phase and identify the dominant cost.

## What we already have (the lucky finding)

The VirtualPlayer **already measures every phase** with `Stopwatch` and packages
them into `VirtualPlayerResult` (see `VirtualPlayer/VirtualPlayer.cs:47-50, 60, 70,
81, 114, 125, 166`). The fields are:

| Field on `VirtualPlayerResult` | Phase | Where it's measured |
|---|---|---|
| `AuthDuration` | Keycloak token fetch (cached after the first call) | `VirtualPlayer.cs:58-60` |
| `OnboardDuration` | onboard → setName → changeAvatar → allocateStats | `VirtualPlayer.cs:68-70` |
| `ConnectDuration` | SignalR `/battlehub` negotiate + connect | `VirtualPlayer.cs:73-81` |
| `QueueWait` | `JoinQueue` + poll-loop until `Matched` (with retry on `Queue.NotReady`) | `VirtualPlayer.cs:84-114` |
| `JoinBattleDuration` | hub `JoinBattle` invocation that returns the snapshot | `VirtualPlayer.cs:123-125` |
| `BattleDuration` | turn loop until `BattleEnded` | `VirtualPlayer.cs:129-166` |
| `TotalDuration` | full `RunOneBattleAsync` wall-clock | `VirtualPlayer.cs:47, 374` |

The smoke scenario already prints these (`Scenarios/SingleBattleScenario.cs:68-73`).
**The load scenario silently discards everything except `BattleDuration`** — it only
goes into the NBomber status-code message as `battleMs=...`
(`Scenarios/ConcurrentBattlesScenario.cs:74`).

So we don't need to *add measurement*. We need to *expose what's already measured*.

## Measurement approach — chosen: Option (a), JSONL-per-iteration

Three options were on the table:

| | Approach | Pros | Cons |
|---|---|---|---|
| **(a)** | Append one JSON line per iteration to `reports/iterations-<ts>.jsonl` | Reuses existing Stopwatch values — minimal code change. Captures failed/timed-out iterations too (where Option c would just truncate). Aggregation = one jq one-liner. No new deps, no NBomber license risk. | Not visible in Jaeger. (We don't need it to be — these are client-side wall clocks. Server-side HTTP latency is already in Prometheus by `http_route`.) |
| (b) | OTel `ActivitySource` with `phase` attribute → Jaeger | Spans visible in Jaeger, queryable by tag. | Larger refactor, more moving parts (exporter resources, sampler config), and per-iteration cardinality may overwhelm Jaeger's UI for a 2-min run. |
| (c) | Wrap each phase in `NBomber.Step.Run("phase", ...)` | Stats roll up natively in NBomber report. | Requires restructuring `RunOneBattleAsync` (closures capture per-step state); NBomber Community license counts steps toward its limits; failed steps drop later steps from the report. |

**Decision: (a).** Reasoning:
1. The data already exists in `VirtualPlayerResult`. We're just choosing where to
   write it. JSON-lines is the smallest possible change.
2. jq aggregation reproduces NBomber's percentile math in one shell pipeline, so
   we can verify our numbers against NBomber's reported 21 086 ms p50 as a sanity
   check (sum of phases should ≈ TotalDuration ≈ NBomber latency).
3. Failed iterations (QueueTimeout, Error) keep their phase data — important
   because **23 of the 25 failures in the last run were QueueTimeout**, and those
   are exactly the ones where we want to see the long `QueueWait` value to
   confirm the bot really waited the full configured timeout.

## What gets written — one JSON object per iteration

```json
{
  "ts": "2026-05-11T17:08:42.123+05:00",
  "username": "loadbot-0007",
  "battle_id": "7aa4021e-...",
  "outcome": "Won",
  "error": null,
  "turns_played": 7,
  "auth_ms": 1.2,
  "onboard_ms": 145.7,
  "connect_ms": 312.4,
  "queue_wait_ms": 18512.0,
  "join_battle_ms": 102.5,
  "battle_ms": 44.0,
  "total_ms": 19117.8
}
```

`outcome` and `error` are included so we can slice (e.g. "p95 queue wait for
QueueTimeout outcomes" vs. "p95 queue wait for Won outcomes").

## Files to be modified

1. **NEW: `tests/Kombats.LoadTests/Reporting/IterationRecorder.cs`** —
   thread-safe JSONL writer. Opens `reports/iterations-<utcTimestamp>.jsonl`,
   exposes `Record(VirtualPlayerResult)`, locks around the file write. Disposable.
   System.Text.Json source generator not required — single-line `JsonSerializer.Serialize`
   is fine; this is not a hot path (1.1 RPS).

2. **MODIFY: `tests/Kombats.LoadTests/Scenarios/ConcurrentBattlesScenario.cs`** —
   - Create the recorder once before `Scenario.Create` (named via the NBomber
     session start time so it lines up with the NBomber report file names).
   - Inside the scenario closure, after `RunOneBattleAsync` returns, call
     `recorder.Record(result)` regardless of outcome.
   - Dispose the recorder after `NBomberRunner.Run()` returns.
   - Log the JSONL path next to the existing "ok/fail" line so the operator
     knows where to look.

3. **NEW: `tests/Kombats.LoadTests/scripts/aggregate-phases.sh`** —
   small jq pipeline that takes a JSONL path and prints a markdown table:

   ```
   phase            count   p50    p95    p99    max
   auth_ms          157     1.1    2.4    5.8    12
   onboard_ms       157     143    298    412    1832
   connect_ms       157     287    612    788    1421
   queue_wait_ms    157     18411  29812  29998  29999
   join_battle_ms   132     98     245    412    611
   battle_ms        132     44     142    198    412
   total_ms         157     19012  29998  30001  30002
   ```

   (Numbers above are illustrative, not predictions.)

   The script will also split by outcome so we can see e.g. queue_wait_ms p95 for
   `Won` vs. `QueueTimeout` separately. Implementation: one `jq -s` pass per phase
   per outcome — about 30 lines of bash.

4. **NO CHANGES** to `VirtualPlayer.cs`, `VirtualPlayerResult.cs`, BFF client, or
   anything under `src/Kombats.*`. The measurement is already there.

## How the output is aggregated

Manual run:

```bash
# pick latest run
JSONL=$(ls -t tests/Kombats.LoadTests/reports/iterations-*.jsonl | head -1)

# overall p50 / p95 / p99 per phase
bash tests/Kombats.LoadTests/scripts/aggregate-phases.sh "$JSONL"

# or ad-hoc, e.g. p95 of queue_wait_ms across all Won iterations
jq -s 'map(select(.outcome=="Won")) | map(.queue_wait_ms) | sort | .[(length*95/100)|floor]' "$JSONL"
```

## Sanity check baked into Phase 2 report

After we have the data we'll verify:

```
sum(p50 per phase) ≈ p50 total_ms ≈ NBomber p50 iteration latency
```

If those three don't agree within ~5%, we have an unmeasured gap (most likely
candidate: the post-iteration `LeaveQueueAsync` in the `finally` block at
`VirtualPlayer.cs:185-196`, which currently isn't a tracked phase). If that gap
shows up we'll add `cleanup_ms` as a follow-up.

## Cross-checks with Prometheus

Once Phase 2 produces a candidate dominant phase (almost certainly `queue_wait_ms`
based on the queue-timeout failure count and the heartbeat fix story), we cross-
check against server-side HTTP metrics:

- `histogram_quantile(0.95, sum by (http_route, le) (rate(http_server_request_duration_seconds_bucket[2m])))`
  → which routes were slow on the **server** side during the run.
- If `queue_wait_ms` dominates the client side but no single HTTP route is slow on
  the server side, then the wait is real wait-for-pair (matchmaking queueing delay,
  not request latency), and the next investigation step is the matchmaking pairing
  worker's tick interval — not request handling. That distinction is the whole
  point of measuring client-side wall clock separately from server-side latency.

Note: the Prometheus check I ran during planning returned NaN for every route
because the last run finished hours ago and the 5–10 min rolling windows are
empty. Phase 2 will query Prometheus while the new run is still hot, or use
`query_range` over the explicit run window if querying after the fact.

## Out of scope for this iteration

- Any change to `src/Kombats.*` (server side).
- Any fix attempt. This is diagnosis only — we report the dominant phase and
  propose (not implement) the next diagnostic step.
- SignalR backplane work, queue race condition fixes, JoinBattle race — all
  known issues, all explicitly *not* the target here.
- Going above 25 pairs.

## Stop condition

Stop after writing this plan. Wait for review before implementing.
