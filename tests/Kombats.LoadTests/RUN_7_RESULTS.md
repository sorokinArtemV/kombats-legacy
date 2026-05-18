# Run 7 — Capacity Scan Results (Chapter 5 Phase C)

## Headline

The Chapter 5 fix **lifts the matchmaking pairing-rate ceiling**. Chapter 4's
~9.5 pairs/s (~19 RPS) cap — a fixed 100 ms `Task.Delay` plus one-pair-per-tick
handler — is gone. At 50 simul the changed code sustains **~58 RPS (~28.9
pairs/s)** with `total_ms p95 = 931 ms` and `queue_wait p95 = 612 ms`, all on a
single Matchmaking + single Battle replica — a **3× throughput lift** and a
**76 % queue-wait reduction** versus Run 6 at the same load.

The new wall is **matchmaking single-core CPU saturation**: the pairing worker,
which slept ~93 % of wall time under the old code, now pegs one vCPU flat at
~103 % across the full Stage 2 hold. The worker no longer waits on a timer — it
waits on CPU.

**Scan is partial — 2 valid stages of 3.** Stage 3 (100 simul) was attempted
three times and is invalid every time: the Locust load generator itself
saturated (single-threaded gevent process pinned at 100 % of one macOS core),
so its throughput numbers reflect load-generator starvation, not backend
behaviour. The true ceiling is therefore **≥ 58 RPS — a lower bound, not a
measured ceiling.** This Mac cannot honestly drive past ~50 simul.

## Scan trend — 25 / 50 simul (Stage 3 invalid, excluded)

Single Battle + single Matchmaking replica, 5-min hold, cold start (`down -v` +
reseed) per stage. `MaxPairsPerTick = 64` throughout.

| Metric                | S1 — 25 (8 924) | S2 — 50 (18 283) | S1→S2   |
|-----------------------|-----------------|------------------|---------|
| `total_ms` p50        | 625             | 778              | +24 %   |
| `total_ms` p95        | 717             | 931              | +30 %   |
| `total_ms` p99        | 856             | 1 024            | +20 %   |
| `queue_wait_ms` p50   | 516             | 544              | +5 %    |
| `queue_wait_ms` p95   | 545             | 612              | +12 %   |
| `battle_ms` p50 / p95 | 47 / 157        | 119 / 263        | +153 % / +67 % |
| Throughput (iter/s)   | ~27–30          | **57.8** (hold)  | ~2×     |
| REAL error rate       | 0 %             | 0 %              | flat    |
| matchmaking CPU       | ~47–55 %        | **101–104 % flat** | pegged |
| Postgres conns        | 44 / 300        | 66 / 300         | +50 %   |

Unlike Run 6 — where 2× users meant 2× queue_wait at a pinned ~19 RPS — here
throughput roughly **doubles** from S1 to S2 while latency stays tight. The
fixed-rate cap is gone; the system now scales with load until a real resource
binds. Stage 1 (25 simul) is the cleanest single point — the only stage where
the conditional idle backoff still engages, since at 50 simul the queue rarely
empties.

## Before / after — Run 6 vs Run 7 (50 simul)

| Metric                | Run 6 (before) | Run 7 (after) | Change   |
|-----------------------|----------------|---------------|----------|
| Throughput            | 18.8 RPS       | 57.8 RPS      | **+208 %** |
| `total_ms` p95        | 2 671 ms       | 931 ms        | −65 %    |
| `queue_wait_ms` p95   | 2 566 ms       | 612 ms        | −76 %    |
| `battle_ms` p95       | 219 ms         | 263 ms        | +20 %    |
| matchmaking CPU       | ~40–49 % flat  | ~103 % flat   | worker no longer sleeps |
| REAL error rate       | 0 %            | 0 %           | flat     |

The matchmaking CPU shift is the cleanest single piece of evidence the fix
engaged. Run 6's flat ~40–49 % was the signature of a worker sleeping ~93 % of
wall time behind an unconditional 100 ms `Task.Delay`. Run 7's flat ~103 % is a
worker that no longer sleeps — the bounded inner loop pairs back-to-back, and
the conditional idle backoff only fires on an empty queue. `battle_ms` rising
slightly (+20 %) is expected: at 3× throughput Battle does proportionally more
work per wall-clock second, but stays far from any limit.

## Predicted vs measured

Prediction recorded before the scan (`CHAPTER_5_PLAN.md` §6): arithmetic
ceiling ~140 pairs/s if nothing else bound; honest range **30–140 pairs/s**,
limited by contention; new wall either the 5 sequential awaits per pair or
SERIALIZABLE conflicts on `player_combat_profiles`.

| Quantity              | Predicted        | Measured (S2)     | Note |
|-----------------------|------------------|-------------------|------|
| New ceiling           | 30–140 pairs/s   | ≥ 28.9 pairs/s    | lower bound only — see below |
| New wall              | awaits OR 40001  | matchmaking CPU   | a third cause, not predicted |
| Old cap removed       | yes              | yes (queue_wait −76 %) | confirmed |

Measured 28.9 pairs/s lands at the **bottom of the predicted range** — but it
is a floor, not the ceiling: Stage 2 did not break, so the true ceiling is
higher and was not reached. The prediction's two named wall candidates were
both wrong as the *first* wall — see below.

## The new wall — matchmaking single-core CPU

At 50 simul the `kombats-matchmaking` container holds **101.79–103.94 % CPU**
flat across all three mid-hold snapshots — one vCPU fully pegged (limit 1.5
vCPU). This is the new pairing-rate ceiling signature, replacing Run 6's flat
~19 RPS. The pairing loop is no longer paced by `Task.Delay`; it is paced by
how fast a single core can run `ExecuteMatchmakingTickHandler` — the 5
sequential Redis/Postgres awaits per pair (`ExecuteMatchmakingTickHandler.cs`
lines 52, 65, 70, 107, 113, 119, 123), now run flat-out on one thread.

This is **not** host saturation: Mac swap was zero, total Docker CPU peaked at
~509 / 1200 %, pages-free rose during the run. One container on one core is a
code / single-threading limit, not "the Mac ran out."

**Correction to recon.** `CHAPTER_5_RECON.md` / `RUN_6_RESULTS.md` §"Secondary
walls" named two likely next walls: 5 sequential awaits per pair, and
SERIALIZABLE `40001` conflicts on `player_combat_profiles`. The `40001` count
does climb with load (S1 = 8, S2 = 60 in matchmaking logs) — recon was right
that they reappear in the 30–50 battles/s band — but they are **not** the first
wall: 0 REAL errors, latency flat, so they retry successfully today. The first
wall is the worker's own single-core CPU. The awaits matter, but as the *cost*
that fills that core, not as a separate contention wall. `40001` remains the
likely *second* wall for a later chapter.

## Why Stage 3 is invalid (load-generator saturation)

Stage 3 (100 simul) was run three times — once, then twice more after freeing
host resources. Every attempt: the Locust process pinned **100.0–100.8 % of one
macOS core** for the entire hold, and Locust emitted `CPU usage above 90 %!`
at T+30 s and `CPU usage was too high` at shutdown. Locust is single-threaded
(gevent); 100 % of one core is its hard ceiling.

Measured Stage 3 throughput (~51–52 RPS) is **below** Stage 2's 57.8 — a
"throughput regression" by the break criteria. But backend containers sat at
36–62 % of their limits, latency held ~1 050 ms p95, errors 0 %. The regression
is the load generator, not the backend. Stage 3 cannot establish a ceiling and
is excluded from all conclusions.

A distributed-Locust re-run (`master + N workers`) was scoped but not executed:
a read-only recon found the harness is not multi-process-safe (see
"Next-chapter input"). Fixing it is tool work, out of scope for a measure-only
phase.

Consequence: the matchmaking-CPU wall is measured cleanly **only at 50 simul**.
At 100 simul the container ran ~90–93 % (not pegged) — but that figure is taken
under a starved load generator and proves nothing. Run 7 establishes the wall
exists at 50; it does not measure how far it extends.

## Measured capacity

- **Sustained clean (single replica):** 50 simul (~25 concurrent battles'
  worth), 57.8 RPS, `total_ms p95 = 931 ms`, `queue_wait p95 = 612 ms`, 0 real
  errors, zero swap.
- **Throughput ceiling:** **≥ 58 RPS — a lower bound.** Stage 2 did not break;
  the true ceiling is higher and was not reached on this hardware.
- **New wall:** matchmaking single-core CPU saturation (~103 % of one vCPU at
  50 simul). The worker is now CPU-bound, not timer-bound.
- **Measurement ceiling:** this single-host Mac cannot honestly drive past
  ~50 simul — the Locust load generator binds one core first.

## Methodology

- **Stages planned:** 3 (25 / 50 / 100 simul); 2 valid, Stage 3 invalid ×3.
- **Per-stage:** 5-min sustained hold + 30 s ramp; cold start (`down -v` +
  reseed-50 + capacity compose chain) every stage. Single Battle + single
  Matchmaking replica, no replica overlay.
- **Code under test:** branch `feat/chapter-5-pairing-ceiling` @ `decca84`
  (Phase B fix; `decca84` = `770a709` + Chapter 5 docs only — no code delta).
  The handoff cited `770a709`; `decca84` is the actual measured tip.
- **Break criteria (stop on first trip):** `total_ms p95 > 4800 ms`; REAL error
  rate > 5 %; throughput regression; whole-host saturation. A single container
  at ~100 % of one vCPU is recorded as a measurement, not a stop — this is the
  expected new-wall signature, not a host limit.
- **REAL-error definition:** `battle_ms > 0` AND `queue_wait > 6 ms`.
  Shape-shutdown rows (`battle_ms = 0`, `queue_wait ≈ 2–17 ms`, `total ≈
  90 000 ms`) excluded — 16 at S1, 1 at S2 — matching the documented signature.
- **`MaxPairsPerTick`:** held at default 64 for the whole of Run 7 per the
  handoff. Note: `CHAPTER_5_PLAN.md` §4/§5 had allowed tuning it between stages
  if it became the visible cap; the handoff overrode this for a clean
  before/after comparison. Recorded here as a deliberate, conscious deviation
  from the plan — it did not become the visible cap, so nothing was lost.
- **Stage 2 re-measured:** the first Stage 2 attempt had a host-clock pause
  that fired its infra snapshots post-run (idle stack); it was discarded and
  Stage 2 cold-started and re-run. All Stage 2 numbers are from the valid
  second run (18 283 rows, `iterations-2026-05-18--12-54-02.jsonl`).
- **Platform:** Mac (Apple Silicon) — same as the Run 6 baseline, so the
  before/after comparison is same-platform and valid.

## Things this run did NOT do

- **Measure the true ceiling.** Stage 2 did not break; the real ceiling is
  above 58 RPS and was not reached. Run 7 gives a lower bound only.
- **Validate behaviour at 100 simul.** All three Stage 3 attempts are invalid
  (load-generator starvation). Blocked by the load generator, not by code.
- **Resolve the matchmaking-CPU wall above 50 simul.** Cleanly measured at 50;
  unmeasured beyond it.
- **Fix any secondary wall.** The `40001` SERIALIZABLE conflicts and the 5
  sequential awaits per pair are named, not fixed — Chapter 6+.
- **Build or change measurement tooling.** A distributed-Locust re-run was
  scoped and a recon was done, but the harness changes were not made — that is
  tool-building, out of scope for a measure-only phase.
- **Touch replicas, the Lua script, or the lease service.** Single-replica
  throughout; correctness machinery untouched.

## Next-chapter input

- **Matchmaking horizontal scaling / multi-core.** The worker is now
  single-core-bound. The single Redis lease (`mm:lease:matchmaking:default`) is
  correctness machinery (recon Q1–Q3) — letting the worker use more than one
  core means reworking the lease scheme without breaking pair atomicity. Sized,
  scoped, Chapter 6.
- **Distributed load generator — prerequisite for measuring above ~50 simul.**
  Single-process Locust binds one macOS core. A recon of the harness for
  `master + N workers` mode returned **RED** — four concrete gaps, all to fix
  before a distributed run is valid:
  1. JSONL path is a per-second timestamp, not per-process, and opened with
     truncate — workers clobber each other
     (`iteration_recorder.py:48,87-90`).
  2. No master/worker role branching — master opens fds / user pool / tokens it
     should not (`locustfile.py:98-123`).
  3. `UserPool` is replicated per worker with no partitioning — duplicate bot
     identities across workers, non-representative load
     (`locustfile.py:57-88`).
  4. `aggregate-phases.sh` consumes exactly one file — cannot fan in N worker
     outputs (`scripts/aggregate-phases.sh:16-24,83`).
  This is preflight work for the next capacity chapter, not an optional extra.
- **`40001` SERIALIZABLE conflicts** on `player_combat_profiles` — climbing
  with load (8 → 60 across S1 → S2), retrying cleanly today, the likely
  *second* wall once the single-core wall is lifted.
