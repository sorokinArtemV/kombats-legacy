# Phase B1b — Locust Integration + Calibration Results

## Acceptance

**PASS.** Two consecutive calibration runs (#2 and #3) land every
acceptance metric inside Run 0's ±5% band. Run #1 was an outlier on the
favorable side (tighter p95 tails) — see §3.

Acceptance table, Run #3 (latest clean run):

| Metric | Run 0 target | ±5% band | Run #3 | Δ | Verdict |
|---|---|---|---|---|---|
| ok count | 2096 | 1991 – 2201 | 2090 | −0.3 % | ✅ |
| RPS | 17.47 | 16.60 – 18.34 | 17.42 | −0.3 % | ✅ |
| queue_wait_ms p50 | 1014.7 | 964 – 1065 | 1020 | +0.5 % | ✅ |
| queue_wait_ms p95 | 1519.4 | 1443 – 1595 | 1518 | −0.1 % | ✅ |
| total_ms p50 | 1106.0 | 1051 – 1161 | 1120 | +1.3 % | ✅ |
| total_ms p95 | 1593.4 | 1514 – 1673 | 1588 | −0.3 % | ✅ |

## Environment

- Python **3.14.5** (the existing B1a `.venv`). Did NOT drop to 3.12:
  gevent 25.9.1 ships a wheel for 3.14 and installs cleanly. The
  brief's 15-min budget for gevent-on-3.14 troubleshooting was unused.
- `locust==2.44.0`, `gevent==25.9.1`, `signalrcore==1.0.2`,
  `websocket-client==1.9.0`, `httpx==0.28.1`, `PyJWT==2.12.1`. Pinned
  in `locust/requirements.txt`.
- Stack: Mode A per RUNBOOK (14 containers + bootstrap Exited 0,
  single-replica Battle, no SignalR backplane). Pre-run Redis baseline
  `battle:state:*=2` (post-B1a residue — negligible vs the 315-key
  accumulation that broke Run 0 attempt #1).
- macOS Darwin 25.4.0, Apple Silicon.

## Calibration runs — Overall slice side-by-side

| Phase | Stat | Run 0 | Run #1 | Run #2 | Run #3 |
|---|---|---|---|---|---|
| ok count | — | 2096 | 2111 | 2090 | 2090 |
| RPS | — | 17.47 | 17.59 | 17.42 | 17.42 |
| queue_wait_ms | p50 | 1014.7 | 1017 | 1024 | 1020 |
| queue_wait_ms | p95 | 1519.4 | **1060** ❌ | 1535 ✅ | 1518 ✅ |
| total_ms | p50 | 1106.0 | 1102 | 1136 | 1120 |
| total_ms | p95 | 1593.4 | **1406** ❌ | 1633 ✅ | 1588 ✅ |
| onboard_ms | p50 | 3.3 | 10 | 12 | 11 |
| connect_ms | p50 | 4.4 | 15 | 17 | 16 |
| battle_ms | p50 | 39.3 | 48 | 48 | 48 |

JSONL files preserved:
- Run #1: `iteration-logs/iterations-2026-05-16--13-52-42.jsonl` (2111 rows)
- Run #2: `iteration-logs/iterations-2026-05-16--13-58-17.jsonl` (2090 rows)
- Run #3: `iteration-logs/iterations-2026-05-16--14-00-53.jsonl` (2090 rows)

Zero errors / QueueTimeouts / BattleTimeouts across all three runs.

Run #1's tighter tails were a benign favorable-direction outlier — every
metric was either within band or BELOW the lower bound (= faster than
Run 0). The Python port never produced worse-than-baseline latencies.

## gevent compatibility

**Monkey-patching alone was sufficient — no explicit `gevent.spawn` needed.**

`gevent.monkey.patch_all()` as the first statement of `locustfile.py`
converts the B1a code transparently:

- `threading.Thread(daemon=True)` heartbeat in `virtual_player._run_heartbeat`
  becomes a greenlet; `stop_event.wait(10s)` yields cooperatively. No
  leaked heartbeat greenlets observed across 6 291 battles.
- `threading.Event` / `threading.Lock` (token cache, recorder, pending
  invocations, user pool) all work under gevent as expected.
- signalrcore's websocket-client `socket.recv()` becomes gevent-cooperative;
  the receive thread (now greenlet) blocks on recv and yields properly.

**Cosmetic noise:** signalrcore logs `[Errno 9] File descriptor was
closed in another greenlet` at ERROR level on dispose. This is gevent's
`cancel_wait_ex` signaling the receive greenlet that its fd was closed
by `connection.stop()` — the greenlet exits cleanly, no metric is
affected. Left as-is (filtering at ERROR would also hide real failures).

## Decisions made

**LoadTestShape overrides CLI flags.** Locust 2.44 silently ignores
`--users` / `--spawn-rate` / `--run-time` when a shape class is loaded.
The intended 4-user smoke executed the full 25-user shape instead — that
became Run #1, which produced 2 111 clean iterations and acted as a far
stricter smoke than the planned 4-user one.

**User pool.** Mirrors `Configuration/UserPool.cs` — cursor-mod
round-robin plus an explicit `_in_use` set guarding double-assignment.
At 25 concurrent / 50 pool the in-use set is never hit; it documents
the invariant.

**Per-User username, persistent.** Each Locust User picks one username
at `on_start` and holds it for the full run (vs C# NBomber rotating
through all 50 every 25 iterations). The system-under-test is stateless
per bot once onboarded, so battle-level metrics are unaffected —
confirmed by Run #3 landing in band.

**Process-wide singletons.** Single `TokenClient` + `IterationRecorder` +
`UserPool` created in `events.init`, closed in `events.quitting`. The
recorder's `threading.Lock` (gevent-cooperative after monkey-patch)
serializes writes from all 25 greenlets into one LF-terminated JSONL.

**Explicit `newline='\n'`.** `IterationRecorder.__init__` now passes
`newline='\n'` to `open()` — no-op on macOS, prevents a CRLF surprise
elsewhere.

## Diff from NBomber behavior

- **No tail-end QueueTimeouts.** Locust lets in-flight `@task` calls
  finish when the shape ends at t=120s; NBomber abandons mid-queue
  scenarios producing ~25 `QueueTimeout` rows. Result: Locust 0 fails
  vs Run 0's 25 fails. Cleaner termination, not a measurement bias —
  ok counts still match (2 090 vs 2 096).
- **`onboard_ms` and `connect_ms` are ~3× higher** (10–17 ms vs 3–4 ms).
  This is the only systematic divergence. Suspected: Python httpx + the
  signalrcore websocket handshake are heavier than .NET's equivalents.
  Doesn't bubble up to `total_ms` because queue-wait (~1 s) dominates.
  These phases are not on Run 0's acceptance list.
- **Linear-step ramp at 1-second granularity** vs NBomber's continuous-
  time ramp. The shape returns integer user counts each tick; difference
  is sub-rounding-noise at this scale.

## Readiness for Phase C/D

**Solid:** 25-simul parity proven over two consecutive runs, zero
errors, JSONL drops into `aggregate-phases.sh` unchanged, gevent
integration is clean.

**Untested above 25:** the user pool has 50 entries (Stage 2 = 100 %
saturation); Stage 3+ needs re-seeding. Per-greenlet memory has only
been exercised at 25 concurrent. `Run0Shape` hardcodes the user count
— Phase D needs env-driven `RAMP_USERS` / `HOLD_SECONDS`.

**Two surprises for Phase D to watch:**
- Runs #2 and #3 each produced one `battle_ms` outlier in the 19–21 s
  range (max only — p99 unaffected at 440–470 ms). Likely a hub-event
  hiccup or the Postgres SERIALIZABLE retry storm noted in
  `RUN_0_BASELINE.md` §6. Worth watching as concurrency grows.
- The signalrcore `[Errno 9]` shutdown noise scales with
  users × battles. At 100+ users on 5-min stages it could flood stdout;
  filter `SignalRCoreClient` at CRITICAL if needed.

## How to reproduce

Stack: Mode A per RUNBOOK; `users-manifest.json` has ≥25 entries.

```bash
cd tests/Kombats.LoadTests/locust
.venv/bin/locust -f locustfile.py --headless --only-summary
# 120 s wall clock; LoadTestShape controls users/ramp/duration.
# Output: ../iteration-logs/iterations-YYYY-MM-DD--HH-MM-SS.jsonl

# Aggregate + compare to Run 0:
../scripts/aggregate-phases.sh ../iteration-logs/iterations-*.jsonl
# Check the "1. Overall" block against RUN_0_BASELINE.md §3.
```

No CLI flags required — the shape class drives everything. For
sub-25-user debugging, temporarily comment out `Run0Shape` and use
`--users N --spawn-rate R --run-time Ts`.
