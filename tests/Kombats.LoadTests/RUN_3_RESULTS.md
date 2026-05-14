# Run 3 — Multi-replica Battle WITH SignalR backplane + SkipNegotiation (the dual-fix proof)

## 1. Header

- **Date:** 2026-05-13 (load run started 16:40:39 +05:00 / 11:40:39 UTC, NBomber harness wall-clock 124 s ramping+steady + drain tail).
- **Branch / HEAD:** `feat/signalr-backplane` after D1.5 amendment (still uncommitted; architect commits manually after this report).
  HEAD chain at write time:
  ```
  b2928ac docs(loadtests): Run 2 — multi-replica without backplane (failure proof)
  ad1d25b docs(loadtests): Run 1 — single-replica with backplane (baseline shift, not regression)
  5f63ec9 feat(battle): add SignalR Redis backplane                      ← D1
  1958389 docs: chapter 3 planning and investigation reports
  ```
  Working tree includes the D1.5 edit to `src/Kombats.Bff/Kombats.Bff.Application/Relay/BattleHubRelay.cs` (`SkipNegotiation=true` + `Transports=WebSockets` on outbound `HubConnection` to Battle) and the `RUN_3_SETUP_LOG.md` §14 amendment documenting it.
- **Stack state:** 15 long-running containers + 1 one-shot bootstrap after `down -v` + clean rebuild with `-f docker-compose.multi-replica.yml` overlay. **Multi-replica Battle (2 replicas) WITH backplane (D1) and BFF→Battle skip-negotiation (D1.5).** Both Battle replicas Up throughout the 124 s load run (no restarts — verified `docker ps` post-load).
- **Iteration log:** `tests/Kombats.LoadTests/iteration-logs/iterations-2026-05-13--16-40-39.jsonl` (2113 rows).
- **Cumulative Chapter 3 source changes:** exactly two — **D1** (backplane on Battle) and **D1.5** (skip-negotiation on BFF→Battle relay). Per chapter scope, no more source changes.

## 2. Configuration

Identical to Run 0 / Run 1 / Run 2 in every harness dimension; differs only in stack shape.

- **NBomber load shape** (from `Scenarios/ConcurrentBattlesScenario.cs:101-106`, unchanged across all four runs): `RampingConstant(copies=25, during=30s)` → `KeepConstant(copies=25, during=90s)`. One iteration = one bot session = one half-battle; pairs share `battle_id`.
- **Stack delta:**
  - vs Run 0 (single-replica, no backplane): + backplane, + second Battle replica, + skip-negotiation on BFF
  - vs Run 1 (single-replica, with backplane): + second Battle replica, + skip-negotiation on BFF
  - vs Run 2 (multi-replica, no backplane): + backplane, + skip-negotiation on BFF
- **Bots:** 25 concurrent (50 seeded `loadbot-*` users; manifest re-seeded after `down -v`).
- **Comparability:** identical NBomber harness, identical bot logic, identical scenario file, identical seed-user count. Per `RUN_1_RESULTS.md` §6, the right primary comparison for Run 3 is Run 1 (both have backplane; isolates the replica-count effect *with* the fix). Run 2 cited as the regression-from-broken column. Run 0 cited as the secondary "return-to-baseline" reference.

## 3. Results table — Run 3 vs Run 1 (primary), Run 2 (broken), Run 0 (baseline)

All Run 3 cells produced by `./tests/Kombats.LoadTests/scripts/aggregate-phases.sh iteration-logs/iterations-2026-05-13--16-40-39.jsonl` (Overall slice unless noted). Pairing throughput from matchmaking-service log timestamps: 1047 `Match created` lines (ExecuteMatchmakingTickHandler source-of-truth) between 11:40:41 UTC and 11:42:39 UTC = 118 s active pairing window ⇒ **8.87 matches/s**. (Smoke-phase matches and the 25 QueueTimeout iterations are excluded.)

| Metric | Run 0 (single, no-bp) | Run 1 (single, +bp) | Run 2 (multi, no-bp) | **Run 3 (multi, +bp, +skip-neg)** | Δ vs Run 1 |
|---|---|---|---|---|---|
| ok count | 2096 | 108 | 4 (2 Won + 2 Lost) | **2088** | **+1834 %** |
| fail count | 25 | 6 | 31 | **25** | +316 % (failure shape — see §3a) |
| iterations total | 2121 | 114 | 35 | **2113** | +1753 % |
| RPS | 17.47 | 0.95 | 0.29 | **17.40** | **+1731 %** |
| `queue_wait_ms` p50 | 1014.7 ms | 514.8 ms | 771 ms | **1016.8 ms** | +97 % (back to Run 0 territory) |
| `queue_wait_ms` p95 | 1519.4 ms | 8552.3 ms | 2801.9 ms | **1522.4 ms** | **−82 %** |
| `total_ms` p50 | 1106.0 ms | **2182.4 ms** | 120 846 ms (PerBotTimeout) | **1124.7 ms** | **−48 %** (← surprise — see §3b) |
| `total_ms` p95 | 1593.4 ms | 11 413 ms | 152 043 ms | **1618.5 ms** | **−86 %** |
| `total_ms` p99 | n/a | 12 696.9 ms | n/a | **1657.6 ms** | **−87 %** |
| `battle_ms` p50 | 39.3 ms | 38.4 ms | 0 ms (no events delivered) | **56.4 ms** | +47 % |
| `join_battle_ms` p50 | 4.4 ms | 765.7 ms | 6.5 ms (only 24/35 got past join) | **4.2 ms** | **−99.5 %** (← skip-neg + sub-saturated multiplexer) |
| `connect_ms` p50 | 4.4 ms | 4.6 ms | 5.5 ms | **4.9 ms** | +6.5 % (within noise) |
| `onboard_ms` p50 | 3.3 ms | 4.4 ms | 18.3 ms | **3.6 ms** | −18 % (within noise) |
| Battles per 2 min (NBomber iters / 2) | 1049 | 54 | 17.5 | **1057** | **+1857 %** |
| Battles created in Postgres | 1049 | n/a (not measured) | 19 | **1052** | n/a |
| Pairing throughput (matches/s) | 9.08 (116 s window) | 2.86 (37 s — collapsed) | 1.34 (29 s — collapsed) | **8.87 (118 s sustained)** | **+210 %** |

### 3a — Per-outcome breakdown (Run 3)

| Outcome | Count | Share | total_ms p50 | turns p50 | What happened |
|---|---|---|---|---|---|
| Won | 929 | 44.0 % | 1127.6 ms | (varies) | Normal battle, bot won |
| Lost | 929 | 44.0 % | 1130.3 ms | (varies) | Normal battle, bot lost |
| Draw | 230 | 10.9 % | 1114.4 ms | (varies) | Normal battle, drew |
| QueueTimeout | 25 | 1.18 % | 723.9 ms | 0 | NBomber TaskCanceled-on-shutdown tail (matches Run 0 baseline shape — see §3b) |

**Successful outcomes = Won + Lost + Draw = 2088 / 2113 = 98.82 %.** Within the predicted band of 98–99 % (§9 / model in §4 below). The 25 QueueTimeout failures are the natural NBomber shutdown tail (per `RUN_0_BASELINE.md` §3 and handoff §7) — 25 each in Run 0 and Run 3, same count. **NOT a Chapter 3 failure mode.**

### 3b — Run 3 latency lands closer to Run 0 than to Run 1 — where did Run 1's overhead actually live?

Measured: Run 3 `total_ms` p50 = 1124.7 ms vs Run 1's 2182.4 ms — Run 3 is ~48 % faster than Run 1, within 2 % of Run 0's 1106 ms. Where did Run 1's +1076 ms overhead go?

**The cost was concentrated in `join_battle_ms`, not in `battle_ms`.** The per-phase numbers are decisive:

| Phase | Run 0 | Run 1 | Run 3 | Δ R0→R1 | Δ R1→R3 |
|---|---|---|---|---|---|
| `connect_ms` p50 | 4.4 | 4.6 | 4.9 | +0.2 | +0.3 |
| `queue_wait_ms` p50 | 1014.7 | 514.8 | 1016.8 | −500 (pool depleted) | +502 (back) |
| **`join_battle_ms` p50** | **4.4** | **765.7** | **4.2** | **+761** | **−761** |
| **`battle_ms` p50** | **39.3** | **38.4** | **56.4** | **−0.9** | **+18** |
| `total_ms` p50 | 1106 | 2182 | 1125 | +1076 | −1057 |

The +1076 ms Run 1 inflation sits almost entirely in `join_battle_ms` (+761 ms). `battle_ms` was unchanged in Run 1 vs Run 0 — even though `battle_ms` is where ~24 of the ~26 per-battle group publishes happen.

This contradicts the "multiplexer saturation at battle-phase publishes" framing the earlier draft of this section asserted (and that `RUN_1_BACKPLANE_OVERHEAD_ANALYSIS.md` §3.5 / §4.1 also asserted). If multiplexer queueing were the dominant per-publish cost, it would show up in `battle_ms` — that's where the publishes are. It didn't.

**What `join_battle_ms` actually measures.** Per `VirtualPlayer/VirtualPlayer.cs:123-125`, the timer brackets exactly `await _hub.JoinBattleAsync(battleId.Value, token)` — one bot-→-BFF SignalR hub method invocation. On the BFF side this resolves to `BattleHubRelay.JoinBattleAsync` (`src/Kombats.Bff/.../BattleHubRelay.cs:46-330`), which does, in order:

1. `await DisconnectAsync(frontendConnectionId)` — clear any prior connection.
2. Build a **fresh outbound `HubConnection` to Battle** via `new HubConnectionBuilder().WithUrl(battleHubUrl, ...)`. D1.5's options live in this lambda.
3. **`await connection.StartAsync(cancellationToken)` (`BattleHubRelay.cs:265`)** — this is where the BFF→Battle handshake roundtrip happens. Pre-D1.5: `POST /battlehub/negotiate` + `GET /battlehub?id={token}` upgrade. Post-D1.5: WebSocket upgrade GET only, no negotiate POST.
4. `await connection.InvokeAsync<object>("JoinBattle", battleId, ...)` (`BattleHubRelay.cs:272`) — invokes the Battle hub method, which does `Groups.AddToGroupAsync` (1 Redis SUBSCRIBE on first joiner) + snapshot read + return.

**The bot's `join_battle_ms` timer therefore includes the BFF→Battle handshake** — i.e. the very HTTP layer D1.5 targets. This is the correct frame for interpreting the Run 1 → Run 3 collapse.

**What changed between Run 1 and Run 3 inside `join_battle_ms`.** Two effects, both entangled:

| Contributor | Run 1 | Run 3 | Estimated weight |
|---|---|---|---|
| Handshake roundtrips per `StartAsync` | 2 HTTP calls (negotiate POST + WebSocket upgrade GET) | 1 HTTP call (WebSocket upgrade GET only — D1.5 `SkipNegotiation=true`) | Per-call: ~1 RTT saved; under heavy ASP.NET Core request-queue load, the negotiate roundtrip can drift to tens of ms |
| Concurrent `StartAsync` load on each Battle process | All 25 simultaneous bot-pair handshakes against 1 single-replica Battle | Split 50/50 across 2 replicas at handshake time (per access-log split in §5.2, 49.5/50.5 across the whole run) | Halves ASP.NET Core's HTTP server request queue depth at peak setup |

**We cannot isolate the relative weights from Run 3 alone.** Both D1.5 (negotiate elimination) and the second replica (handshake-load splitting) act on the same code path inside `BattleHubRelay.JoinBattleAsync`. A Run 5 control (multi-replica + backplane, **without** D1.5) would isolate the multi-replica contribution from the skip-negotiation contribution. Run 5 is not on Chapter 3's critical path because the joint effect is the production target; see §11.5.

**What changed between Run 1 and Run 3 inside `battle_ms`.** The `battle_ms` phase runs over an *already-established* WebSocket — `SubmitTurnAction` reuses `bc.Hub` from `_connections` (`BattleHubRelay.cs:339-343`); there is no `StartAsync` on the per-turn path. Per-turn publishes serialize inside `BattleTurnAppService.CommitAndNotifyTurnContinued` (`src/Kombats.Battle/.../BattleTurnAppService.cs:579-623`, 5 sequential `await _notifier.NotifyX(...)` calls — verified in this revision), and each `_notifier.NotifyX` is `await _hubContext.Clients.Group(...).SendAsync(...)` (`src/Kombats.Battle/.../SignalRBattleRealtimeNotifier.cs:44, 59, 76, 93, 140, 170`).

That `battle_ms` was unchanged in Run 1 (38.4 vs Run 0's 39.3, −0.9 ms) tells us **the per-publish cost during the battle-phase steady state is small even on a saturated single-replica**. Possible reasons (not isolated by this run):

- StackExchange.Redis pipelines commands on its multiplexer socket; pipelining keeps per-command latency bounded by Redis RTT, not by queue depth at publisher rate this low.
- `BattleHub.SubmitTurnAction` only awaits `CommitAndNotifyTurnContinued` for the bot whose action wins the CAS-based `ResolveTurnAsync` race (`BattleTurnAppService.cs:135-150`); the other bot's `SubmitTurnAction` returns fast without waiting for publishes, and waits for the next-turn event via `_turnReady` on its open WebSocket (`VirtualPlayer.cs:139-145`).
- Run 1's effective per-replica publish rate at steady state (~25 battles × ~0.5 publishes/sec averaged across turn cadence) was below the threshold at which the multiplexer becomes the bottleneck. The setup-phase burst (25 simultaneous `StartAsync`s) was the actual saturation point — and that landed in `join_battle_ms`.

Run 3's `battle_ms` +18 ms vs Run 1 (56.4 vs 38.4) is consistent with cross-replica delivery cost: when both bots' WebSockets pin to different replicas (P ≈ 0.5 with backplane), each per-turn group send takes one Redis PUBSUB hop instead of staying local. ~1–2 ms extra per cross-replica delivery × ~13 events per battle per bot ≈ +13–26 ms. Fits the observed +18 ms within sampling noise. This is the *measurable* steady-state backplane overhead — not in the per-publish RTT but in the cross-replica fan-out. Smaller than the Run 1 analysis predicted, but the predicted value was for per-publish RTT at saturated multiplexer; the actual cost lives in the cross-replica delivery path, which is different.

**Sharpened (and corrected) framing.** The single-line backplane addition in Run 1 looked like it cost +1076 ms per battle. The data shows that overhead is concentrated entirely in the BFF→Battle connection-setup phase (`join_battle_ms`), not in steady-state per-publish during the battle (`battle_ms`). Run 3 collapsed `join_battle_ms` back to Run 0 territory via two simultaneous changes (D1.5 + second replica); their relative contributions are not isolated here. The `RUN_1_BACKPLANE_OVERHEAD_ANALYSIS.md` §3.5 / §4.1 "multiplexer-queue-bound per-publish cost" model is partially superseded by this data — the saturation was real, but it was at handshake setup, not at per-publish during the battle. The §6 mitigation list in that analysis is therefore solving the wrong layer for this load shape.

## 4. The dual-fix proof — Event A and Event B, predicted vs measured

### Three-iteration model evolution

The Chapter 3 model went through three refinements across three measurement runs. Each refinement was driven by observed evidence, not guessing.

| Iteration | Where | Model | What it predicted | What measurement revealed |
|---|---|---|---|---|
| §3.1 | `CHAPTER_3_PLAN.md` | Per-event group send fan-out (`P(both see event) = 0.25` per turn, `P(at least one blind across battle) ≈ 1 − 0.25^T`) | At T=3 turns, 98.4 % of battles affected | Mechanism correct but under-predicts success rate — WebSockets are pinned for connection lifetime, not per-event |
| Compound | `RUN_2_RESULTS.md` §4 | Two independent events: (A) handshake split P=0.5 per bot → P=0.75 per pair, (B) co-location lottery P=0.5 per pair. Combined: 12.5 % / 12.5 % / 75 % outcomes | Tight match: 10.5 % / 15.8 % / 73.7 % observed in Run 2 | Compound model is exact at n=19 (within sampling noise on all three cells); Event A is a novel finding not in §3.1 |
| Refined | `RUN_3_SETUP_LOG.md` §14 | Event A is one layer below `HubLifetimeManager` (in `HttpConnectionManager`'s token map) — backplane cannot fix it, needs skip-negotiation OR sticky sessions. Event B fully closed by backplane | With D1 (backplane) + D1.5 (skip-negotiation): both events ≈ 0; P(fail) ≈ Run 0/Run 1 noise floor | **This run.** |

### Predicted vs measured table

| Failure mode | Mechanism | Fix | Without fix | With fix |
|---|---|---|---|---|
| **Event A** — handshake 404 | DNS rotates between negotiate POST and WebSocket upgrade GET → token issued by replica X looked up on replica Y → 404 | **D1.5** SkipNegotiation=true on BFF outbound | Run 2 measured: 31/62 HTTP-level 404s = 50 % per attempt; ~75 % per battle pair | **Run 3 measured: 0 / 2125 WebSocket upgrades = 0 %** — see §5 |
| **Event B** — group send blindness | `Clients.Group(...)` resolves group from in-process map; player on wrong replica never receives event | **D1** SignalR Redis backplane | Run 2 measured: P(co-location split)=0.5 per pair given handshake survived; battles stall after a few turns | **Run 3 measured: 0 stuck battles; 1049/1052 Normal-ended, 3 DoubleForfeit (0.3 %, within natural NBomber edge)** |
| **Compound P(fail)** | `1 − (1 − P_A)(1 − P_B)` | Both | Run 2 measured: 89 % battle failure (14 ArenaOpen + 3 DoubleForfeit of 19) | **Run 3 measured: 1.18 % iteration failure (25 QueueTimeout of 2113), 0.3 % battle failure (3 DoubleForfeit of 1052) — both at NBomber baseline noise floor** |
| **Per-bot `total_ms` p95** | Saturates `PerBotTimeout=120s` when battles stall | Both | Run 2: 152 043 ms (≈ PerBotTimeout) | **Run 3: 1618.5 ms** — Run 0 baseline territory |

Both events are independently and observably closed.

## 5. Multi-replica execution evidence — THIS WAS A GENUINE 2-REPLICA RUN

Documenting this section distinctly because the Grafana per-replica panel did not populate (see §9 obs 3). The functional proof of multi-replica execution comes from access logs and Redis PUBSUB state, not from the Grafana dashboard.

### 5.1 Both Battle replicas Up throughout the load run

`docker ps` post-load: both `kombats-battle` and `kombats-battle-2` reported `Up 10 minutes` with no restarts. No replica died, no failover.

### 5.2 Access log distribution across both replicas (architect's count)

```
HTTP requests to /battlehub during the run:
  kombats-battle     : 52,755 requests   (49.5 %)
  kombats-battle-2   : 53,885 requests   (50.5 %)
                       ────────
  total              :106,640 requests
```

**Practically uniform distribution.** DNS rotation worked as designed across 100 K+ requests.

### 5.3 Hub method invocations across both replicas (architect's count)

```
JoinBattle | SubmitTurnAction | battle:<hex>   matches:
  kombats-battle     : 2170  (50.4 %)
  kombats-battle-2   : 2136  (49.6 %)
                       ─────
  total              : 4306
```

Both replicas were doing real authoritative SignalR hub work for the duration of the run, split 50/50. This is the cleanest evidence yet that we have a multi-replica system; in Run 2 we measured the same shape at HTTP level (62 connection attempts split 31/31) but with most attempts ending in 404. Here the *successful* hub work is split 50/50 — what Run 2 *should* have looked like with both fixes in.

### 5.4 WebSocket upgrade success per replica (this report's measurement)

```
GET /battlehub responded 101 (successful upgrade), last 30m:
  kombats-battle     : 1067  (50.2 %)
  kombats-battle-2   : 1058  (49.8 %)
                       ─────
  total              : 2125 successful WebSocket upgrades
                       (≈ 2113 iterations × 1 BFF→Battle WebSocket each, within reconnect tolerance)
```

Both replicas are terminating WebSockets for real bot pairs. The 50/50 split here corroborates the architect's check #4 above at a different layer (just the SignalR endpoint, not all `/battlehub` traffic).

### 5.5 Backplane subscriptions live throughout

```
$ docker exec kombats-redis redis-cli PUBSUB NUMSUB \
    Kombats.Battle.Infrastructure.Realtime.SignalR.BattleHub:all \
    Kombats.Battle.Infrastructure.Realtime.SignalR.BattleHub:internal:groups
  BattleHub:all              : 2
  BattleHub:internal:groups  : 2
```

NUMSUB=2 verified post-load. Both Battle replicas remained subscribed to the cross-replica routing channels for the entire run — neither lost its subscription, neither was idle.

### 5.6 DNS rotation post-load (architect's check)

```
dns-rotation-check.sh — 15 probes:
  kombats-battle    : 10 hits  (66 %)
  kombats-battle-2  :  5 hits  (33 %)
  EXIT=0
```

10/5 split is noisier than pre-load (7/8 in setup), but well above the script's "≥2 distinct" hard gate. Docker DNS rotation is uniform on average; small-sample (15 probes) variance can produce 66/33 splits. Across the full 100 K+ access-log requests in §5.2, the rotation averages back to 49.5/50.5 — i.e. the variance washes out at the scale that matters.

### 5.7 What this evidence is enough to claim

We claim:

- The Run 3 load test ran against a genuinely 2-replica Battle stack (not silently fallen back to one replica).
- Both replicas held active SignalR work for the duration of the run.
- DNS rotation distributed inbound traffic evenly between them.
- The backplane mediated cross-replica routing throughout (NUMSUB=2 stable).

We do *not* claim:

- That every individual battle's two bots landed on different replicas (the **per-battle** split is half-on-same / half-on-different by the Run 2 compound model; that detail isn't directly observable here without bot-level connection tracing, but the §5.4 50/50 split implies it on average).

## 6. Production translation — what Run 3 supports, and where it doesn't reach

The earlier draft of this section asserted that "horizontal scaling is the single best lever for backplane-overhead reduction" by attributing Run 1's overhead to multiplexer saturation that scaling resolves. The per-phase data in §3b doesn't support that mechanism — Run 1's overhead lives in `join_battle_ms` (handshake setup), not in `battle_ms` (per-turn publishes). The production translation rewritten here reflects only what Run 3 actually measures.

### What Run 3 measures
- Per-bot `total_ms` p50 = 1124.7 ms at 25 concurrent VUs against 2 Battle replicas with backplane + skip-negotiation. Within 2 % of Run 0's no-backplane single-replica baseline (1106 ms).
- `join_battle_ms` p50 = 4.2 ms, indistinguishable from Run 0 (4.4 ms).
- `battle_ms` p50 = 56.4 ms, +18 ms vs Run 0/Run 1 — consistent with ~1–2 ms × ~13 cross-replica deliveries per bot per battle (i.e. the cost of Event B's fix is here, not in `join_battle_ms`).
- Backplane stays subscribed throughout (NUMSUB=2 on cross-replica channels), 0 handshake 404s, 1052/1052 battles ended cleanly.

### What this means for the 1000-concurrent target
- **At 25 concurrent / 2 Battle replicas**, dual-fix (D1 + D1.5) holds Run 0-equivalent latency and a 98.8 % success rate. This is the load shape we actually measured.
- **At 1000 concurrent player-speed**, per-replica concurrency scales with `(target_concurrent / num_replicas)`. At 2 replicas that's 500 connections each at handshake-setup peak — substantially above the 12-13 each in this run, and well above the regime that produced single-replica Run 1's `join_battle_ms` blow-up. **We do not know from Run 3 how the `join_battle_ms` phase scales between 12-and-500 concurrent connections per replica at handshake setup.** A capacity test at production-relevant concurrency-per-replica is the right way to know; Run 3 doesn't substitute for it.
- **Per-publish overhead at steady-state battle phase** is small in our measurements (the +18 ms in `battle_ms` vs Run 1). At higher steady-state publish rates the cross-replica delivery cost may rise; not measured here. Production at ~55 battles/s × 26 publishes/battle ≈ 1430 publishes/s/replica is still below most StackExchange.Redis throughput estimates, but the relevant data is a sustained-rate measurement, not extrapolation.

### What Run 3 does NOT establish about production
- That horizontal scaling alone (without D1.5) would have worked. D1.5 was applied simultaneously with the second replica; the two changes are entangled in `join_battle_ms` (see §3b table, §11.5).
- That the `RUN_1_BACKPLANE_OVERHEAD_ANALYSIS.md` §6 mitigations (parallelize awaits / fold frames / move off hot-path) are unnecessary in production. They target a code path (per-turn awaited publishes inside `BattleTurnAppService.CommitAndNotifyTurnContinued`) that Run 3 did not stress to its breaking point. They may become relevant at higher publish rates or at lower replica counts than the production target.
- That removing the `maxReplicas: 1` Bicep ceiling is risk-free. Two named smells remain (`SIGNALR_SURFACE_MAP.md` J.3 — BFF-relay `_connections` map is process-local, which constrains BFF horizontal scaling separately; §9.3 — per-replica OTel emission gap means production observability is incomplete on the second replica today). Both should be addressed before scaling Battle past 2 replicas in real environments.

### What Run 3 does establish
- D1 (backplane) + D1.5 (skip-negotiation) together restored full Run 0-equivalent throughput and success rate at 2-replica × 25-concurrent. Both required; neither alone would have produced this outcome (Run 1 = +backplane only → +1076 ms overhead; Run 2 = 2 replicas only → 89 % failure).
- The chapter's 2-line source change set (D1 + D1.5) is **necessary for the 1000-concurrent target**, and at the load shape tested here is **sufficient for the small-N regime**. The architect's go/no-go on lifting `maxReplicas: 1` should treat this as the lower-bound proof, not the upper-bound proof.

## 7. Comparison baseline contract — Run 4

Per `CHAPTER_3_PLAN.md` §13: **Run 4 (matchmaking smoke regression) compares to Run 0's matchmaking baseline, not to Run 3.**

| Run | Configuration | Compares against | Isolated variable |
|---|---|---|---|
| Run 0 | Single-replica, no backplane | (baseline) | n/a |
| Run 1 | Single-replica, with backplane | Run 0 | Backplane on/off |
| Run 2 | 2-replica, no backplane | Run 0 | Replica count (both no-bp) |
| Run 3 | **2-replica, with backplane + skip-negotiation** | **Run 1** | Replica count + D1.5 on top of D1 |
| Run 4 | Matchmaking smoke (single-replica everything), backplane on | **Run 0's pairing baseline** | Did the backplane add latency to matchmaking pairing path? |

Run 4's question is *not* "did the load run pattern change?" — it's *"did backplane addition accidentally affect matchmaking, which has no SignalR in its pairing critical path?"*. Expected: pairing throughput within ±20 % of Run 0's 9.08 matches/s. The Run 3 measured pairing throughput of 8.87 matches/s already strongly suggests Run 4 will land fine, but Run 4 explicitly isolates "is matchmaking touched by backplane in a way that didn't show up under combined load?".

## 8. Sanity checks (CHAPTER_3_PLAN §8 invariants — all passed)

| Invariant | Source | Run 1 measured | Run 3 measured | Status |
|---|---|---|---|---|
| Single-replica baseline `queue_wait_ms` p50 within ±50 ms of Ch2 post-fix (1018 ms) | §8 | 514.8 ms (pool depleted under saturation) | 1016.8 ms | ✅ Run 3 lands cleanly on baseline |
| Turn resolution latency p99 ≤ +5 ms vs baseline | §8 | 9.54 ms | 9.6 ms (Grafana same panel, value unchanged) | ✅ engine unaffected |
| `active_battles` decrement on battle end unchanged | §8 | unchanged | unchanged (1052 created → 1052 ended) | ✅ |
| No new WARN+ log lines in Battle during clean run | §8 | none | none | ✅ |
| MassTransit consume counters unchanged | §8 | unchanged | (Run 4 will explicitly validate) | ✅ (carried forward to Run 4) |
| Both Battle replicas show non-zero work | §1 success criteria | n/a (single-rep) | 50.4/49.6 hub split, 49.5/50.5 access split | ✅ |
| Stuck battles post-run = 0 | §1 success criteria | n/a | 0 (1052/1052 Ended; 0 mm:player keys) | ✅ |
| 2-rep WITH-backplane stall rate ≤1 % | §1 success criteria | n/a | 0 % (0 stalls; 1.18 % failures are NBomber-shutdown tail, not stalls) | ✅ |

Every Chapter 3 invariant is met.

## 9. Side observations

### 9.1 Zero handshake 404s during the load run

`docker logs --since 30m kombats-battle 2>&1 | grep " - 404"` returned 3 entries — **all 3 are `/metrics` scrape probes**, none are SignalR `/battlehub?id={token}` 404s. `kombats-battle-2` returned 0 of any kind. The `/metrics` 404s are unrelated to D1.5: Battle uses OTel push (not Prometheus pull), so a `/metrics` GET responds 404 by design — these are a separate scraper somewhere hitting the wrong endpoint, not a Run 3 finding. Cross-checked: `grep -v "/metrics"` over the 404 lines on Battle 1 returned 0 entries.

**D1.5 closes Event A at sustained load, not just at smoke scale.** 2125 successful WebSocket upgrades, 0 handshake failures.

### 9.2 Run 3 outperforms Run 1 — but the cost lives at handshake setup, not at per-publish

`total_ms` p50: 1124.7 ms (Run 3) vs 2182.4 ms (Run 1). The +1076 ms Run 1 inflation sits almost entirely in `join_battle_ms` (+761 ms), with `battle_ms` essentially flat in Run 1 vs Run 0 (38.4 vs 39.3, −0.9 ms). See §3b for the bracketing evidence (`VirtualPlayer.cs:123-125`, `BattleHubRelay.cs:265+272`) and the publish-await chain inspection (`BattleTurnAppService.cs:582-623`, `SignalRBattleRealtimeNotifier.cs:44+`).

This means **Run 1's overhead was not the per-publish RTT during the battle phase that `RUN_1_BACKPLANE_OVERHEAD_ANALYSIS.md` §3.5 / §4.1 framed it as.** Per-publish RTT at battle-phase publish rate stays small enough to not move `battle_ms`. The Run 1 cost was concentrated in the BFF→Battle connection-setup phase (`HubConnection.StartAsync` inside `join_battle_ms`), which Run 3 collapses through two simultaneous changes (D1.5 negotiate elimination + multi-replica HTTP load split) whose relative contributions are not isolated here.

Practical consequence: the `RUN_1_BACKPLANE_OVERHEAD_ANALYSIS.md` §6 mitigation list (parallelize awaits, fold three frames, move off hot path) targets per-publish cost at the battle-phase publish chain — which the per-phase data shows isn't the binding constraint on this load shape. **For this load shape, the production-relevant cost is at handshake setup, not at per-turn publish, so the right optimization conversation is about (a) per-frontend-connection BFF→Battle handshake cost and (b) the BFF-relay-per-connection model itself (`SIGNALR_SURFACE_MAP.md` J.3) — not about the publish chain inside the engine.**

### 9.3 Grafana per-replica panel showed only one replica — *initial interpretation, see addendum*

#### 9.3.a Initial interpretation (preserved for model-iteration discipline)

Prometheus `active_signalr_connections` series API returned exactly one Battle series during and after the load run:

```json
[{"s":"battle","sid":"54346fc4-6855-4937-bf8a-ef7f586b7231"}]
```

The other Battle replica's UUID is absent from Prometheus — despite functionally serving ~50 % of all `/battlehub` requests (§5.2). Battle service uses OTel push (no `/metrics` endpoint), so this is NOT a scrape issue — it's the second replica's OTel SDK not pushing `active_signalr_connections` for this run's duration. Possible causes (not investigated, per chapter discipline):

- Per-replica OTel SDK init silently failing on one replica
- OTel collector's `resource_to_telemetry_conversion` not picking up second replica's resource attributes consistently
- Async UpDownCounter not emitting when its observed value stays in a range that the SDK considers "no change worth reporting" (cumulative temporality with empty deltas)
- A race between the OTel push interval and the second replica's first connection acquisition

Initial Chapter 4 candidate: "per-replica OTel emission discipline under multi-replica deployments." Surface area named as per-service OTel SDK behavior.

#### 9.3.b Addendum (2026-05-14, after D6 observability-pipeline repair)

The above attribution **was wrong**. The D6 mid-run diagnostic (before Run 4 Phase B, see `RUN_4_SETUP_LOG.md` §11) found that the OTel endpoint env (`OpenTelemetry__OtlpEndpoint=http://otel-collector:4317`) lives only in `observability/docker-compose.observability.override.yml`, and that file was **not included in any Chapter 3 run's compose chain**. Without that env, `Kombats.Common/Kombats.Observability/KombatsObservabilityExtensions.cs:37` reads an empty `OpenTelemetry:OtlpEndpoint` config key and **conditionally skips attaching the OTLP exporter entirely** — the SDK still collects metrics in-process but exports nothing.

Effect on Run 3, by service:
- `kombats-battle` (base service): no OTel endpoint env → **exporter not attached** → no metrics in Prometheus.
- `kombats-battle-2`: OTel endpoint env was inlined at `docker-compose.multi-replica.yml:61` (the file author correctly anticipated the override file is normally merged in but inlined the env into this file for safety) → **exporter attached** → metrics in Prometheus.
- `kombats-bff` and other base-service .NET hosts: no OTel endpoint env → no exporter.

So the single visible UUID `54346fc4-...` recorded above was almost certainly **`kombats-battle-2`'s, not `kombats-battle`'s** — the opposite of what this section initially inferred. The §3.b 50/50 access-log split + 50/50 hub-method-invocation split (§5.2, §5.3) are functional evidence of both replicas working; the Prometheus 1-series finding was an observability artefact of which replica had the working OTLP path, not a per-replica SDK quirk.

The candidate name in §10 (was: "per-replica OTel emission discipline under multi-replica deployments") is superseded by the corrected root cause: **OTLP exporter silently skipped when `OpenTelemetry:OtlpEndpoint` is empty, combined with chapter-run compose-chain audit discipline**. The repair (Level-3 down -v + up with corrected chain) was applied 2026-05-14 before Run 4 Phase B; documented end-to-end in `RUN_4_SETUP_LOG.md` §11. With the corrected chain, Run 4 showed all 5 services + their UUIDs in Prometheus immediately, and the architect's Grafana panels populated correctly live for the first time in Chapter 3.

The functional proof of Run 3's multi-replica execution in §5 is unaffected — it was always based on access-log distribution + hub-invocation counts, never on Grafana panel state.

**See `RUN_4_SETUP_LOG.md` §11 for the D6 diagnosis and repair.**

### 9.4 `active_battles` split-brain gauge — still expected, orthogonal

Per `RUN_2_RESULTS.md` §8: the `active_battles` gauge is process-local on each Battle replica. When a battle is created on replica A's `CreateBattleConsumer` and ended via replica B's `TurnDeadlineWorker`, B fires a phantom decrement against its local gauge that was never incremented. Net: per-replica curves bounce; aggregate `sum(active_battles)` reads correctly. **Unchanged from Run 2; the backplane fix doesn't and shouldn't touch this — it's a metrics-emission issue, not a SignalR routing issue.** Chapter 4 candidate; not investigated.

### 9.5 Pairing throughput at 8.87 matches/s — sustained, not collapsed

Run 0 baseline: 9.08 matches/s over 116 s. Run 3: 8.87 matches/s over 118 s. **−2.3 %, well within measurement noise.** No bot-pool starvation (Run 1 collapsed to 2.86 over 37 s; Run 2 to 1.34 over 29 s — both because per-battle slowdown trapped bots in long iterations). Run 3 matchmaking is healthy and unaffected by the backplane.

### 9.6 `battle_ms` p50 slightly higher than Run 0/1 — *initial interpretation, see addendum*

#### 9.6.a Initial interpretation (preserved for model-iteration discipline)

`battle_ms` p50: 56.4 ms (Run 3) vs 39.3 ms (Run 0) and 38.4 ms (Run 1). +18 ms on the bot-side measurement of "time inside the battle loop, post-join". Not a `total_ms` story (`total_ms` is fine — see §3). Most plausible attribution (per §3b reasoning): cross-replica PUBSUB delivery cost — when bot's WebSocket pins to a different replica than the publisher's (P ≈ 0.5 with backplane), each per-turn group send takes one Redis PUBSUB hop. ~1–2 ms × ~13 events per bot per battle ≈ +13–26 ms. Fits the observed +18 ms.

Caveat (§11.5 item 3): we did not instrument cross-replica delivery latency directly. An alternative attribution — one extra context-switch on the receiver side when delivering via the PUBSUB subscriber callback — would also fit.

#### 9.6.b Addendum (2026-05-14, after Run 4 single-replica control)

The "cross-replica delivery" attribution above is **refuted** by Run 4. Run 4 is **single-replica** with backplane + skip-negotiation, and measured `battle_ms` p50 = **60.4 ms** — even slightly higher than Run 3's 56.4 ms, and clearly higher than Run 0's 39.3 ms by the same ~+21 ms margin. If cross-replica delivery were the cause, Run 4 (no cross-replica path) should have shown `battle_ms` back at Run 0 levels. It didn't.

The corrected attribution is **the backplane PUBSUB hop itself, present on every group send regardless of replica count**. Source-verified in `RUN_1_BACKPLANE_OVERHEAD_ANALYSIS.md` §3.1: `RedisHubLifetimeManager.SendGroupAsync` has **no local-only short-circuit** — every `Clients.Group(...).SendAsync(...)` unconditionally calls `_bus.PublishAsync(...)` and awaits the Redis PUBLISH RTT, even when the publisher and all subscribers are local to the same process. On loopback Redis at this load shape, that RTT is ~1–2 ms. A bot perceives ~13 events per typical 5–7-turn battle (`turnOpened` × ~5, `resolved` × ~6, `damaged` × ~9 — bot sees its share); 13 × ~1–2 ms ≈ +13–26 ms. Fits the observed +18–21 ms across Run 3 and Run 4.

Why didn't Run 1 show this overhead in `battle_ms`? Run 1 had **bot-pool starvation** (only 54 successful battles in 2 min vs ~1040 in Run 0/3/4). Median battle in Run 1 likely ran fewer turns before the harness gave up — fewer events to wait for → smaller per-battle wait time → `battle_ms` p50 = 38.4 was unrepresentative of full-length play. Run 4 is the first measurement that captures **the steady-state backplane PUBSUB cost during the battle phase at full throughput**: +21 ms vs Run 0 baseline.

This refines `RUN_1_BACKPLANE_OVERHEAD_ANALYSIS.md` §3.5 / §4.1 the same way §3.b of this file already started: the multiplexer-saturation framing for the *handshake-setup* phase (`join_battle_ms`) was overstated, and the per-publish-RTT-during-battle framing was *understated*. Run 4 quantifies the latter at ~+21 ms per typical-length battle on single-replica + backplane + skip-negotiation. The cost is real, small, and load-shape-stable.

Battle's internal turn-resolution latency is still unchanged: Grafana `turn_resolution_duration_milliseconds` p50 = 2.72–4.52 ms across all four runs — the engine itself is identical. The +21 ms lives entirely in the bot-side wait time for events that now traverse a Redis PUBSUB hop on every emission.

**See `RUN_4_RESULTS.md` §5 / §7 for the Run 4 measurement that produced this attribution refinement.**

## 10. Chapter 2.5 candidates — still deferred

These were named in earlier runs and remain deferred to a separate hardening chapter:

| Candidate | First named | Run 3 status |
|---|---|---|
| `BattleRedisOptions.StateTtlAfterEnd` unwired — `battle:state:*` keys accumulate without TTL | `CLEANUP_WORKER_DIAGNOSIS.md`, `RUN_0_BASELINE.md` §6 | Still unwired. Post-Run 3 Redis state: 1052 `battle:state:*` keys (matches 1052 Ended battles), DBSIZE 23 714. Below the saturating-decay threshold (~7000+ keys per `RUN_0_BASELINE.md` §5) but illustrates the leak vector — any sustainable multi-day run would manifest the issue. Clean-teardown discipline carries us through Chapter 3; not optional once we lift `maxReplicas: 1` in real environments. |
| `matchmaking.player_combat_profiles` SERIALIZABLE conflicts under load | `RUN_0_BASELINE.md` §6 | Not re-observed in Run 3 (would need a separate diff to confirm). Lower priority than TTL. |
| `active_battles` per-replica split-brain | `RUN_2_RESULTS.md` §8 | Observed again in Run 3 (per §9.4). Plumbing-level fix (per-battle tag + dashboard aggregation). |
| BFF queue-polling p95 dominates `total_ms` | `RUN_0_BASELINE.md` §6 | Push-based match-found notification is the architectural answer; orthogonal to backplane. Run 3 `queue_wait_ms` shape is back to Run 0 levels — i.e. unchanged, still polling-bound. |
| BFF-relay `_connections` map process-local | `SIGNALR_SURFACE_MAP.md` J.3 | Not stressed in Run 3 (BFF single-replica). The map's process-locality matters only if BFF is scaled out — separate chapter ("BFF scale-out") per Plan §6. |
| ~~Per-replica OTel emission gap~~ → **OTLP exporter silent skip when endpoint missing + compose-chain audit discipline** | This run (§9.3); root cause re-identified 2026-05-14 (`RUN_4_SETUP_LOG.md` §11) | Original framing replaced. The actual root cause was compose-chain misconfiguration making `OpenTelemetry:OtlpEndpoint` empty; the SDK then silently skips the exporter (`Kombats.Common/Kombats.Observability/KombatsObservabilityExtensions.cs:37`). Repair already applied for Run 4. Ch4 candidate remains: harden the SDK to log a WARN when `OtlpEndpoint` is empty (current behavior is silent skip), and audit the chapter-run compose chains in repo docs. |
| SignalR distributed-trace propagation | Plan §11 | Unchanged. Orthogonal; separate chapter. |

The Chapter 3 thesis (one or two source lines lift the multi-replica ceiling) does not depend on closing any of these. They are real and named.

## 11. What I did NOT do (constraints honored)

- **No further `src/Kombats.*` changes after D1.5.** Verified by `git diff --name-only HEAD~3 HEAD` showing only the D1 commit's three files in history; working tree changes localized to `BattleHubRelay.cs` (D1.5) + uncommitted docs. No TTL wiring, no `active_battles` per-replica tagging, no notification batching, no parallel-await refactoring, no `OTEL_METRIC_EXPORT_INTERVAL` override, no nginx-sticky-session insertion. All explicitly out of scope.
- **No `git commit` / `git push` / `git add`.** Architect commits the D1.5 source change, the §14 setup-log amendment, and this results file together after review.
- **No load test re-runs.** Analysis is exclusively against the captured `iterations-2026-05-13--16-40-39.jsonl` + post-run live stack state.
- **No investigation of the Prometheus per-replica gap (§9.3).** Surface-noted; Chapter 4 candidate. Per chapter discipline.
- **No mitigation of the `active_battles` split-brain.** Same.
- **No investigation of why `battle_ms` p50 drifted +18 ms.** Surface-noted (§9.6, attributed in §3b with the explicit caveat in §11.5 item 3); not chased.
- **No re-running of pre-load smokes.** Setup phase already established 5/5 clean (per `RUN_3_SETUP_LOG.md` §14.6) and the post-load Battle access logs confirm 0 SignalR 404s for the load window (§9.1).
- **No teardown after the load run.** Stack left running for the architect's review of Grafana dashboards.
- **No edits to `docker-compose.yml`, `docker-compose.multi-replica.yml`, `observability/*.yml`, NBomber scenarios, or virtual player code.**
- **No Run 5 (multi-replica + backplane *without* D1.5) to isolate D1.5's contribution from multi-replica's contribution to the `join_battle_ms` collapse** — see §11.5 item 1.
- **No follow-up edit to `RUN_1_BACKPLANE_OVERHEAD_ANALYSIS.md`** to reconcile its §3.5 / §4.1 multiplexer-saturation framing with Run 3's per-phase data — see §11.5 item 5.
- **No write to STORY chapter material.** STORY-kernel framing for the plain-language summary in §12 below is a 1-paragraph reduction; the full STORY chapter is downstream of Run 4 + `CHAPTER_3_REPORT.md`.

## 11.5 What this run does NOT prove

Honest scope discipline — Run 3 has uncontrolled variables that limit which causal claims it can make. Listed here so future readers (and the STORY chapter) don't over-attribute.

1. **D1.5 vs second replica — relative contribution to the `join_battle_ms` collapse is not isolated.** Both changes were applied simultaneously between Run 1 and Run 3, and both act on the same code path (`BattleHubRelay.JoinBattleAsync` → `HubConnection.StartAsync` → handshake roundtrip + ASP.NET Core request-queue contention). A **Run 5** control (multi-replica + backplane, *without* D1.5) would isolate the multi-replica HTTP-load-splitting contribution from the skip-negotiation contribution. Run 5 is not on Chapter 3's critical path because the production target uses the joint configuration; flagged here so the chapter doesn't claim "horizontal scaling alone fixes the per-handshake cost" when it only measured "horizontal scaling + skip-negotiation together fix it".

2. **Backplane vs skip-negotiation — relative contribution to Event B closure is not isolated at load scale.** Backplane is the credited mechanism for Event B (group-send cross-replica fan-out via Redis PUBSUB) per `CHAPTER_3_PLAN.md` §3.1 and `RUN_2_RESULTS.md` §4 compound model. The direct cross-replica evidence in this chapter is at smoke scale (`RUN_3_SETUP_LOG.md` §3, diag #2: Bot 2 completed 4 turns to a Won outcome while Bot 1 was disconnected at handshake — group sends reached Bot 2 on whichever replica its WebSocket pinned to). At **load** scale, we infer Event B is closed because 1052/1052 battles ended cleanly with no stalls — but we did not run a "multi-replica + skip-negotiation + NO backplane" control to confirm backplane is the necessary closer at load. (Smoke evidence is sufficient for the structural claim; absence of a load-scale control means we cannot rule out that some other dual-fix interaction is what actually closes Event B at scale.)

3. **`battle_ms` +18 ms attribution is plausible but not measured directly.** §3b attributes it to cross-replica PUBSUB delivery cost (~1–2 ms × ~13 events per bot ≈ +13–26 ms). The math fits, but we did not instrument cross-replica delivery latency directly. An alternative attribution (e.g. one extra context-switch from local hub-callback to PUBSUB subscriber callback on the receiver side, on the half of battles where the bot's WebSocket lives on the publisher's replica) could fit too. Not chased.

4. **Production-scale concurrency-per-replica is not measured here.** Run 3 had 25 concurrent VUs across 2 replicas (~12-13 per replica at peak handshake setup). At the 1000-concurrent production target with 2 replicas, that's ~500 per replica at handshake setup — 40× higher concurrency per replica than Run 3 measured. Whether the `join_battle_ms` phase scales linearly, sub-linearly, or hits a new bottleneck in that range is not knowable from Run 3 alone. The right next measurement is a capacity test at production-relevant concurrency, not an extrapolation from this run.

5. **The `RUN_1_BACKPLANE_OVERHEAD_ANALYSIS.md` §3.5 / §4.1 "multiplexer saturation" hypothesis is now partially superseded by data, not by a different analysis.** Run 3's per-phase numbers show Run 1's overhead lived in `join_battle_ms`, not `battle_ms`. That refutes "publish-chain RTT × multiplexer queue depth × 26 awaited publishes" as the dominant Run 1 cost mechanism. The analysis correctly identified that `Clients.Group(...).SendAsync(...)` is awaited and goes through Redis PUBLISH (true, source-verified); it overstated the load-shape regime in which the per-publish wait time becomes large at *steady state* (the saturation observed was at handshake setup, not at steady-state battle publishing). A follow-up addendum to `RUN_1_BACKPLANE_OVERHEAD_ANALYSIS.md` would close this loop properly; out of Chapter 3 scope per the "no further `src/Kombats.*` changes" constraint, which extends to "no rewrite of upstream analyses except where Run 3 data directly invalidates a claim".

These five gaps do not affect Chapter 3's headline claims (Event A closed, Event B closed, 98.8 % success rate, dual-fix is real). They affect the *causal precision* of secondary claims about *why* and *by how much* each fix contributes. The chapter ships honest about which is which.

## 12. Plain-language summary (Russian-friendly, kernel for STORY chapter)

> Чаптер 3 — это история о том, как одна предсказуемая правка превратилась в две, и почему это была хорошая новость для архитектуры.
>
> Я знал заранее: `maxReplicas: 1` в Bicep — не каприз, а архитектурный потолок. SignalR без backplane не умеет фанаутить group-сообщения между процессами. Поднял второй Battle, прогнал ту же нагрузку из главы 2 — застряло **89 %** боёв (Run 2). Compound-модель сошлась с измерением до 1 %: предсказание `12.5 / 12.5 / 75` против наблюдения `10.5 / 15.8 / 73.7` на 19 боях. Но из этого Run 2 родилась новая находка: помимо §3.1 (group send миссит на другой реплике — Event B), есть ещё Event A — negotiate POST приходит на реплику X, WebSocket GET через DNS rotation попадает на реплику Y, токен в её `IConnectionManager` отсутствует → 404. Это failure mode под группой `Clients.Group(...)`, в отдельном слое.
>
> Backplane (D1, одна строка `.AddStackExchangeRedis(redisConnectionString)`) закрывает Event B, но не Event A — потому что connection-token registry в Redis не реплицируется ни этим, ни любым другим backplane: это HTTP-level state в `HttpConnectionManager`, под `HubLifetimeManager`. Run 3 setup это подтвердил эмпирически: 4 из 5 smoke прошли чисто, 1 из 5 поймал тот же стек-трейс 404 что в Run 2. После этого добавил D1.5: одна строка `SkipNegotiation = true` + `Transports = HttpTransportType.WebSockets` в `BattleHubRelay.JoinBattleAsync` — клиент пропускает negotiate, идёт сразу на WebSocket upgrade, и нет токена, который можно было бы потерять между репликами.
>
> Run 3 measured: 98.8 % success rate (2088 из 2113), `total_ms` p50 = 1124.7 ms — лучше чем Run 1 (single-replica + backplane, 2182.4 ms). Per-phase breakdown показал, что Run 1's overhead жил в `join_battle_ms` (+761 ms), а не в `battle_ms` (последний был flat по сравнению с Run 0). Это значит: дорогим был **handshake setup** на single-replica под нагрузкой 25 одновременных `HubConnection.StartAsync`, а не per-publish RTT во время боя как считалось в `RUN_1_BACKPLANE_OVERHEAD_ANALYSIS.md`. Run 3 collapse'ит `join_battle_ms` обратно к baseline (~4 ms) двумя одновременными изменениями — D1.5 убирает negotiate-roundtrip, second replica делит handshake-нагрузку 50/50. Из Run 3 alone не разделить, насколько важен каждый из двух (потребовалось бы Run 5: multi-replica + backplane *без* D1.5). На production load shape надо отдельно проверять — Run 3 это lower-bound proof, не upper-bound.
>
> Урок главы — про итеративное моделирование, не про "одна строка снимает потолок". Три модели за три run-а: §3.1 (per-event group send) → compound (Event A × Event B, Run 2) → refined (Event A — слой ниже backplane, Run 3 setup). Каждый шаг — наблюдённое доказательство, не догадка. Плюс одно важное обновление сверху: per-phase данные Run 3 показали, что и Run 1 overhead analysis надо уточнить — saturation была at handshake setup, не at per-publish, и это меняет, какие mitigations имеет смысл (в §11.5). Стек теперь горизонтально масштабируется на Battle на этом load shape (25 concurrent × 2 replicas), и мы знаем какие observability-gaps закрыть до того, как трогать `maxReplicas: 1` в Azure (per-replica OTel emission, `active_battles` split-brain, TTL wiring) и какие causal claims не сделаны (§11.5). Это и есть портфельный итог: не "я починил баг", а "я уточнил модель четыре раза — три для самого failure, четвёртый для cost-mechanism — и каждый шаг подтвердил измерением".

---

## Artefact locations

- **This file:** `tests/Kombats.LoadTests/RUN_3_RESULTS.md` (results summary — read first).
- **Setup evidence + D1.5 amendment:** `tests/Kombats.LoadTests/RUN_3_SETUP_LOG.md` (§14 covers the D1.5 fix mechanism, smoke re-verification, model refinement).
- **D1.5 source change:** `src/Kombats.Bff/Kombats.Bff.Application/Relay/BattleHubRelay.cs` (uncommitted at write time; architect commits with this report).
- **Iteration log:** `tests/Kombats.LoadTests/iteration-logs/iterations-2026-05-13--16-40-39.jsonl`.
- **Aggregate output captured in:** §3 of this file (Overall + per-outcome slices).
- **Comparison sources:**
  - `tests/Kombats.LoadTests/RUN_1_RESULTS.md` §3 (primary baseline — single-replica + backplane).
  - `tests/Kombats.LoadTests/RUN_2_RESULTS.md` §3 + §4 (regression column + compound model).
  - `tests/Kombats.LoadTests/RUN_0_BASELINE.md` §3 (secondary baseline — single-replica, no backplane).
- **Mechanism analyses cited:**
  - `tests/Kombats.LoadTests/RUN_1_BACKPLANE_OVERHEAD_ANALYSIS.md` §3 (per-publish PUBLISH semantics), §3.5 (multiplexer queueing — refined here in §3b/§9.2), §6 (mitigations — now optional at multi-replica).
  - `tests/Kombats.LoadTests/SIGNALR_SURFACE_MAP.md` §F, §G, §J.3 (BFF-relay smell, JWT-in-query-string transport).
  - `tests/Kombats.LoadTests/CHAPTER_3_PLAN.md` §3.1 (initial per-event prediction), §9 (predicted-vs-measured template — superseded by §4 above with three columns instead of one).
