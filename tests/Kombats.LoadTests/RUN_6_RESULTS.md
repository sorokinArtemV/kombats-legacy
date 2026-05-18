# Run 6 — Capacity Scan Results (Chapter 4 Phase D)

## Headline

The single-host stack sustains **~50 concurrent battle-bots cleanly** and
crosses the latency cliff (`total_ms p95 > 4 800 ms`) somewhere between
**50 and 100 simul**. At 100 simul, `total_ms p95 = 5 242 ms`. The
bottleneck is the matchmaking pairing rate: a fixed 100 ms `Task.Delay`
plus one-pair-per-tick handler, gated by a single Redis lease, at
`src/Kombats.Matchmaking/Kombats.Matchmaking.Bootstrap/Workers/MatchmakingPairingWorker.cs:72`.
Not Battle, not BFF, not Postgres, not Redis, not host. Throughput is
**flat at ~19 RPS** (~9.5 battles/s) across all three stages — a code-level
cap, not a capacity cap.

## Scan trend — 25 / 50 / 100 simul

All three stages, single Battle + single Matchmaking replica, 5-min hold,
cold start (`down -v` + reseed) per stage. JSONL counts in parentheses.

| Metric                | S1 — 25 (6 154) | S2 — 50 (6 212) | S3 — 100 (6 274) | S1→S2  | S2→S3  |
|-----------------------|-----------------|-----------------|------------------|--------|--------|
| `total_ms` p50        | 1 085           | 2 158           | 4 752            | +99 %  | +120 % |
| `total_ms` p95        | 1 202           | 2 671           | **5 242**        | +122 % | +96 %  |
| `total_ms` p99        | 1 304           | 2 734           | 5 303            | +110 % | +94 %  |
| `queue_wait_ms` p50   | 1 015           | 2 043           | 4 644            | +101 % | +127 % |
| `queue_wait_ms` p95   | 1 041           | 2 566           | 5 143            | +147 % | +100 % |
| `battle_ms` p50 / p95 | 46 / 399        | 44 / 219        | 40 / 94          | flat / −45 % | flat / −57 % |
| Throughput (iter/s)   | 18.6            | 18.8            | 19.0             | +1.1 % | +1.1 % |
| REAL error rate       | 0 %             | 0 %             | 0 %              | flat   | flat   |
| Postgres conns (mid)  | 36 / 300        | 42 / 300        | 50 / 300         | +17 %  | +19 %  |

Read in one glance: each **2× users → 2× queue_wait**, throughput pinned
at ~19 RPS, `battle_ms` *decreasing* (Battle does less per battle as load
rises — fewer turns under pressure). Classic upstream-bottleneck signature.

## The bottleneck

Locator: `src/Kombats.Matchmaking/Kombats.Matchmaking.Bootstrap/Workers/MatchmakingPairingWorker.cs:72`
— `await Task.Delay(TimeSpan.FromMilliseconds(_options.TickDelayMs), …)`
fires after every tick whether or not a pair was created. Config
`Matchmaking:Worker:TickDelayMs = 100` (`appsettings.json:27`, default
`MatchmakingRedisOptions.cs:40`). The handler pops exactly one pair per
tick at
`src/Kombats.Matchmaking/Kombats.Matchmaking.Application/UseCases/ExecuteMatchmakingTick/ExecuteMatchmakingTickHandler.cs:52`
— no inner loop, no batch parameter.

**Arithmetic model** (recon Q3): per-tick work inside the lease is ~7–8 ms
of sequential Redis + Postgres awaits, then a fixed 100 ms sleep. So

```
rate = 1 / (TickDelayMs + ~7 ms work) pairs/s
     = 1 / 0.107         ≈ 9.35 pairs/s
     × 2 bots per pair   ≈ 18.7 RPS
```

The model predicts a load-independent ceiling. Three stages confirm it:

| Stage | Predicted RPS | Measured RPS | Predicted `total_ms` p95 | Measured |
|-------|---------------|--------------|--------------------------|----------|
| S1 (25 simul)  | ~18.7 | 18.6 | (margin)            | 1 202    |
| S2 (50 simul)  | ~18.7 | 18.8 | doubling rule       | 2 671    |
| S3 (100 simul) | ~18.7 | 19.0 | ~5 200 ms           | **5 242 ms (+0.8 %)** |

Three independent measurements, one model. The 0.8 % error on the
Stage 3 latency prediction is the strongest single piece of evidence in
this chapter.

## Why not the predicted suspects

The plan and `RUN_0_BASELINE.md` §6 expected **BFF queue-polling** as the
likely first ceiling (500 ms cadence in `VirtualPlayer.PollUntilMatchedAsync`,
queue-status p95 ≈ 1.80 s at baseline). The scan disproved that.

Evidence the bottleneck is matchmaking, not the predicted suspects:

- **Battle CPU DECREASED across the scan**: 57.8 % → 55.9 % → **51.2 %**
  as users tripled. Battle is doing *less* work per wall-clock second
  under more load — it cannot be the bottleneck.
- **Matchmaking CPU flat at ~40–49 %** of one core across all three
  stages — the fixed-rate-consumer signature of a worker that sleeps
  ~93 % of wall time (recon Q3).
- **BFF rose 17.7 → 22.5 → 24.8 %** of one core (scales with
  connected-bot count, not battles/s) — well within headroom.
- **Postgres connections 36 → 42 → 50 / 300** — 17 % of cap at the break.
- **Redis ~3 000 ops/s**, queues at depth 0, RabbitMQ idle.
- **Host: zero swap, zero pages out, no container near its CPU limit.**

The break is single-tripped: only the latency criterion. Throughput did
not collapse (it rose marginally), error rate stayed 0 % real failures,
host stayed quiet. The system degrades latency-gracefully under a
code-level pairing-rate cap.

## Why scaling cannot lift this

Per recon Q4:

- The pairing-worker variant is hardcoded: `const string variant = "default"`
  at `MatchmakingPairingWorker.cs:41`.
- Lease key derives only from variant: `mm:lease:matchmaking:default`
  (`RedisLeaseLock.cs:181`), single-writer via `SET NX PX` with 5 s TTL.
- Adding **Battle replicas** does not help — Battle is not the bottleneck
  (its CPU went down across the scan).
- Adding **Matchmaking replicas** does not help — losers of the lease
  race fall through to the same 100 ms `Task.Delay` and retry. Net
  cadence stays one-pair-per-100 ms; leadership just migrates.

Lifting this ceiling requires a code change to the pairing worker or
handler. **Fix is Chapter 5 scope.** One-sentence direction (recon Q4):
either reduce `TickDelayMs` or loop the handler over the queue until
empty inside one lease window — designing it is out of scope here.

## Measured capacity

For the portfolio, the clean numbers:

- **Sustained clean**: 50 concurrent bots / ~25 concurrent battles' worth
  of demand. `total_ms p95 = 2 671 ms`, `queue_wait_ms p95 = 2 566 ms`,
  0 real errors, zero swap, all containers steady.
- **Break**: between 50 and 100 simul. At 100, `total_ms p95 = 5 242 ms`
  — first and only break criterion tripped (latency cliff, +9.2 % over
  threshold).
- **Throughput ceiling**: ~19 RPS (~9.5 battles/s), flat regardless of
  input load.
- **Steady-state split at 100 bots** (Little's Law from Stage 3, per-bot
  round-trip 4 752 ms, per-battle in-flight ≈ 44 ms): **~99 / 100 bots
  in queue, ~1 in battle at any instant**. Bottleneck is naked.
- **The ceiling was always there**: `RUN_0_BASELINE.md` §3 measured
  **9.08 matches/s** in Chapter 3 from matchmaking log timestamps — same
  ~9.5 battles/s ceiling, simply not stressed enough for queue_wait to
  reveal it. Run 6 is the first scan high enough to cross it.

## Why the scan stopped at Stage 3

Methodology is **stop on first break** (plan §3). Stage 3 tripped the
latency cliff cleanly. Under the current code, the arithmetic model
predicts Stages 4–6 would multiply `queue_wait_ms` linearly with input
(200 simul → ~10.4 s, 500 → ~26 s, 1 000 → ~52 s) at the same flat
~19 RPS. The bottleneck is already located to one file and one line; the
arithmetic is confirmed across three measurements. Running Stages 4–6
adds no new information — only more queue depth against the same code
cap. The planned Stage 3 scaling experiment (Battle replicas) was
skipped on recon Q4 grounds (Battle is not the limiter; adding replicas
cannot move a matchmaking-side cap).

## Secondary walls (Chapter 5 input)

From recon Q5, once the 100 ms sleep is lifted, expected walls in order:

1. **5 sequential Redis/Postgres awaits per pair** inside
   `ExecuteMatchmakingTickHandler` (lines 52, 65, 70, 107, 113, 119,
   123). At ~7 ms per tick the next floor is ~140 pairs/s ≈ 280 RPS
   before contention.
2. **Postgres SERIALIZABLE conflicts on `matchmaking.player_combat_profiles`**
   — 32 `40001` errors observed in `RUN_0_BASELINE.md` §6 during a failed
   attempt. Originate on the projection-writer side
   (`BattleCompletedConsumer`, `PlayerCombatProfileChangedConsumer`); will
   scale linearly with battles/s. Absent today at ~9 battles/s; likely
   reappear at 30–50 battles/s.
3. **Outbox + MassTransit publish per pair**: `EfUnitOfWork.SaveChangesAsync`
   inserts `Match` row plus outbox row in one transaction
   (`EfUnitOfWork.cs:19-20`). One Postgres round-trip per pair.
4. **Single Redis lease acquire/release cost** (~1–2 ms per tick) becomes
   non-trivial at sub-millisecond pairing cadences
   (`MatchmakingLeaseService.cs:15-16`).
5. **Ruled out**: `QueuePresenceSweepWorker` is on a separate 20 s
   interval (`QueuePresenceSweepWorker.cs:55`) — not coupled to pairing.

## Methodology

- **Stages planned**: 6 (25 / 50 / 100 / 200 / 500 / 1 000 simul); ran 3.
- **Per-stage duration**: 5-min sustained hold + 30 s ramp (~331–334 s
  wall clock each); endurance, not burst.
- **Cold start every stage**: `docker compose down -v` + reseed-400 +
  full capacity compose chain (base + obs + obs.override + override +
  capacity.yml). Single Battle replica, single Matchmaking replica.
- **Break criteria** (plan §3, stop on first trip): latency cliff
  `total_ms p95 > 4 800 ms` (3× baseline); REAL error rate > 5 %;
  throughput regression; host-side (Docker 100 % > 30 s, swap).
- **REAL-error definition**: `battle_ms > 0` AND `queue_wait > 6 ms`.
  All `BattleTimeout` rows observed across the scan (12 / 16 / 12) match
  the **shape-shutdown signature** documented in B1b: `battle_ms = 0`,
  `queue_wait ≈ 2–14 ms`, `total ≈ 90 000 ms` — Locust greenlet abandoned
  at shape end, correctly excluded from real-error counts.
- **Infrastructure snapshots**: 3 × `docker stats` per stage at 30–60 s
  intervals mid-hold; Postgres `pg_stat_activity`; Redis `INFO`/`DBSIZE`;
  RabbitMQ queue depths; host `vm_stat` swap pre/post.
- **Baseline reference**: Locust calibration (`PHASE_B1B_RESULTS.md` Run
  #3) landed within ±5 % of NBomber Run 0 on every acceptance metric;
  the Python port is not a source of measurement drift.
