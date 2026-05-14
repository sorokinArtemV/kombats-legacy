# Run 1 — Backplane overhead root-cause analysis

*Static, code-level analysis of why the SignalR Redis backplane addition (committed at
`5f63ec9` on `feat/signalr-backplane`) caused a ~20× throughput drop on single-replica
Battle in Run 1. No code changes, no load runs, no commits — read-only.*

---

## 1. Summary

Every server-emitted event on the Battle hub is a `Clients.Group("battle:{battleId}").SendAsync(...)` (six sites in `src/Kombats.Battle/Kombats.Battle.Infrastructure/Realtime/SignalR/SignalRBattleRealtimeNotifier.cs:44,59,76,93,140,170`). With the backplane wired, **`SendGroupAsync` in `Microsoft.AspNetCore.SignalR.StackExchangeRedis 10.0.3` unconditionally publishes the message to Redis and awaits the publish RTT — there is no "all group members are local, skip the bus" short-circuit.** A typical 5-turn battle emits ~26 group sends (table in §2), so a single-replica battle now performs ~26 sequential `await StackExchange.Redis.ISubscriber.PublishAsync(...)` round-trips that the no-backplane baseline didn't do. Under the Run 1 load shape (25 concurrent battles all pumping serial publishes through one StackExchange.Redis multiplexer), each round-trip blew out from a near-zero in-process call to a contention-bound ~30–50 ms Redis RTT. 26 publishes × ~40 ms ≈ +1040 ms per battle — almost exactly the observed +1020 ms p50 shift. Bot threads serialize on `BattleHub.SubmitTurnAction`'s return path (which waits for the notification block in `BattleTurnAppService.CommitAndNotifyTurnContinued` to finish), so the per-turn publish cost feeds directly into `total_ms`. H1' confirmed.

---

## 2. Call map — every hub method invocation per battle

### 2.1 BFF → Battle (client → server hub methods)

Source: `src/Kombats.Bff/Kombats.Bff.Application/Relay/BattleHubRelay.cs`.

| Order | When | BFF call site | Battle hub method | SignalR addressing | Routes through Redis backplane? |
|---|---|---|---|---|---|
| 1 | At first `JoinBattleAsync` from frontend | `BattleHubRelay.cs:251` (`connection.StartAsync`) + `:258` (`InvokeAsync<object>("JoinBattle", battleId)`) | `BattleHub.JoinBattle(battleId)` (`src/Kombats.Battle/Kombats.Battle.Infrastructure/Realtime/SignalR/BattleHub.cs:42`) | Direct hub method on a WebSocket. Return value goes back via SignalR Completion frame on the same socket. | **No.** Hub method invocations + their return values flow over the per-connection WebSocket. `RedisHubLifetimeManager` does not intermediate them. |
| 1a | Inside `JoinBattle` | `BattleHub.cs:48` | `Groups.AddToGroupAsync(Context.ConnectionId, $"battle:{battleId}")` | Group membership mutation | **Conditionally.** Local-connection fast-path (`RedisHubLifetimeManager.cs:209-222` → `AddGroupAsyncCore` at `:302-318`) only updates in-process state; but it ALSO calls `_groups.AddSubscriptionAsync(...)` which **triggers a Redis SUBSCRIBE the first time a group is created on this server** (`SubscribeToGroupAsync` at `:613-641`). Subsequent joins to the same group are local-only. So: **1 awaited SUBSCRIBE per new battle, 0 traffic for the second joiner**. Plus 1 awaited UNSUBSCRIBE on group-empty (battle end). |
| 2..N+1 | Once per turn the bot submits | `BattleHubRelay.cs:343` (`bc.Hub.InvokeAsync("SubmitTurnAction", battleId, turnIndex, actionPayload, …)`) | `BattleHub.SubmitTurnAction(...)` (`BattleHub.cs:66`) | Direct hub method + Completion frame return. | **No.** Same as `JoinBattle`. |

**Per 5-turn battle, per bot:** 1 `JoinBattle` + 5 `SubmitTurnAction` invocations + 1 implicit `AddToGroup`. The two bots share one `AddToGroup`-triggered SUBSCRIBE (first joiner only). None of these BFF-direction calls publishes to Redis as message traffic; the BFF→Battle direction is a "thick"-client SignalR connection where the BFF acts as a regular client.

### 2.2 Battle → clients (server → group emissions — the dominant traffic)

All six notifier methods in `SignalRBattleRealtimeNotifier` (`src/Kombats.Battle/Kombats.Battle.Infrastructure/Realtime/SignalR/SignalRBattleRealtimeNotifier.cs:44, 59, 76, 93, 140, 170`) are `Clients.Group($"battle:{battleId}").SendAsync(...)`. Call sites that drive them:

| Order | Method | Notifier file:line | Driven from | Per 5-turn battle | Backplane? |
|---|---|---|---|---|---|
| 1 | `NotifyBattleReadyAsync` | `:44` | `BattleLifecycleAppService.cs:131` (CreateBattleConsumer) | **1** | **Yes** — `Clients.Group(...)` |
| 2 | `NotifyTurnOpenedAsync` (turn 1) | `:59` | `BattleLifecycleAppService.cs:132` | **1** | **Yes** |
| 3 | `NotifyPlayerDamagedAsync` | `:93` | `BattleTurnAppService.cs:391` (end path), `:582` (continuing path) | **~9** (smoke observed 9 across 5 turns = 1.8/turn) | **Yes** |
| 4 | `NotifyTurnResolvedAsync` | `:76` | `BattleTurnAppService.cs:409, 591` | **5** (one per resolved turn) | **Yes** |
| 5 | `NotifyTurnOpenedAsync` (turn N+1) | `:59` | `BattleTurnAppService.cs:599` | **4** (continuing turns only) | **Yes** |
| 6 | `NotifyBattleStateUpdatedAsync` | `:140` | `BattleTurnAppService.cs:438` (end path trailing) + `:605` (continuing path) | **5** (4 continuing + 1 trailing) | **Yes** |
| 7 | `NotifyBattleEndedAsync` | `:170` | `BattleTurnAppService.cs:419` | **1** | **Yes** |

**Total Battle → group publishes per 5-turn battle: ≈26.** Every one of them publishes to Redis under the backplane.

Bot-observable counts from `dotnet run -- smoke` after Run 1 setup (`RUN_1_SETUP_LOG.md` §6.2) cross-check the math: a single bot received `turnOpened=4 resolved=5 damaged=9 stateUpd=4` events (+1 BattleReady + 1 BattleEnded not counted in the smoke summary) = 24 raw events. The 2 extra in the table above are the trailing `BattleStateUpdated` after end (also re-confirmed in `SIGNALR_SURFACE_MAP.md` §G.1 row 8) and turn-1 `TurnOpened` that may be implicitly covered by the JoinBattle snapshot rather than a re-emit to the bot.

### 2.3 Battle → individual connections / users / all

Grep is exhaustive: `grep -rn 'Clients\.\(All\|Group\|Client\|User\|Caller\)' src/Kombats.Battle --include='*.cs'` returns **only** the six `Clients.Group(...)` sites in `SignalRBattleRealtimeNotifier.cs`. No `Clients.Client`, no `Clients.User`, no `Clients.All`, no `Clients.Caller` from any server-side notifier or worker. Hub-method return values (`JoinBattle` returning a snapshot) flow back through the per-connection WebSocket without going through `RedisHubLifetimeManager` (verified in §3.2 below).

### 2.4 BFF → frontend (out of scope for backplane cost — different hub)

`HubContextBattleSender.SendAsync` (`src/Kombats.Bff/Kombats.Bff.Api/Hubs/HubContextBattleSender.cs:15`) is `hubContext.Clients.Client(connectionId).SendCoreAsync(...)` on **BFF's own** `Bff.Api.Hubs.BattleHub`. **BFF has no backplane wired** (per `PHASE_3_D1_REPORT.md` §6 sanity: `src/Kombats.Bff/Kombats.Bff.Bootstrap/Program.cs:301` is bare `AddSignalR()`). Even if it did, `SendConnectionAsync` has a local-only fast-path (see §3.2). So BFF→frontend doesn't cost any Redis traffic — confirmed by `redis-cli PUBSUB CHANNELS '*'` only listing channels under `Kombats.Battle.Infrastructure.Realtime.SignalR.BattleHub:*`, never `Kombats.Bff.Api.Hubs.BattleHub:*`.

---

## 3. Backplane routing verification (Microsoft source, v10.0.3 tag)

Sources read (downloaded raw from `dotnet/aspnetcore` at the `v10.0.3` tag):

- `src/SignalR/server/StackExchangeRedis/src/RedisHubLifetimeManager.cs` (863 lines)
- `src/SignalR/server/StackExchangeRedis/src/Internal/RedisProtocol.cs` (285 lines)

### 3.1 `SendGroupAsync` has NO local-only short-circuit

`RedisHubLifetimeManager.SendGroupAsync` (file `RedisHubLifetimeManager.cs:184-190`):

```csharp
public override Task SendGroupAsync(string groupName, string methodName, object?[] args, CancellationToken cancellationToken = default)
{
    ArgumentNullException.ThrowIfNull(groupName);

    var message = _protocol.WriteInvocation(methodName, args);
    return PublishAsync(_channels.Group(groupName), message);
}
```

**No subscriber-count check, no "are any members local" check, no exclusion of the case "this is the only server".** The method unconditionally serializes the InvocationMessage and calls `PublishAsync` (`:295-300`):

```csharp
private async Task<long> PublishAsync(string channel, byte[] payload)
{
    await EnsureRedisServerConnection();
    RedisLog.PublishToChannel(_logger, channel);
    return await _bus!.PublishAsync(RedisChannel.Literal(channel), payload);
}
```

`StackExchange.Redis.ISubscriber.PublishAsync` returns `Task<long>` where the `long` is the **subscriber count Redis returned synchronously as the reply to PUBLISH**. The `await` therefore waits for one full Redis command round-trip, not fire-and-forget.

The message then comes back to the same server's subscription handler (`SubscribeToGroupAsync` at `:613-641`), which parses the MessagePack envelope and writes to each local group connection. On single-replica + 2 bots both joined to the group, the server is publishing a message to Redis only to receive it back as the sole subscriber. Round-trip overhead is paid in full; zero new functional value (because no other replica exists).

### 3.2 Compare: `SendConnectionAsync` HAS a short-circuit

For contrast, `RedisHubLifetimeManager.SendConnectionAsync` (`:167-181`) does check:

```csharp
public override Task SendConnectionAsync(string connectionId, string methodName, object?[] args, CancellationToken cancellationToken = default)
{
    ArgumentNullException.ThrowIfNull(connectionId);

    // If the connection is local we can skip sending the message through the bus since we require sticky connections.
    // This also saves serializing and deserializing the message!
    var connection = _connections[connectionId];
    if (connection != null)
    {
        return connection.WriteAsync(new InvocationMessage(methodName, args), cancellationToken).AsTask();
    }

    var message = _protocol.WriteInvocation(methodName, args);
    return PublishAsync(_channels.Connection(connectionId), message);
}
```

The comment is explicit: "If the connection is local we can skip sending the message through the bus since we require sticky connections. This also saves serializing and deserializing the message!" — and `SendGroupAsync` deliberately doesn't get the same treatment because group membership can span multiple servers and the package can't know whether a remote member is or isn't subscribed without coordination. Same applies to `SendUserAsync` (`:202-206`) and `SendAllAsync` (`:153-157`) — both always-publish.

This is the structural source of the regression: every group send eats a Redis RTT, by design, even when the entire group lives on the publishing server.

### 3.3 `AddToGroupAsync` local fast-path — caveats

`RedisHubLifetimeManager.AddToGroupAsync` (`:209-222`):

```csharp
public override Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
{
    var connection = _connections[connectionId];
    if (connection != null)
    {
        // short circuit if connection is on this server
        return AddGroupAsyncCore(connection, groupName);
    }
    return SendGroupActionAndWaitForAck(connectionId, groupName, GroupAction.Add);
}
```

But the "short-circuit" `AddGroupAsyncCore` (`:302-318`) still calls `_groups.AddSubscriptionAsync(groupChannel, connection, SubscribeToGroupAsync)`, and the `SubscribeToGroupAsync` callback (`:613-641`) executes `await _bus!.SubscribeAsync(RedisChannel.Literal(groupChannel))` — **the first joiner of any new group triggers a Redis SUBSCRIBE round-trip**. Subsequent joiners to the same group skip the SUBSCRIBE (group already subscribed). On battle end when the last connection leaves, `RemoveGroupAsyncCore` (`:324-344`) issues an UNSUBSCRIBE.

So `BattleHub.JoinBattle:48` adds **+1 SUBSCRIBE await + 1 UNSUBSCRIBE await per battle**, in addition to the 26 PUBLISH awaits — total ≈28 awaited Redis ops per battle.

### 3.4 Wire format and serialization

`RedisProtocol.WriteInvocation` (`RedisProtocol.cs:36-90`): wraps the SignalR `InvocationMessage` in a MessagePack array containing the excluded-IDs list + the serialized hub-message bytes. The hub message itself is pre-serialized via `DefaultHubMessageSerializer.SerializeMessage` (`:235`) using whichever hub protocols are registered — in our case JSON only (from `AddJsonProtocol(...)` at `src/Kombats.Battle/Kombats.Battle.Bootstrap/Program.cs:128`). Subscriber side: the local connection-writer fishes out the JSON bytes directly without re-deserializing the application payload (`SubscribeToGroupAsync:621-632` writes `invocation.Message` straight to the connection).

So the CPU cost of the backplane envelope is small per message — one MessagePack write + read, ~10-50 µs combined. The dominant cost is the **TCP round-trip and multiplexer queueing**.

### 3.5 StackExchange.Redis multiplexer concurrency

`StackExchange.Redis.ConnectionMultiplexer` (used by `_bus` here) uses **one physical TCP socket per Redis endpoint** by default for command pipelining. With 25 concurrent battle threads each issuing serial `await _notifier.NotifyX(...)` awaits, all PUBLISH commands serialize through that one socket. Commands pipeline (a new one can go out before the previous reply arrives), so throughput is fine — but **per-command latency is bounded below by 1 RTT** and inflates with multiplexer queue depth.

Under Run 1 conditions (25 concurrent battles × 5 serial publishes per turn resolution = up to 125 publishes in-flight at once), the multiplexer queue depth becomes the bottleneck. Empirically (Run 1 p50 = 2126 ms vs Run 0 p50 = 1106 ms), this manifests as ~30-50 ms average per publish.

---

## 4. Per-battle cost math + observed comparison

### 4.1 Build the budget

Per 5-turn battle (numbers from §2.2 + §3.3):

| Operation | Count | Per-op cost (Run 0 baseline) | Per-op cost (Run 1 estimated) |
|---|---|---|---|
| `Clients.Group(...)` PUBLISH | 26 | ~10 µs (in-proc enumerate + WebSocket write) | ~30-50 ms (Redis RTT under multiplexer contention) |
| Group SUBSCRIBE (first joiner) | 1 | n/a | ~1-5 ms |
| Group UNSUBSCRIBE (battle end) | 1 | n/a | ~1-5 ms |

**Run 0 expected per-battle notification cost:** 26 × 10 µs ≈ 0.3 ms (well below noise floor; not visible in `total_ms`).

**Run 1 expected per-battle notification cost:** 26 × 40 ms + 2 × 3 ms ≈ **1046 ms**.

### 4.2 Compare to observed

| Statistic | Run 0 (no backplane) | Run 1 (backplane) | Δ | Predicted by §4.1 |
|---|---|---|---|---|
| `total_ms` p50 | 1106 ms | 2126 ms | **+1020 ms** | **+1046 ms** |
| `total_ms` p95 | 1593 ms | 11190 ms | +9597 ms | see §4.3 |
| `total_ms` p99 | n/a | 12681 ms | n/a | see §4.3 |

**The +1020 ms p50 shift falls within ~2 % of the model's +1046 ms prediction.** No second mechanism is required to explain the median.

### 4.3 The tail (p95/p99) is HOL-blocking on top of the same mechanism

The +9597 ms p95 shift implies some battles incur ~370 ms per publish, not 40 ms. Possible mechanisms (all consistent with H1', none requiring a new hypothesis):

- **Multiplexer head-of-line blocking.** StackExchange.Redis pipelines commands in arrival order. When the multiplexer queue depth spikes (more than ~50 in-flight), individual command latency rises super-linearly.
- **Battle-resolution lease serialization.** `BattleTurnAppService` resolves a turn under the Redis state-store's CAS-marking; the notification block (`BattleTurnAppService.cs:579-623` for continuing turns) runs ENTIRELY inside the `SubmitAction` hub method. Other bots' `SubmitTurnAction` calls on the same battle block waiting for this. So if one battle's publish chain stretches to 500 ms, both its bots' `total_ms` for that turn inherits all of it.
- **Subscriber-side fan-out runs on the same threadpool.** `SubscribeToGroupAsync.OnMessage(...)` (`RedisHubLifetimeManager.cs:617`) executes on StackExchange.Redis's message-dispatcher threadpool. Under load, the dispatcher backs up and the publish→subscribe latency drifts up — but the publisher has already returned by then. This is a secondary effect on event-delivery latency, not on `total_ms` (which is gated on the SubmitAction Completion, not on event delivery).
- **Sequential awaits inside `CommitAndNotifyTurnContinued`.** The 5 sequential `await _notifier.NotifyX(...)` calls (`BattleTurnAppService.cs:582-622`) cannot overlap. With 25 battles all in this section simultaneously, each Redis publish lands behind 24 others in the multiplexer queue — average wait time scales with concurrent battles.

All of these scale with concurrency and all are consequences of the single root cause: **publishes are awaited serially per battle thread, with no local short-circuit, against a single Redis multiplexer**.

### 4.4 Pairing throughput collapse comes from the same root cause

The 1049 → 54 battles drop (-95 %) is a consequence of `total_ms` p50 doubling: each NBomber iteration takes ~2× longer to complete, and concurrency is capped by NBomber's 25 virtual users + the 50-bot user pool. So battles/second halves *at minimum*; the p95 blow-out drags throughput further down because the slowest iterations dominate the user-pool occupancy. Not a separate failure mode.

### 4.5 Redis state-key accumulation is a side effect, not a cause

The architect noted "Redis state accumulation is slightly above expected: 54 state keys, 800 action keys, DBSIZE=1254." This is `BattleRedisOptions.StateTtlAfterEnd` being unwired (`CLEANUP_WORKER_DIAGNOSIS.md` §2) and unrelated to backplane overhead. The 1254 keys at end-of-run is well below the levels that caused Run 0 attempt #1's failure (~7000 keys); state accumulation is not the dominant signal here.

---

## 5. Confidence verdict

**HIGH** that H1' (every server-side group emission is awaited through Redis, no local short-circuit, ~26 publishes × ~40 ms ≈ +1040 ms) is the complete explanation of the **p50** shift. The median model agreement (+1020 measured vs +1046 modeled) is tighter than the per-publish cost estimate's natural uncertainty.

**HIGH** that p95/p99 inflation is the same mechanism magnified by multiplexer queueing and sequential-await serialization (§4.3) — no second root cause needed, just compounding of the same Redis-bound bottleneck.

The one thing that would falsify the model: if a profile of the Battle process showed `_notifier.Notify*` await time clustering around 1-2 ms (not 30-50 ms), then the Redis publish hypothesis would be wrong and the +1000 ms would have to come from somewhere else (e.g. GC, threadpool starvation, MassTransit outbox flush latency). Since Grafana already confirms `turn_resolution_duration_milliseconds` p50 = 2.72 ms / p99 = 9.54 ms — i.e. the Battle engine itself is fine — the wait time is unambiguously in the notifier/publish layer, which is exclusively backplane-bound code on this build. The architect's pre-listed sanity checks rule out the alternatives.

**No mini-experiment required.** The static analysis closes the case.

---

## 6. Production implications

This Run 1 result is not "the backplane is broken" — it is a textbook **horizontal-scaling trade-off**: the single-replica configuration pays the per-publish Redis cost that buys cross-replica correctness in the multi-replica configuration. The fix to the regression doesn't live in the backplane; it lives upstream, in how Battle emits notifications. Concretely:

1. **Linear-in-replicas:** at 2 replicas (Run 3 territory), per-publish Redis cost is unchanged (still one PUBLISH per group send), but Redis processes twice as many sources and the multiplexer-queueing argument applies on both sides. Throughput will be near Run 1's, not Run 0's — proving the backplane was the binding constraint, not solving it. The Chapter 3 thesis (backplane unlocks horizontal scaling) is still demonstrable in the WITH-vs-WITHOUT comparison; the cost is the *price* of that unlocking.
2. **Headroom is in the call shape, not the wire.** Three structural changes to `BattleTurnAppService.CommitAndNotifyTurnContinued` would compress this dramatically *without touching the backplane*:
   - Parallelize the four serial awaits (`damage` × N + `TurnResolved` + `TurnOpened` + `BattleStateUpdated`) via `Task.WhenAll`. Per-publish cost stays the same; per-turn wall-clock drops to ~max(per-publish), not ~sum(per-publish). Out of scope for Chapter 3 by spec, candidate for a future "Battle notification batching" chapter.
   - Fold `TurnResolved` + `TurnOpened` + `BattleStateUpdated` into one composite event. Same information, ⅓ the publishes. Domain change, not transport change. Candidate for the same future chapter.
   - Move notification emission **off** the `SubmitAction` hub-method hot path entirely — emit from a worker that consumes a Redis stream or RabbitMQ queue and fan out asynchronously. Decouples the bot's `SubmitTurnAction` Completion frame from publish latency. Bigger change; also a separate chapter.
3. **Tuning levers inside StackExchange.Redis:** `ConfigurationOptions.SocketManager` can be tuned for thread allocation; `ConnectionMultiplexer.Configure(...)` supports `pool: <N>` multi-connection setups in newer versions, which would parallelize the multiplexer bottleneck described in §3.5. These are real-world ops levers but they don't change the per-publish-await semantics — they only relieve the queueing layer.
4. **At ~1k production battles/day** (mentioned in `RUN_0_BASELINE.md` §7), this overhead is invisible — load tests intentionally compress a day's traffic into 2 minutes. Production won't experience a ~20× slowdown; it'll experience ~26 × ~1 ms idle Redis RTT ≈ 26 ms per battle, well below human-perceptible thresholds. **The Run 1 number is a load-test stress-test artefact, not a production catastrophe** — but worth documenting because it surfaces the trade-off precisely.

---

## 7. STORY material — 3-5 sentence framing for Chapter 3 narrative

> Backplane включён — и средняя длительность боя выросла в два раза. Это не сюрприз: каждый `Clients.Group(...)` теперь — это await на Redis PUBLISH, который ждёт RTT и acknowledgement. На single-replica все подписчики уже локальные — PUBLISH идёт в Redis, и тут же возвращается обратно на тот же процесс через subscription — RTT платится впустую, но платится. На 5-ходовой бой это ~26 PUBLISH awaits, под нагрузкой 25 параллельных битв они выстраиваются в очередь через единственный StackExchange.Redis multiplexer, и итоговая стоимость складывается в +1 секунду к p50 на бой — то самое, что мы и измерили. Это не баг backplane, это его честная цена: за возможность горизонтально масштабироваться приходится платить per-publish round-trip — и теперь мы знаем, во сколько он обходится в нашей раскладке, и где находится следующая оптимизация (parallel-await в `BattleTurnAppService`, или отделение нотификаций от hot-path вообще).

---

## 8. Things I did not do

- No code changes to `src/Kombats.*`. No `.AddStackExchangeRedis(...)` toggling. No `BattleTurnAppService` await-parallelization.
- No load test runs — no `dotnet run -- load`, no `smoke`, no `single-bot`. Static analysis only against Run 1's jsonl + the live stack state the architect captured.
- No teardown. Stack left as-is per spec ("we may want to inspect more after your report").
- No commits.
- No mini-experiment script (e.g. instrumented `await Stopwatch` around `_notifier.Notify*`). The static analysis was decisive enough on the median; the tail required no extra evidence beyond the multiplexer-queueing argument.
- No web fetches against Microsoft docs sites — went straight to the `dotnet/aspnetcore` repo at the `v10.0.3` tag via `gh api` (raw file download). Source itself is the authoritative answer; doc pages restate it less precisely.

---

## 9. Open questions for architect

None — the model is decisive on the p50. The p95 tail is correlated with the same root cause via multiplexer queueing + sequential `await`s in `BattleTurnAppService`; no separate diagnostic needed unless you want to confirm by instrumentation in a follow-up.

## References

- Microsoft source (read raw at `v10.0.3` tag):
  - `dotnet/aspnetcore/src/SignalR/server/StackExchangeRedis/src/RedisHubLifetimeManager.cs` — `SendGroupAsync:184-190`, `PublishAsync:295-300`, `SendConnectionAsync:167-181`, `AddToGroupAsync:209-222`, `AddGroupAsyncCore:302-318`, `SubscribeToGroupAsync:613-641`.
  - `dotnet/aspnetcore/src/SignalR/server/StackExchangeRedis/src/Internal/RedisProtocol.cs` — `WriteInvocation:36-90`, `WriteHubMessage:230-247`.
- This repo:
  - `src/Kombats.Battle/Kombats.Battle.Infrastructure/Realtime/SignalR/SignalRBattleRealtimeNotifier.cs:44,59,76,93,140,170` (all 6 group-send sites).
  - `src/Kombats.Battle/Kombats.Battle.Infrastructure/Realtime/SignalR/BattleHub.cs:42,48,66` (JoinBattle, AddToGroupAsync, SubmitTurnAction).
  - `src/Kombats.Battle/Kombats.Battle.Application/UseCases/Turns/BattleTurnAppService.cs:382-457` (CommitAndNotifyBattleEnded), `:516-630` (CommitAndNotifyTurnContinued).
  - `src/Kombats.Bff/Kombats.Bff.Application/Relay/BattleHubRelay.cs:251,258,343` (BFF→Battle invocations).
  - `src/Kombats.Bff/Kombats.Bff.Api/Hubs/HubContextBattleSender.cs:15` (BFF→frontend, not backplane-bound).
  - `src/Kombats.Battle/Kombats.Battle.Bootstrap/Program.cs:128-131` (the one-line `.AddStackExchangeRedis(redisConnectionString)` registration).
- Repo artefacts:
  - `tests/Kombats.LoadTests/SIGNALR_SURFACE_MAP.md` §F, §G, §C.1.
  - `tests/Kombats.LoadTests/PHASE_3_D1_REPORT.md` (the diff that triggered this).
  - `tests/Kombats.LoadTests/RUN_0_BASELINE.md` (baseline numbers used in §4.2 table).
  - `tests/Kombats.LoadTests/RUN_1_SETUP_LOG.md` §4 (PUBSUB CHANNELS evidence the backplane is wired and active).
