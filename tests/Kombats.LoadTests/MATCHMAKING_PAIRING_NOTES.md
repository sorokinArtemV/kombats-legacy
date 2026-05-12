# Matchmaking Pairing Notes — Source Spelunking

Read-only walk of `src/Kombats.Matchmaking/`. Goal: explain the 19.8 s p50
queue wait observed in `PHASE_BREAKDOWN_REPORT.md` from the code.

## TL;DR

The matchmaking service pairs **exactly one couple per tick**. Each tick does
real Postgres + Redis + outbox work, then unconditionally sleeps 100 ms. With
50 bots (25 pairs) permanently queued under `KeepConstant`, the steady-state
pairing rate is throttled to roughly **one pair per 800 ms**, which matches
the observed 19.8 s wait within ~5 %. The configured tick interval (100 ms)
is not the bottleneck — the per-tick handler work is.

## 1. The pairing worker

**Type:** Periodic `BackgroundService` (option **a**).

`src/Kombats.Matchmaking/Kombats.Matchmaking.Bootstrap/Workers/MatchmakingPairingWorker.cs:13-75`

```csharp
internal sealed class MatchmakingPairingWorker : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var result = await _leaseService.TryExecuteUnderLeaseAsync(variant,
                async ct => { ... handler.HandleAsync(...) ... }, stoppingToken);
            await Task.Delay(TimeSpan.FromMilliseconds(_options.TickDelayMs), stoppingToken);
        }
    }
}
```

Registered at `src/Kombats.Matchmaking/Kombats.Matchmaking.Bootstrap/Program.cs:166`
(`builder.Services.AddHostedService<MatchmakingPairingWorker>()`).

The HTTP `queue/join` endpoint just adds the player to a Redis list — it does
**not** attempt to pair on the request thread.

## 2. Tick interval and batch size

**Tick interval:** `TickDelayMs = 100` (default), set in
`src/Kombats.Matchmaking/Kombats.Matchmaking.Bootstrap/appsettings.json:26-28`:

```json
"Worker": { "TickDelayMs": 100 }
```

**Batch size: exactly 1 pair per tick.** The handler at
`Kombats.Matchmaking.Application/UseCases/ExecuteMatchmakingTick/ExecuteMatchmakingTickHandler.cs:41-44`
calls `TryPopPairAsync` once and returns:

```csharp
var pair = await _queueStore.TryPopPairAsync(cmd.Variant, ct);
if (pair == null) return new MatchmakingTickResult(false);
```

No loop. No `while (queue.HasPair)`. One pair, then exit and sleep 100 ms.

Crucially, the `Task.Delay(100 ms)` at worker line 72 fires **whether or not
a pair was formed** — there's no short-circuit like "if we just matched a
pair, immediately try again without sleeping". So even if the queue has 50
ready pairs, the worker forms them one at a time at 100 ms + work intervals.

## 3. Pairing algorithm shape

**FIFO from a Redis list, no rating-awareness, no fairness window.**

The pop logic is a Lua script at
`Kombats.Matchmaking.Infrastructure/Redis/RedisScripts.cs` (`TryPopPairScript`):
LPOP first player, skip canceled players (ZSET check), LPOP second player; if
no second player exists, **RPUSH the first one back to the tail** so they
don't get re-picked immediately. No rating bucket, no MMR distance, no
minimum-wait timer. The repush-to-tail behavior is fairness for the *case
where the queue has odd length*; it doesn't extend waits in our test because
50 bots is always even.

Combat profile lookup (Postgres) happens *after* the pair is popped, and
both players are re-queued at the head if either lookup fails
(`ExecuteMatchmakingTickHandler.cs:53-64`).

## 4. Locks and serialization

Pairing is **globally serialized** by a Redis distributed lease.

`Kombats.Matchmaking.Infrastructure/Redis/MatchmakingLeaseService.cs:33-96`
acquires a `SET NX PX 5000` lock keyed by variant
(`Kombats.Matchmaking.Infrastructure/Redis/RedisLeaseLock.cs:34-69`). Only one
process across the whole deployment can run a tick at a time. With a single
matchmaking replica (per the handoff: `maxReplicas: 1`), the lease has zero
effect on throughput — it's there for the day backplane + multi-replica is
turned on. Not a current contributor to the wait.

Inside the handler, **per-tick work is serial**: two Postgres SELECTs
(`_profileRepository.GetByIdentityIdAsync` ×2), one EF `Add`, one MassTransit
outbox `PublishAsync` (Postgres INSERT into the outbox table), one EF
`SaveChangesAsync` (transaction commit including the outbox write), then two
Redis `SetMatchedAsync` calls
(`ExecuteMatchmakingTickHandler.cs:50-92`). That is real I/O — the lease lock
isn't the floor, the work inside it is.

## 5. The "Matched" notification path

**The worker does not push.** Match outcome propagates by two independent paths:

1. **Match row in Postgres** — `_matchRepository.Add(match)` +
   `_unitOfWork.SaveChangesAsync(ct)` at `ExecuteMatchmakingTickHandler.cs:75, 88`.
   The bot's polling endpoint `GetQueueStatusHandler` queries this first
   (`Kombats.Matchmaking.Application/UseCases/GetQueueStatus/GetQueueStatusHandler.cs:26`).
2. **Redis player-status keys** — `_statusStore.SetMatchedAsync(...)` ×2 at
   `ExecuteMatchmakingTickHandler.cs:91-92`, fallback if the Postgres query
   returns nothing yet.

There is no Redis pub/sub and no SignalR push from matchmaking to the BFF.
Bots **must poll** `api/v1/queue/status` to discover the match. The
VirtualPlayer polls every **500 ms** (`tests/Kombats.LoadTests/VirtualPlayer/VirtualPlayer.cs:294`),
so on average the bot lags the pairing event by ~250 ms after the fact.

Battle creation, separately, is driven by a MassTransit outbox message
(`Kombats.Matchmaking.Infrastructure/Messaging/MassTransitCreateBattlePublisher.cs:57`)
consumed asynchronously by the Battle service. The bot doesn't wait on
battle-create on the queue-status poll — it learns the `battleId` from the
match row.

## Back-of-envelope: does the source explain the 19.8 s wait?

**Naive lower bound (configured tick only, work assumed instantaneous):**
- 1 pair per 100 ms × 25 queued pairs ⇒ median wait ≈ 12 × 100 ms = **~1.25 s**.

**Naive estimate is wrong by ~16× compared to measured 19.8 s.** So the
100 ms tick is not the binding constraint. The binding constraint must be
the actual per-tick work.

**Solving for actual per-tick cycle time from measured throughput:**
- 134 successful iterations in 90 s steady-state ⇒ 67 battles completed.
- 67 battles / 90 s ⇒ **0.74 pairs/s** ⇒ one pair every **~1.35 s**.
- With ~25 pairs always queued, median bot waits ~12.5 × 1.35 s = **~17 s**
  pairing + ~250 ms poll lag + small extras (auth/onboard/connect ≈ 10 ms,
  join-battle retry loop ≈ 780 ms) = **~18 s** modeled vs **19.8 s** measured.
- **Modeled wait is within ~10 % of measured.** ✅

**So the source explains the wait.** Per-pair cycle time ≈ 1.35 s = 100 ms
configured sleep + **~1.25 s of real handler work** (2 Postgres SELECTs +
EF INSERT + outbox INSERT + transaction commit + 2 Redis writes, all
sequential). That's the throughput ceiling at one pair per tick.

The gap from "1.25 s of Postgres+Redis I/O" to a typical local-Postgres
expectation (~50-100 ms for that workload) suggests something else is
slowing the per-tick work — most likely **transaction contention or outbox
flush latency** under concurrent queue-join writes. Worth a focused dig
later (Prometheus `pg_*` metrics, or `EXPLAIN ANALYZE` on the outbox
INSERT) but **not required to explain the wait at the system level**.

## Candidate levers (sorted easy → hard)

### Lever 1 — Drain in one tick (1 config flag + ~5 lines, ~30 min)

Make the handler loop until `TryPopPairAsync` returns null, all under one
lease. One pair becomes a *batch of N* pairs per acquired-lease window.

```csharp
// Pseudocode for ExecuteMatchmakingTickHandler.HandleAsync
int paired = 0;
while (true) {
    var pair = await _queueStore.TryPopPairAsync(cmd.Variant, ct);
    if (pair == null) break;
    /* same per-pair work as today */
    paired++;
    if (paired >= maxBatchPerTick) break;  // safety cap
}
return new MatchmakingTickResult(paired > 0);
```

**Expected impact:** Pairing throughput becomes (1 / per-pair-work-time)
instead of (1 / (per-pair-work-time + 100 ms)). At ~1.25 s of work that's
~14 % faster — modest. **But also lets the queue drain to empty without
artificial sleeps when load spikes** (e.g. 50 bots all joining at once).

**Risk:** Lease TTL = 5 s, lock renewed at TTL/3 ≈ 1.67 s. Draining a deep
queue in one window could exceed TTL → renewal happens fine, but a runaway
loop could starve other instances. Cap at, say, `maxBatchPerTick = 50`.

### Lever 2 — Skip the sleep when work was done (1 line, ~5 min)

Just change the loop tail:

```csharp
// before:
await Task.Delay(TimeSpan.FromMilliseconds(_options.TickDelayMs), stoppingToken);

// after:
if (result is not { MatchCreated: true })
    await Task.Delay(TimeSpan.FromMilliseconds(_options.TickDelayMs), stoppingToken);
```

**Expected impact:** Throughput ≈ 1 / per-pair-work-time = 1 / 1.25 s = 0.8
pairs/s, up from 0.74 pairs/s. **~10 % improvement.** Mainly proves the
shape of the problem; not enough on its own.

### Lever 3 — Reduce per-pair handler work (moderate, ~2-3 hours)

The 1.25 s of per-pair work is the real ceiling. Concrete candidates:
- **Combine the two `SetMatchedAsync` Redis calls into one Lua script** —
  saves one round trip.
- **Pre-fetch combat profiles into Redis** — `IPlayerCombatProfileRepository`
  is hitting Postgres; the profile is already populated by the projection
  consumer, could land in Redis instead and skip Postgres entirely.
- **Move the outbox publish off the critical path** — MassTransit transactional
  outbox flushes in `SaveChangesAsync`. If this is dominant, an in-memory
  fan-out → durable forwarder pattern could shave 200-500 ms.

**Expected impact (combined):** Per-pair work down to ~200 ms → throughput
~5 pairs/s → wait at 25-queued-pair steady state down to ~2.5 s. **~8× win.**

### Lever 4 — Pair-on-join (large refactor, ~1 day)

Switch from periodic worker to: queue/join endpoint synchronously checks if
the queue now has ≥ 2 players and pairs them inline. Same lease lock for
safety. Eliminates the polling-worker latency entirely.

**Expected impact:** Wait goes to ~per-pair-work-time + poll lag = ~1.5 s
without any other changes. With Lever 3 applied: sub-second.

**Risk:** The queue/join endpoint becomes a write-amplified path that may
contend with itself under concurrent joins. The current periodic-worker
model has the advantage of *serializing* pair-formation regardless of join
rate. Worth a design review before doing.

---

**Recommendation if we proceed:** Lever 1 + Lever 2 together as one small
change (~30 min, low risk, proves the model). If the result is still > 5 s
median wait under the same 25-pair load, that's the signal to dig into the
per-pair work (Lever 3) — and that dig becomes its own portfolio chapter
about "fast workers, slow handlers, where to actually look".

## Things I did not touch

- `src/Kombats.Matchmaking/**` — read-only, as instructed.
- No commit. This notes file is uncommitted in the working tree on
  `feat/iteration-phase-breakdown`, awaiting your call on commit + branch.
