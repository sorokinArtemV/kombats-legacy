# Phase D Recon — Matchmaking Pairing-Rate Ceiling

## Verdict

The pairing-rate limiter is a **fixed 100 ms inter-tick `Task.Delay` in a
single-pair-per-tick worker, gated by a single Redis lease**. Locator:
`MatchmakingPairingWorker.ExecuteAsync` at
`src/Kombats.Matchmaking/Kombats.Matchmaking.Bootstrap/Workers/MatchmakingPairingWorker.cs:72`
(the `Task.Delay`), with the handler popping at most one pair per tick at
`src/Kombats.Matchmaking/Kombats.Matchmaking.Application/UseCases/ExecuteMatchmakingTick/ExecuteMatchmakingTickHandler.cs:52`.
Config: `Matchmaking:Worker:TickDelayMs = 100` in `appsettings.json:27`
(default also `100` at
`src/Kombats.Matchmaking/Kombats.Matchmaking.Infrastructure/Options/MatchmakingRedisOptions.cs:40`).

## Q1 — Pairing mechanism

`MatchmakingPairingWorker : BackgroundService` — a hosted-service timer
loop, NOT request-driven.

- Registered: `Program.cs:166` (`AddHostedService<MatchmakingPairingWorker>()`).
- Loop: `MatchmakingPairingWorker.cs:43` — `while (!stoppingToken.IsCancellationRequested)`.
- Each iteration: acquire Redis lease via `MatchmakingLeaseService.TryExecuteUnderLeaseAsync`
  (`MatchmakingPairingWorker.cs:51`), invoke
  `ExecuteMatchmakingTickHandler.HandleAsync`, then unconditionally sleep
  `TickDelayMs` (`MatchmakingPairingWorker.cs:72`).
- Hardcoded variant: `const string variant = "default"` (`MatchmakingPairingWorker.cs:41`).

The handler is single-pair per call:
`TryPopPairAsync` returns exactly one pair (Lua script in
`RedisMatchQueueStore.cs:109-156`); if null, the tick returns immediately;
if non-null, the tick creates **one** match and returns. There is no
inner loop over the queue.

## Q2 — The rate limiter

Three reinforcing mechanisms, in order of impact:

1. **Inter-tick sleep (primary).** `await Task.Delay(TimeSpan.FromMilliseconds(_options.TickDelayMs), stoppingToken);`
   at `MatchmakingPairingWorker.cs:72`. Fires AFTER every tick whether or
   not a pair was created. Config key `Matchmaking:Worker:TickDelayMs`,
   current value **100 ms** (`appsettings.json:27`, default
   `MatchmakingRedisOptions.cs:40`).

2. **One-pair-per-tick handler.** `ExecuteMatchmakingTickHandler.HandleAsync`
   pops a single pair (`ExecuteMatchmakingTickHandler.cs:52`) and exits.
   Even if 20 players are queued, one tick pairs 2 of them. No batch
   parameter.

3. **Global single-writer lease.** All ticks run under
   `MatchmakingLeaseService.TryExecuteUnderLeaseAsync("default", ...)`
   (`MatchmakingPairingWorker.cs:51`, lease key
   `mm:lease:matchmaking:default` from
   `RedisLeaseLock.cs:181`). `SET NX PX` with 5 s TTL
   (`MatchmakingLeaseService.cs:15`). Only one process holds the lease
   at a time.

The 100 ms sleep is the binding constraint at current load; (2) and (3)
become binding only when (1) is reduced.

## Q3 — Arithmetic check

Predicted ceiling = `1 / (TickDelayMs + per_tick_work_ms)` pairs/s,
where each pair = 2 bots (one battle).

Per-tick work inside the lease (handler trace, all sequential awaits):

- `TryPopPairAsync` (Redis Lua) — ~1 ms
- `GetByIdentityIdAsync × 2` (Postgres SELECTs) — ~2–3 ms
- `PublishAsync` (in-memory MassTransit outbox add) — sub-ms
- `SaveChangesAsync` (Postgres INSERT match + outbox row) — ~3–4 ms
- `SetMatchedAsync × 2` (Redis HSET) — ~1 ms
- Lease release (`ReleaseLockAsync`, Lua) — ~0.5 ms

Estimated ≈ **7–8 ms** of work, then `Task.Delay(100 ms)`.

Predicted pair rate: `1000 / (100 + 7) ≈ 9.35 pairs/s`.
Predicted bot iteration rate: `9.35 × 2 ≈ 18.7 RPS`.

Observed in `RUN_6_STAGE_2_SNAPSHOT.md` lines 40, 92: **18.6 → 18.8 RPS,
flat across 25 → 50 bots.** Match within measurement noise. The
arithmetic confirms the limiter — Battle CPU dropping and matchmaking
CPU pinned ~40 % is consistent with a worker sleeping ~93 % of wall time
and processing one pair per ~107 ms.

## Q4 — Horizontally scalable without code change?

**No.** Two matchmaking replicas would NOT lift the ceiling — they would
share it.

- Variant `"default"` is hardcoded at `MatchmakingPairingWorker.cs:41`.
- Lease key derives only from variant:
  `mm:lease:matchmaking:default` (`RedisLeaseLock.cs:181`).
- Both replicas call `TryAcquireLockAsync` with `When.NotExists`
  (`RedisLeaseLock.cs:47-51`). Loser receives `false`, falls through to
  the same `Task.Delay(100 ms)` (`MatchmakingPairingWorker.cs:48-54, 72`),
  and retries. Net result: the same one-pair-per-100 ms cadence, just
  with leadership occasionally migrating.

Implication for Phase D Stage 3: **adding matchmaking replicas cannot
move the ceiling.** Adding Battle replicas (Stage 3's plan) also will
not, because Battle is not the bottleneck (Stage 2 snapshot: Battle CPU
went down). The Stage 2 ceiling is a code-level cap, not a capacity cap.
Lifting it is Chapter 5 scope; a one-line fix would target either
reducing `TickDelayMs` or looping the handler over the queue until empty
inside one lease window — but designing that is out of scope here.

## Q5 — Secondary limits behind the primary one

Once the 100 ms sleep is removed, the next walls in likely order:

1. **5 sequential Redis/Postgres awaits per pair** inside the handler
   (`ExecuteMatchmakingTickHandler.cs:52, 65, 70, 107, 113, 119, 123`).
   At ~7 ms each tick, the next floor is ~140 pairs/s ≈ 280 RPS — but
   contention (next bullets) will compress that well before it's hit.

2. **Postgres SERIALIZABLE conflicts on `matchmaking.player_combat_profiles`**,
   flagged in `RUN_0_BASELINE.md:58` (32 `40001` errors during the
   failed Run 0 attempt). The pairing path only **reads** profiles, so
   these conflicts originate on the **projection-writer** side
   (`BattleCompletedConsumer`, `PlayerCombatProfileChangedConsumer`) and
   will scale linearly with battles/s. At ~9 battles/s today they are
   absent; at 30–50 battles/s the projection writers will likely
   re-trigger this class of error. Did not audit the writer isolation
   level in this recon; flagged for Chapter 5.

3. **Outbox flush + MassTransit publish per pair.** `EfUnitOfWork.SaveChangesAsync`
   (`EfUnitOfWork.cs:19-20`) is a single transaction inserting a `Match`
   row plus the outbox row that MassTransit later relays. One round-trip
   per pair; not a hard cap at expected post-fix rates, but worth
   measuring.

4. **Single Redis lease + 5 s lock TTL with 1.67 s renewal**
   (`MatchmakingLeaseService.cs:15-16`). At sub-millisecond ticks the
   lease overhead per pair (acquire + release ≈ 1–2 ms) becomes
   non-trivial. Renewal cadence is fine; lease *contention* is fine with
   one replica; lease *acquire cost* is the relevant overhead.

5. **`QueuePresenceSweepWorker` is NOT coupled** to pairing — separate
   `BackgroundService` on a 20 s interval
   (`QueuePresenceSweepWorker.cs:55`, config `SweepIntervalSeconds: 20`
   in `appsettings.json:37`). Ruled out as a contributor to the Stage 2
   ceiling.

## What I did not do

- Did not propose a fix. Verdict names the limiter and the file:line;
  designing the change is Chapter 5 scope.
- Did not run the stack, did not edit any source, did not run tests.
- Did not audit the projection-writer isolation level for
  `player_combat_profiles` — flagged in Q5 #2 as the most probable
  secondary wall but not located to file:line in this pass.
- Did not measure per-tick work directly; the 7–8 ms estimate in Q3 is
  inferred from the await sequence, not from a trace. The arithmetic
  agreement with observed 18.7 RPS is the validation.
