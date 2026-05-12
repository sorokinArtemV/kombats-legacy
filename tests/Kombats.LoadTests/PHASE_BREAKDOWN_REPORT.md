# Phase Breakdown Report — Where the 21 Seconds Go

**Run:** 25 pairs × 120 s (commit on top of `95af2d7`, branch `fix/virtual-player-heartbeat`)
**Session:** `2026-05-11_12-48-23_f44c2450`
**Iteration log:** `tests/Kombats.LoadTests/iteration-logs/iterations-2026-05-11--17-48-23.jsonl`
**NBomber report:** `tests/Kombats.LoadTests/reports/nbomber_report_2026-05-11--12-48-23.md`

## Implementation summary

What I added (no changes to `src/Kombats.*`):

- `tests/Kombats.LoadTests/Reporting/IterationRecorder.cs` — thread-safe JSONL writer, one line per iteration, lock-guarded `WriteLine`, `AutoFlush=true`.
- `tests/Kombats.LoadTests/Scenarios/ConcurrentBattlesScenario.cs` — wires recorder into the scenario closure; **all** iterations get logged (including failed ones — important for the QueueTimeout analysis).
- `tests/Kombats.LoadTests/scripts/aggregate-phases.sh` — jq pipeline that prints overall + per-outcome + successful-vs-QueueTimeout tables.

The recorder lives in a *sibling* `iteration-logs/` folder, not under `reports/`. **NBomber's `WithReportFolder` wipes the report directory at the end of a run, including subdirectories** — discovered the hard way when my first two runs produced empty disk despite the `Record()` call returning. Cost about 15 minutes of debugging; comment in the code explains the gotcha.

## Configured queue timeout (the user-requested baseline)

```
Load.QueueTimeoutSeconds   = 60   // VirtualPlayer's per-iteration queue-wait cap
Load.PerBotTimeoutSeconds  = 120  // outer cap covering the entire iteration
Load.TestDurationSeconds   = 120  // NBomber session length
Load.RampUpSeconds         = 30   // RampingConstant up to 25 copies, then KeepConstant
```
(Source: `tests/Kombats.LoadTests/appsettings.json`, `Configuration/LoadTestOptions.cs:26-27`)

**Take-away:** `QueueTimeoutSeconds = 60` is the truncation point we need to keep in mind when reading `queue_wait_ms`. As we'll see, **no bot in this run actually hit it** — they all either got paired faster or were cut off by the test ending first.

## 1. Overall (all 159 iterations)

| phase | count | p50 ms | p95 ms | p99 ms | max ms |
|---|---:|---:|---:|---:|---:|
| auth_ms | 159 | 0.0 | 37.8 | 41.8 | 42.9 |
| onboard_ms | 159 | 3.3 | 4.5 | 5.1 | 10.6 |
| connect_ms | 159 | 3.8 | 8.5 | 12.6 | 35.9 |
| **queue_wait_ms** | **159** | **19 760.4** | **22 277.6** | **22 312.4** | **22 325.5** |
| join_battle_ms | 159 | 778.4 | 1 544.1 | 1 551.3 | 1 558.2 |
| battle_ms | 159 | 40.0 | 550.5 | 612.5 | 705.7 |
| total_ms | 159 | 20 739.4 | 23 386.5 | 23 425.6 | 23 441.9 |

## 2. Successful vs QueueTimeout (the slice you asked for)

### 2a. Successful — Won + Lost + Draw (134 iterations)

| phase | p50 ms | p95 ms | p99 ms | max ms |
|---|---:|---:|---:|---:|
| auth_ms | 0.0 | 38.6 | 41.8 | 42.9 |
| onboard_ms | 3.3 | 4.7 | 5.1 | 10.6 |
| connect_ms | 3.8 | 8.6 | 12.6 | 35.9 |
| **queue_wait_ms** | **19 806.8** | **22 281.9** | **22 312.4** | **22 325.5** |
| join_battle_ms | 779.6 | 1 544.1 | 1 551.3 | 1 558.2 |
| battle_ms | 44.6 | 555.0 | 612.5 | 705.7 |
| total_ms | 21 100.4 | 23 396.5 | 23 425.6 | 23 441.9 |

### 2b. QueueTimeout (23 iterations)

| phase | p50 ms | p95 ms | p99 ms | max ms |
|---|---:|---:|---:|---:|
| auth_ms | 0.0 | 0.0 | 0.0 | 0.0 |
| onboard_ms | 3.4 | 3.8 | 3.8 | 4.5 |
| connect_ms | 3.8 | 4.3 | 4.7 | 5.3 |
| **queue_wait_ms** | **9 700.3** | **18 575.0** | **18 575.7** | **20 385.9** |
| join_battle_ms | 0.0 | 0.0 | 0.0 | 0.0 |
| battle_ms | 0.0 | 0.0 | 0.0 | 0.0 |
| total_ms | 9 707.6 | 18 583.7 | 18 583.8 | 20 394.5 |

**Reading this slice:**

- For bots that succeeded, **queue wait alone is 19.8 s p50 / 22.3 s p95** — that is essentially all of the 21 s iteration time.
- For bots that "timed out", they actually **never hit the 60 s queue cap** (max queue_wait = 20.4 s). They were cancelled by NBomber's `ScenarioCancellationToken` when the 120 s test window closed. In other words, the "QueueTimeout" outcomes are mostly bots that joined the queue too late to finish — they would have been paired eventually given more wall clock. We should rename the outcome to `Cancelled` or distinguish between "hit 60 s queue cap" vs "test ended" in a follow-up; the current label is misleading.

## 3. Sanity check — does our breakdown explain NBomber's iteration latency?

NBomber's `ok stats` p50 (across all 134 successful iterations) = **21 118.98 ms**.
Our recorder's `total_ms` p50 for successful iterations = **21 100.4 ms**.

**Δ = 18 ms (0.09 %).** ✅

Sum of per-phase p50 (successful, e.g. for "Won"): 0 + 3.3 + 3.8 + 19 776.7 + 779.9 + 41.6 = **20 605.3 ms**.
Won `total_ms` p50: **20 855.3 ms**.
**Δ = 250 ms.** That's the post-iteration `LeaveQueueAsync` in `VirtualPlayer.cs:189-191`, which the recorder doesn't track as its own phase. Negligible for this iteration, but worth a `cleanup_ms` field if we ever want full closure.

So the breakdown accounts for the full iteration time.

## 4. Top-1 contributor and hypothesis

### `queue_wait_ms` — **94.6 % of the median successful iteration** (19 807 ms / 21 100 ms)

Everything else combined adds up to 1 293 ms. Queue wait is not just dominant, it dwarfs everything else by a factor of ~15.

**Hypothesis (one paragraph):** The bots are not waiting on a slow HTTP call — they are waiting on the matchmaking *pairing decision itself*. Server-side HTTP confirms this: `api/v1/queue/status` p95 = **9 ms** and `api/v1/queue/heartbeat` p95 = **7 ms** (Prometheus, `http_server_request_duration_seconds_bucket`, 5 min window covering the run). Every poll is fast; the bot is just told "Searching" until matchmaking finally returns "Matched". With 25 pairs running in `KeepConstant`, there are 50 bots permanently queued; whenever a battle ends, one bot rejoins, and matchmaking has to pair it with whoever else is available. The dominant factor is the *pairing throughput* of the matchmaking service — almost certainly a periodic worker tick (`MatchmakingPairingWorker` or whatever the equivalent is in `src/Kombats.Matchmaking`), not a per-request synchronous pair-on-join. The 19.8 s median wait is the steady-state queue depth divided by the pairing rate.

### Honorable mention: `join_battle_ms`

p50 = **779 ms**, p95 = **1 544 ms**. This is the BFF hub `JoinBattle` invocation **with the 8-step retry loop** at `SignalR/BattleHubClient.cs:97`. The fact that the *median* iteration needs 779 ms means most bots take at least one retry on first call — there's a race between the matchmaking-confirmed `battleId` reaching the bot and the battle being writable in Redis. The handoff calls this out as known issue F4 ("JoinBattle 'Battle not found' race"). At p95 = 1.5 s we're talking ~3-4 retries. This is a real, fixable bug — but it's an order of magnitude smaller than queue_wait, so it's the *second* lever, not the first.

## 5. Proposed next diagnostic step (not implemented)

**Pin down the matchmaking pairing-tick interval and queue-depth dynamics.**

Concretely, do exactly one of these — they're each ~30 minutes of work:

**Option A — Code spelunking (cheapest, do this first):**
Read the matchmaking pairing worker source (look in `src/Kombats.Matchmaking/` for a `HostedService` or `BackgroundService` that runs a loop on a timer). Look for: the tick interval, the batch size per tick, and any "minimum wait time" or "fairness window" before two bots can be paired. Report these as numbers in a short note. This will likely **explain the 19.8 s wait directly** — e.g. if the pairing worker ticks every 1 s and pairs one couple per tick, the steady-state wait for 50 queued bots is ~25 s, which matches what we see.

**Option B — Add server-side metric (slightly more work):**
The MVP almost certainly doesn't have a metric named `matchmaking_pairing_latency_seconds` (time from "QueueJoined" event → "Matched" event server-side). Adding it to `src/Kombats.Matchmaking` would let us confirm from the *server's* perspective that the wait is in pairing, not anywhere else. But this changes server code, which the user said to avoid for now.

**Option C — Profile matchmaking pod CPU during a run:**
If the pairing worker is CPU-bound (e.g. doing an O(n²) scan over the queue every tick), more concurrency would make it worse. Run a 2× larger test (won't fit under NBomber Community's 25-cap, would need our own runner) or just `dotnet-counters monitor` against the matchmaking process during a normal run.

**Recommendation:** **Option A.** It is the cheapest, doesn't touch server code, and is highly likely to be a "30 lines of source code explain everything" outcome. If the tick interval is small and the wait still 19.8 s, *that's* the surprise that warrants Option B or C.

## 6. Things I did not do (worth flagging)

- Did **not** change `QueueTimeoutSeconds`. Reported above as 60 s for interpretation context.
- Did **not** touch `src/Kombats.*`.
- Did **not** commit. Three files are new / modified, all under `tests/Kombats.LoadTests/`:
  - `Reporting/IterationRecorder.cs` (new)
  - `Scenarios/ConcurrentBattlesScenario.cs` (modified — recorder wired in, ~10 lines)
  - `scripts/aggregate-phases.sh` (new)
  - `iteration-logs/iterations-2026-05-11--17-48-23.jsonl` (new, run artifact — should probably be gitignored alongside `reports/`)
  - `PHASE_BREAKDOWN_PLAN.md`, `PHASE_BREAKDOWN_REPORT.md` (this file, planning artifacts)

Awaiting your call on commit + branch strategy.
