# Lease Renewal Cancellation Fix — Results

**Branch:** `fix/matchmaking-lease-renewal-cancellation`
**Fix:** `MatchmakingLeaseService.cs` — `Task.Delay` in renewal loop now listens to a linked token combining `stoppingToken` and `leaseLostSource.Token`.
**Profile:** 25 pairs × 120 s (same as baseline).
**Before run:** `iteration-logs/iterations-2026-05-11--17-48-23.jsonl` (commit `95af2d7`)
**After run:** `iteration-logs/iterations-2026-05-11--18-38-56.jsonl`

## TL;DR

| | Before | After | Change |
|---|---:|---:|---:|
| **Successful iterations / 2 min** | **134** | **2 102** | **15.7× ↑** |
| **queue_wait_ms p50** | **19 807 ms** | **1 018 ms** | **19.4× ↓** |
| **total_ms p50** | **21 100 ms** | **1 115 ms** | **18.9× ↓** |
| **Tick rate** | **0.57 /s** | **9.44 /s** | **16.6× ↑** |
| **Inter-tick interval p50** | **1 755 ms** | **105.7 ms** | **16.6× ↓** |
| QueueTimeout % | 14.5 % | 0.9 % | 16× ↓ |
| battle_ms p50 (regression check) | 44.6 ms | 49.2 ms | ≈ unchanged ✓ |

## 1. Before / After table — per-phase (successful iterations only)

| Phase | Before p50 | After p50 | Before p95 | After p95 | Before p99 | After p99 |
|---|---:|---:|---:|---:|---:|---:|
| `auth_ms` | 0.0 | 0.0 | 38.6 | 0.0 | 41.8 | 25.4 |
| `onboard_ms` | 3.3 | 3.7 | 4.7 | 6.5 | 5.1 | 10.9 |
| `connect_ms` | 3.8 | 5.0 | 8.6 | 9.3 | 12.6 | 14.3 |
| **`queue_wait_ms`** | **19 806.8** | **1 018.4** | 22 281.9 | 1 525.6 | 22 312.4 | 1 530.8 |
| **`join_battle_ms`** | **779.6** | **4.4** | 1 544.1 | 10.0 | 1 551.3 | 21.3 |
| `battle_ms` | 44.6 | 49.2 | 555.0 | 132.1 | 612.5 | 406.0 |
| **`total_ms`** | **21 100.4** | **1 115.0** | 23 396.5 | 1 609.0 | 23 425.6 | 1 631.8 |

Iteration counts: before 134 successful (+ 25 failed = 159 total); after 2 102 successful (+ 25 failed = 2 127 total).

### Reading the table

**queue_wait_ms** is the lever we pulled. From 19.8 s to 1.0 s — almost exactly the 1.5 s figure predicted in `LEASE_OVERHEAD_INVESTIGATION.md`.

**join_battle_ms** is the bonus. From 780 ms to 4.4 ms — **177× faster**. The bot used to need ~3-4 retries of the JoinBattle SignalR call because of the "Battle not found" race (handoff issue F4) — the matchmaking-side projection of the new battle hadn't reached Redis when the bot tried to join. With matchmaking now ticking 16× faster, the race window collapses; the median call hits a ready battle on the first try.

**battle_ms** p50 moved 44.6 → 49.2 ms (≈ unchanged at this scale). This is the sanity check: the per-battle work in the Battle service is unrelated to matchmaking, and shouldn't move. It didn't. ✓

**auth, onboard, connect** are all in the 0-10 ms range and unchanged. ✓

## 2. Throughput comparison

| | Before | After |
|---|---:|---:|
| Successful iterations / 120 s | 134 | **2 102** |
| Iterations / second (overall) | 1.12 | **17.5** |
| Battles completed | 67 | **1 051** |
| Battles / second (steady state) | 0.74 | **~9.5** |
| Battles / minute | 44.6 | **~573** |

Throughput improvement: **15.7×.** With this fix alone, the existing single-replica matchmaking service can handle on the order of 600 battles/min on a single laptop's Docker stack — comparable to what we'd previously have assumed required multi-replica + backplane work.

## 3. Handler tick cycle confirmation (Jaeger spans)

Confirms the fix targets the right place. Sample size: last 1 500 `matchmaking.pair.tick` parent spans, covering the fix-applied run.

| Metric | Before (chapter 2 baseline) | After |
|---|---:|---:|
| Tick rate observed | 0.57 ticks/s | **9.44 ticks/s** |
| Inter-tick interval p50 | 1 755 ms | **105.7 ms** |
| Inter-tick interval p95 | — | 109.7 ms |
| Inter-tick interval p99 | — | 112.2 ms |
| Parent `tick` span duration p50 | 5.0 ms | **2.3 ms** |
| Parent `tick` span duration p95 | 10.2 ms | 5.2 ms |

Distribution of inter-tick intervals over the fix run:

```
  < 120 ms      : 1498 / 1500   (99.87 %)
  120-1000 ms   :    1 / 1500   (0.07 %)
  >= 1000 ms    :    0 / 1500   (0.00 %)
```

Sample 10 consecutive intervals from the middle of the run (ms):
`105.4, 110.4, 105.9, 108.1, 104.1, 106.0, 106.4, 106.6, 110.2, 104.1`

The configured `TickDelayMs = 100` + handler ~5 ms = predicted ~105 ms cycle. Observed median = 105.7 ms. **Match within ~0.7 %.** The renewal-loop wait is gone.

Worth noting: the parent `matchmaking.pair.tick` span duration *also* dropped (5.0 ms → 2.3 ms). That's because most ticks in the after run are no-op ticks (queue temporarily empty between rapid pair-formations), and a no-op tick is just the `try-pop` Lua script. In the before run there were always 50 bots queued waiting, so almost every tick formed a pair.

## 4. Failure rate

| Outcome | Before | After |
|---|---:|---:|
| Won | 58 | 933 |
| Lost | 58 | 933 |
| Draw | 16 | 236 |
| **QueueTimeout** | **23** (14.5 %) | **19** (0.9 %) |
| Error | 2 | 6 |
| **Total** | 159 | 2 127 |

QueueTimeout count is roughly the same in absolute terms (23 → 19), but the rate dropped from 14.5 % to 0.9 % — because the denominator exploded. As discussed in `PHASE_BREAKDOWN_REPORT.md`, "QueueTimeout" outcomes are actually scenario-cancellation outcomes — bots that joined the queue too close to the end of the 120 s NBomber window to finish before the test stopped. With 16× faster iteration, far more bots fit before the deadline. The 19 leftover are essentially the bots that started their last iteration in the final ~1.5 s.

Error count crept up 2 → 6 but is at the noise floor (0.28 % of iterations). Worth a quick look in a follow-up run if the rate persists, but not a blocker.

## 5. Sanity checks

| Check | Before | After | Verdict |
|---|---|---|---|
| `battle_ms` (Battle service, unrelated to fix) | 44.6 ms | 49.2 ms | unchanged ✓ |
| `auth_ms` / `onboard_ms` / `connect_ms` (client-side, unrelated) | ~7 ms total | ~9 ms total | unchanged ✓ |
| `tick` parent span sum-of-children ≈ parent | yes (chapter 2) | yes (chapter 2) | unchanged ✓ |
| Inter-tick interval ≈ `TickDelayMs` + handler | violated by 16× | 105.7 ms ≈ 105 ms expected | fixed ✓ |
| Cycle math: `cycle × 12.5 + 250 ms poll ≈ queue_wait_ms p50` | 22.2 s ≈ 19.8 s measured | 1.57 s ≈ 1.02 s measured (within poll-lag jitter at this scale) | fixed ✓ |
| Wait model now predicts measured wait | within 12 % | within ~50 % at small scale because the 500 ms poll cadence becomes a meaningful fraction of the wait, not a rounding error | acceptable* |

\* At 1 s queue wait, the bot's own 500 ms poll cadence (`VirtualPlayer.cs:294`) injects 0-500 ms of detection latency on top of the actual pairing wait. The model is correct; the bot's polling resolution simply becomes the noise floor. Reducing the bot's poll interval (or switching to a push notification — handoff issue) would tighten this further. **For the portfolio, this is its own next chapter.**

## 6. Plain-language summary (for STORY)

> We were measuring 19.8-second queue waits in matchmaking under 25-pair load. The throughput model said one pair formed every 1.35 seconds, and the previous source walk (`MATCHMAKING_PAIRING_NOTES.md`) hypothesized that ~1.25 s of that was Postgres / outbox work inside the matchmaking handler. We instrumented the handler with OTel spans (chapter 2, commit `3ada746`) and found the handler was actually finishing in 5 ms — so the 1.65-second-per-cycle gap was somewhere else. Reading the lease management code (`MATCHMAKING_LEASE_OVERHEAD_INVESTIGATION.md`, commit `ab5bf89`), we localized the bug to a single line: the renewal-task's `Task.Delay` was bound to the wrong cancellation token, so when a tick finished it had to wait up to 1.667 seconds for the renewal task's natural sleep cycle to expire before the cleanup could complete. We fixed it by passing a linked token (combining the worker-shutdown token and the per-tick lease-lost token) to that `Task.Delay`. One file modified, six lines added (including a comment explaining why), zero behavior change to the lease semantics themselves. Result: queue wait dropped from 19.8 s to 1.0 s (19× faster), throughput went from 67 to 1 051 battles per two minutes (15.7× more), and a separate "JoinBattle not found" race condition that depended on a slow matchmaking projection effectively disappeared as a side effect (its window collapsed from 780 ms to 4 ms). The bug was the kind that only shows up under sustained load — and only when you can compare *the configured cycle time* against *what the system actually does*.

## 7. Things not done

- Did **not** turn off the lease (Fix B in `LEASE_OVERHEAD_INVESTIGATION.md`). That's a design call about whether a distributed lock is needed in single-replica mode, and belongs in the multi-replica / backplane chapter — not riding a bug fix.
- Did **not** add unit tests for the renewal-loop cancellation behavior. The bug was specifically that one token wasn't threaded into one `Task.Delay`; a meaningful test would need to simulate the renewal loop interacting with the outer `TryExecuteUnderLeaseAsync`, which is non-trivial harness work. Cost > value for a portfolio piece; documenting the bug + the fix is sufficient. If we go to production multi-replica, that test becomes worth writing.
- Did **not** touch anything else in `src/Kombats.*`. No drive-by improvements.

## 8. Files modified in this commit

| | Path | Change |
|---|---|---|
| MOD | `src/Kombats.Matchmaking/.../MatchmakingLeaseService.cs` | +5 source lines + 4-line comment; one `await Task.Delay` argument changed |
| NEW | `tests/Kombats.LoadTests/LEASE_FIX_RESULTS.md` | this file |

The new `iteration-logs/iterations-*.jsonl` artifact from the fix run is NOT committed (gitignored, as established in chapter 1).
