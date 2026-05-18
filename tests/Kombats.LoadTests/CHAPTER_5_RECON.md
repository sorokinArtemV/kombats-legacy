---
title: Chapter 5 Recon — Matchmaking Pairing Path
scope: Read-only code recon. Findings only. No recommendations, no plan, no fix design.
date: 2026-05-18
---

# Chapter 5 — Matchmaking Pairing Path: Recon

Files examined:

- `src/Kombats.Matchmaking/Kombats.Matchmaking.Bootstrap/Workers/MatchmakingPairingWorker.cs`
- `src/Kombats.Matchmaking/Kombats.Matchmaking.Application/UseCases/ExecuteMatchmakingTick/ExecuteMatchmakingTickHandler.cs`
- `src/Kombats.Matchmaking/Kombats.Matchmaking.Infrastructure/Redis/RedisMatchQueueStore.cs`
- `src/Kombats.Matchmaking/Kombats.Matchmaking.Infrastructure/Redis/RedisScripts.cs` (hosts the `TryPopPairScript` Lua source invoked by `RedisMatchQueueStore.TryPopPairAsync`)
- `src/Kombats.Matchmaking/Kombats.Matchmaking.Infrastructure/Redis/MatchmakingLeaseService.cs`
- `src/Kombats.Matchmaking/Kombats.Matchmaking.Infrastructure/Redis/RedisLeaseLock.cs`
- `src/Kombats.Matchmaking/Kombats.Matchmaking.Infrastructure/Options/MatchmakingRedisOptions.cs` (defaults: `TickDelayMs = 100`, `CancelTtlSeconds = 600`)

---

## Q1 — Atomicity of the pop

### Redis structures backing the queue

`RedisMatchQueueStore.cs:31-33`:

```csharp
    private string GetQueueKey(string variant) => $"mm:queue:{variant}";
    private string GetQueuedSetKey(string variant) => $"mm:queued:{variant}";
    private string GetCanceledSetKey(string variant) => $"mm:canceled:{variant}";
```

Three structures, all under Redis DB index 1 (`MatchmakingRedisOptions.cs:14`: `public int DatabaseIndex { get; set; } = 1;`):

- `mm:queue:{variant}` — a Redis **LIST** (FIFO). Joining uses `RPUSH`, re-queueing uses `LPUSH`, popping uses `LPOP`. See `RedisScripts.cs:36` (`redis.call('RPUSH', queueKey, playerId)`), `RedisScripts.cs:196` (`redis.call('LPUSH', queueKey, playerId)`), and `RedisScripts.cs:110` / `RedisScripts.cs:142` (`local candidate = redis.call('LPOP', queueKey)`).
- `mm:queued:{variant}` — a Redis **SET** used as a dedupe / membership marker. `RedisScripts.cs:32`: `local added = redis.call('SADD', queuedKey, playerId)`.
- `mm:canceled:{variant}` — a Redis **ZSET** used to record cancellations with their epoch-second timestamps. `RedisScripts.cs:71`: `redis.call('ZADD', canceledKey, nowEpochSeconds, playerId)`.

### How the script obtains atomicity

`TryPopPairAsync` evaluates a single Lua script (`RedisMatchQueueStore.cs:120-123`):

```csharp
            var result = await db.ScriptEvaluateAsync(
                RedisScripts.TryPopPairScript,
                [queueKey, queuedKey, canceledKey],
                [nowEpochSeconds, cancelTtlSeconds]);
```

The script body (`RedisScripts.cs:91-176`) performs LPOP twice on `mm:queue:{variant}`, skipping any popped GUID whose `ZSCORE canceledKey candidate` returns non-`false` or whose `SISMEMBER queuedKey candidate` returns 0, then `SREM`s both winners from `mm:queued:{variant}`:

```lua
        -- Find first valid (non-canceled) player
        while not firstPlayer and attempts < maxAttempts do
            attempts = attempts + 1
            local candidate = redis.call('LPOP', queueKey)
            ...
            elseif redis.call('SISMEMBER', queuedKey, candidate) == 1 then
                -- Valid player in queue
                firstPlayer = candidate
```
(`RedisScripts.cs:108-126`)

```lua
        -- Both players found - remove from queued set and return pair
        redis.call('SREM', queuedKey, firstPlayer, secondPlayer)

        return {firstPlayer, secondPlayer}
```
(`RedisScripts.cs:172-175`)

Atomicity comes from Redis's Lua execution contract: Redis runs each script body single-threaded, with no other client command interleaved. The two `LPOP` calls and the trailing `SREM` are observed by all other clients as one indivisible operation.

### Invariant under concurrent callers

Because each `LPOP` mutates the list head before the next Redis command from any other client can run, two concurrent `TryPopPairAsync` invocations against the same Redis instance are linearized: one script completes, then the second runs. The first script removes its two GUIDs from the LIST before the second script's `LPOP` observes the list. **A single queued player GUID cannot be returned by two concurrent script executions** — it is removed from the list by `LPOP` in whichever script ran first, and the second script's `LPOP` returns the *next* element (or `nil`).

The "no second player" branch is also internal to the single script and pushes the first player back to the tail before returning, so the rollback is part of the same atomic unit (`RedisScripts.cs:144-148` and `RedisScripts.cs:166-169`):

```lua
            if not candidate then
                -- No second player - push first back to TAIL (fairness fix)
                redis.call('RPUSH', queueKey, firstPlayer)
                return {}
            end
```

What the atomicity does NOT guarantee:

- Atomicity is **per Redis script execution**. The .NET handler still does work *after* the script returns (profile lookup, `Match.Create`, `_battlePublisher.PublishAsync`, `_unitOfWork.SaveChangesAsync`, two `SetMatchedAsync` calls — see `ExecuteMatchmakingTickHandler.cs:62-124`). Those are not inside the Lua script.
- The fallback failure path at `ExecuteMatchmakingTickHandler.cs:73-84` re-injects both players via two **separate** `TryRequeueAsync` calls (`RequeueScript`, `RedisScripts.cs:186-200`), each its own Lua execution — there is no atomic "undo the pop" guarantee bracketing the whole handler.

---

## Q2 — What the lease actually guarantees

The lease is acquired against key `mm:lease:matchmaking:{variant}` (`RedisLeaseLock.cs:181`):

```csharp
    public static string GetLockKey(string variant) => $"mm:lease:matchmaking:{variant}";
```

Acquisition uses `SET NX PX` (`RedisLeaseLock.cs:47-51`):

```csharp
            var acquired = await db.StringSetAsync(
                lockKey,
                lockValue,
                TimeSpan.FromMilliseconds(ttlMs),
                When.NotExists);
```

Pairing is gated by it in the worker (`MatchmakingPairingWorker.cs:51-58`):

```csharp
                var result = await _leaseService.TryExecuteUnderLeaseAsync(
                    variant,
                    async ct =>
                    {
                        var r = await handler.HandleAsync(new ExecuteMatchmakingTickCommand(variant), ct);
                        return r.IsSuccess ? r.Value : new MatchmakingTickResult(false);
                    },
                    stoppingToken);
```

### Observation: the lease is not the mechanism that prevents double-pairing

The "no player popped into two pairs" invariant established in Q1 is enforced by the Lua script's atomic `LPOP … LPOP … SREM` sequence on `mm:queue:{variant}` and `mm:queued:{variant}`. It holds whether or not the caller is holding the lease, because Redis serializes script bodies regardless of which client invokes them.

Concretely, if two replicas were to call `_queueStore.TryPopPairAsync` simultaneously without any lease:

- Replica 1's script runs first, pops players (P1, P2) and `SREM`s them.
- Replica 2's script runs next, observes the LIST with P1 and P2 already gone, pops (P3, P4).

No GUID appears in both pairs. The four GUIDs land in two distinct matches.

### What the lease does do

`MatchmakingLeaseService.cs:48-54`:

```csharp
        if (!lockAcquired)
        {
            _logger.LogDebug(
                "Lease lock not acquired for variant {Variant}, sleeping. InstanceId={InstanceId}",
                variant, instanceId);
            return default;
        }
```

Only the replica holding `mm:lease:matchmaking:{variant}` proceeds into `handler.HandleAsync`. The lease therefore serializes which *replica* is allowed to run a tick — it is a **single-active-pairing-worker coordination key across replicas**, not the mechanism that prevents player-overlap in produced pairs. The 5-second TTL (`MatchmakingLeaseService.cs:15`: `private const int LockTtlMs = 5000;`) and renewal-every-1/3-TTL design (`MatchmakingLeaseService.cs:16`: `private static readonly int RenewalIntervalMs = LockTtlMs / 3;`) are consistent with a coordination role: keep one replica "elected" as the active pairer while it is alive, fail over after 5 s if it dies.

Side effects of removing or losing the lease (not present today) are not "two replicas pop the same player" — that is structurally impossible given the atomic Lua pop — but rather "two replicas concurrently run handler post-pop work" (profile load, `Add(match)`, `PublishAsync`, `SaveChangesAsync`, `SetMatchedAsync`) against *different* pairs at the same time. The pairs are disjoint by construction; the concurrency hits downstream resources (DB, MassTransit publishes, status writes), not pair correctness.

---

## Q3 — New races introduced by an inner pop-until-empty loop inside one lease window

Today the handler pops at most one pair per `HandleAsync` invocation (`ExecuteMatchmakingTickHandler.cs:49-57`):

```csharp
        (Guid, Guid)? pair;
        using (var a = _activitySource.StartActivity("matchmaking.pair.try-pop"))
        {
            pair = await _queueStore.TryPopPairAsync(cmd.Variant, ct);
        }
        if (pair == null)
        {
            return new MatchmakingTickResult(false);
        }
```

The lease has a 5 s TTL with a renewal task at ~1.67 s intervals. The renewal task runs in parallel to the action and is the only thing that signals lease loss:

`MatchmakingLeaseService.cs:15-16`:

```csharp
    private const int LockTtlMs = 5000; // Lock expires after 5 seconds (must be renewed)
    private static readonly int RenewalIntervalMs = LockTtlMs / 3; // Renew every 1/3 of TTL
```

`MatchmakingLeaseService.cs:116-137`:

```csharp
            while (!stoppingToken.IsCancellationRequested && !leaseLostSource.Token.IsCancellationRequested)
            {
                await Task.Delay(RenewalIntervalMs, linked.Token);

                if (stoppingToken.IsCancellationRequested || leaseLostSource.Token.IsCancellationRequested)
                    break;

                var renewalResult = await _leaseLock.RenewLeaseAsync(
                    lockKey,
                    instanceId,
                    LockTtlMs,
                    stoppingToken);

                if (renewalResult == 0)
                {
                    // Lease lost - cancel the action
                    _logger.LogWarning(
                        "Lease renewal failed (lease lost). Aborting tick. InstanceId={InstanceId}, LockKey={LockKey}",
                        instanceId, lockKey);
                    leaseLostSource.Cancel();
                    break;
                }
```

Cancellation of the action only happens when `RenewLeaseAsync` *returns 0*, i.e. when the renewal Lua script (`RedisLeaseLock.cs:91-98`) finds the key's value no longer matches our instance ID:

```csharp
            const string renewScript = @"
                if redis.call('GET', KEYS[1]) == ARGV[1] then
                    redis.call('PEXPIRE', KEYS[1], ARGV[2])
                    return 1
                else
                    return 0
                end
            ";
```

### New races that appear if the action body is changed to loop pops until empty

1. **Mid-batch lease expiry, undetected for up to one renewal interval.** If the inner loop's body keeps Redis busy enough that a renewal round-trip is delayed past 5 s after the last successful renewal — or if the key has already been silently deleted by something else and `RenewLeaseAsync` returns 0 only on the *next* renewal attempt — the lease has already expired in Redis while the action is still popping pairs and publishing matches. There is a detection window of up to `RenewalIntervalMs` (≈1.67 s) plus the renewal RTT before `leaseLostSource.Cancel()` fires.

2. **Another replica acquires the lease while we are still inside the loop.** Once the TTL elapses in Redis without successful renewal, any other replica's `TryAcquireLockAsync` (`SET NX PX`, `RedisLeaseLock.cs:47-51`) will succeed. From that moment until step 1 cancels our action, **two replicas are simultaneously inside the handler**. Each is calling the atomic pop script, so individual pair atomicity from Q1 still holds — no player ends up in two pairs — but two replicas are now concurrently:

   - calling `_battlePublisher.PublishAsync(request, ct)` (`ExecuteMatchmakingTickHandler.cs:107`)
   - calling `await _unitOfWork.SaveChangesAsync(ct)` (`ExecuteMatchmakingTickHandler.cs:113`)
   - calling `_statusStore.SetMatchedAsync(...)` twice per pair (`ExecuteMatchmakingTickHandler.cs:119`, `:123`)

   The lease's only correctness guarantee (Q2) — "one active pairer at a time" — is broken for the duration of the detection window.

3. **Partial-batch commit when the lease is lost mid-loop.** Today's single-pair-per-tick design makes the unit of work coincide with the unit of lease coverage: either the one pair gets fully published and saved, or the action is cancelled before any persistence. With a multi-pair inner loop, pairs popped earlier in the loop have already been through `SaveChangesAsync` and `PublishAsync` (those are inside the loop body), so a mid-batch `OperationCanceledException` from the linked token aborts the *current* iteration but leaves the *prior* iterations committed. The cancellation path in `TryExecuteUnderLeaseAsync` (`MatchmakingLeaseService.cs:72-79`) acknowledges the loss but cannot retract prior outbox messages or DB writes:

   ```csharp
           catch (OperationCanceledException) when (leaseLostSource.Token.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
           {
               // Lease was lost - abort action
               _logger.LogWarning(
                   "Action aborted due to lost lease. InstanceId={InstanceId}, Variant={Variant}",
                   instanceId, variant);
               return default;
           }
   ```

4. **Cancellation reaches the action only at `await` points and only via `linkedCancellation.Token`.** The action receives the linked token (`MatchmakingLeaseService.cs:70`: `return await action(linkedCancellation.Token);`). An inner loop must thread that token into every `await` inside it — including each `TryPopPairAsync`, `GetByIdentityIdAsync`, `PublishAsync`, `SaveChangesAsync`, `SetMatchedAsync`, and `TryRequeueAsync` call (see `ExecuteMatchmakingTickHandler.cs:52, 65, 70, 80-81, 107, 113, 119, 123`). Any `await` that does *not* take the linked token, or that is past the point of no return (already inside `SaveChangesAsync` against the DB), will continue running after lease loss is detected.

5. **Mid-batch lease loss with already-popped state.** A pop that succeeded inside the loop has already mutated `mm:queue:{variant}` and `mm:queued:{variant}` (`SREM` at `RedisScripts.cs:173`). If lease loss is detected after the pop but before `PublishAsync` / `SaveChangesAsync`, both players are now neither in the queue nor in any persisted match. The existing failure-mode requeue (`ExecuteMatchmakingTickHandler.cs:80-81`) is only invoked on missing-profile, not on `OperationCanceledException`. (Note: this race exists *today* in a degenerate form for the single pair, but an inner loop multiplies the surface area to N pairs per tick.)

### Specific 5 s overrun behavior

If the loop runs longer than 5 s on a deep queue and the renewal task fails (or is starved):

- `RenewLeaseAsync` returns 0 once it observes the key value mismatch (key expired and another replica reclaimed it, or key was deleted by `ReleaseLockAsync` from an attacker/admin).
- Renewal task logs `Lease renewal failed (lease lost). Aborting tick.` and calls `leaseLostSource.Cancel()` (`MatchmakingLeaseService.cs:135`).
- The action's `linkedCancellation.Token` becomes cancelled; the next `await` in the loop that observes this token throws `OperationCanceledException`.
- The `catch` at `MatchmakingLeaseService.cs:72-79` swallows the exception and returns `default`.
- Any pair-iterations that already passed their `await _unitOfWork.SaveChangesAsync(ct)` are persisted and published; any pair-iteration in flight at the moment of cancellation is in whichever partial state its current `await` was at.
- During the window between (a) the key actually expiring in Redis and (b) the renewal task observing the mismatch, another replica's `TryAcquireLockAsync` can have already succeeded and that replica's handler body can already be running in parallel.

---

## Q4 — Idle behavior and the unconditional `Task.Delay`

The current tick loop ends with an unconditional sleep regardless of whether a pair was produced (`MatchmakingPairingWorker.cs:43-73`):

```csharp
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var handler = scope.ServiceProvider
                    .GetRequiredService<ICommandHandler<ExecuteMatchmakingTickCommand, MatchmakingTickResult>>();

                var result = await _leaseService.TryExecuteUnderLeaseAsync(
                    variant,
                    async ct =>
                    {
                        var r = await handler.HandleAsync(new ExecuteMatchmakingTickCommand(variant), ct);
                        return r.IsSuccess ? r.Value : new MatchmakingTickResult(false);
                    },
                    stoppingToken);

                if (result is { MatchCreated: true })
                {
                    _logger.LogInformation(
                        "Match created: MatchId={MatchId}, BattleId={BattleId}",
                        result.MatchId, result.BattleId);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error in MatchmakingPairingWorker tick");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(_options.TickDelayMs), stoppingToken);
        }
```

The delay is at line `MatchmakingPairingWorker.cs:72`:

```csharp
            await Task.Delay(TimeSpan.FromMilliseconds(_options.TickDelayMs), stoppingToken);
```

Default `TickDelayMs = 100` (`MatchmakingRedisOptions.cs:40`: `public int TickDelayMs { get; set; } = 100;`).

### What happens in the empty-queue case today

When the queue is empty, the handler's pop returns null and exits early (`ExecuteMatchmakingTickHandler.cs:50-57`):

```csharp
        using (var a = _activitySource.StartActivity("matchmaking.pair.try-pop"))
        {
            pair = await _queueStore.TryPopPairAsync(cmd.Variant, ct);
        }
        if (pair == null)
        {
            return new MatchmakingTickResult(false);
        }
```

For an empty queue, the Lua pop body still runs `ZREMRANGEBYSCORE` on the canceled ZSET (`RedisScripts.cs:99-100`), then does one `LPOP` that returns nothing, and exits with `return {}` (`RedisScripts.cs:112-115`):

```lua
            local candidate = redis.call('LPOP', queueKey)
            
            if not candidate then
                -- No more players in queue
                return {}
            end
```

Per-replica per idle tick, today, against Redis DB 1:

1. `SET NX PX mm:lease:matchmaking:{variant} <instance> 5000` from `TryAcquireLockAsync` (`RedisLeaseLock.cs:47-51`).
2. Lua `TryPopPairScript` eval — does `ZREMRANGEBYSCORE`, one `LPOP`, returns `{}`.
3. Release Lua script (`RedisLeaseLock.cs:145-151`) — `GET` + `DEL` if owned:

   ```csharp
           const string releaseScript = @"
               if redis.call('GET', KEYS[1]) == ARGV[1] then
                   return redis.call('DEL', KEYS[1])
               else
                   return 0
               end
           ";
   ```

4. The renewal loop in `MatchmakingLeaseService.StartRenewalLoopAsync` (`MatchmakingLeaseService.cs:116-137`) typically does not fire for an empty-queue tick because the action returns well before `RenewalIntervalMs` (≈1.67 s) elapses, so no `PEXPIRE` is sent.
5. Then the unconditional `await Task.Delay(100ms, stoppingToken)`.

Net per replica at idle: roughly **3 Redis round-trips per ~100 ms**, i.e. ~30 ops/s. On the replicas that fail to acquire the lease, only step 1 happens (the `SET NX` returns false) and the worker still sleeps 100 ms before retrying — `MatchmakingLeaseService.cs:48-54` returns `default` immediately:

```csharp
        if (!lockAcquired)
        {
            _logger.LogDebug(
                "Lease lock not acquired for variant {Variant}, sleeping. InstanceId={InstanceId}",
                variant, instanceId);
            return default;
        }
```

So non-holder replicas idle at ~1 `SET NX PX` per 100 ms = ~10 ops/s each.

### What removing the unconditional delay does to the idle case

`Task.Delay` at `MatchmakingPairingWorker.cs:72` is the only thing pacing the outer `while (!stoppingToken.IsCancellationRequested)` loop. The handler itself does not back off when the queue is empty — it returns `new MatchmakingTickResult(false)` immediately (`ExecuteMatchmakingTickHandler.cs:56`). The lease service does not back off either when the lease was not acquired — it returns `default` immediately (`MatchmakingLeaseService.cs:53`). Neither path inserts any sleep.

With the `Task.Delay` removed:

- The lease-holder replica iterates as fast as it can complete `acquire → empty-pop → release`. Each cycle is a synchronous-ish sequence of three Redis round-trips against DB 1, so the cycle rate is bounded only by Redis RTT (sub-millisecond on localhost; a few ms on a network hop). At ~1 ms per round-trip, ~3 round-trips per cycle ⇒ several hundred to a few thousand acquire/release cycles per second per replica on the lease key alone.
- Non-holder replicas iterate at the rate of a single `SET NX PX` round-trip per loop — `RedisLeaseLock.cs:47-51` — which is uncapped by anything in `TryExecuteUnderLeaseAsync` or the worker. Same order of magnitude.
- Nothing in the handler, lease service, or worker observes the "no pair available" outcome to apply a longer back-off — `MatchmakingTickResult(false)` is consumed only by the `if (result is { MatchCreated: true })` log branch in the worker (`MatchmakingPairingWorker.cs:60-65`); the false case has no associated wait.

The unconditional `await Task.Delay(TimeSpan.FromMilliseconds(_options.TickDelayMs), stoppingToken)` at line 72 is therefore the sole pacing mechanism that keeps the lease acquire/release path and the empty-pop path from running at Redis-RTT-bounded rates on every replica continuously.
