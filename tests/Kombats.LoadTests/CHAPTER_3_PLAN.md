# Chapter 3 Plan — SignalR Redis Backplane + Multi-Replica Battle

*Drafted 2026-05-12, revised twice 2026-05-12 (initial revisions + closing
pass). Planning artefact only — no source code in this chapter.
Architectural decisions are listed in the handoff. Recon input:
`tests/Kombats.LoadTests/SIGNALR_SURFACE_MAP.md`.*

## 1. Goal and success criteria

This chapter proves that **the `maxReplicas: 1` ceiling on Battle is
removable by a single-line addition of the SignalR Redis backplane**, and
quantifies the failure mode that makes the ceiling load-bearing today. We
run the load test in three configurations — single-replica baseline,
2-replica WITHOUT backplane, 2-replica WITH backplane — and use the gap
between configs 2 and 3 as the proof.

Success criteria (measurable from existing iteration-log + Grafana):

- **2-replica WITHOUT backplane:** ≥70 % of battles end in timeout or
  `DoubleForfeit` outcome (consistent with the per-battle aggregation
  model in §3.1; exact number is the experimental verification of that
  model). `total_ms` p95 hits
  `VirtualPlayer.PerBotTimeout` (`tests/Kombats.LoadTests/VirtualPlayer/VirtualPlayer.cs:43`).
- **2-replica WITH backplane:** stall rate ≤1 %; per-bot `total_ms`
  p50/p95 within ±10 % of single-replica baseline.
- `turn_resolution_duration_milliseconds` p99 regression ≤ +5 ms with
  backplane on single-replica (Redis-hop overhead bound).
- New per-replica Grafana panel (3a) shows non-zero connections on
  **both** Battle replicas — confirms BFF downstreams fan out.
- `0` stuck `mm:player:{guid}` Redis keys after WITH-backplane run vs.
  measurable drift after WITHOUT.

## 2. Architectural baseline (current state)

Distilled from `SIGNALR_SURFACE_MAP.md` A/F/G:

- **3 production hubs across 3 services**: Battle's `BattleHub`
  (`src/Kombats.Battle/.../SignalR/BattleHub.cs:17`), BFF's
  `BattleHub`/`ChatHub` (`src/Kombats.Bff/Kombats.Bff.Api/Hubs/BattleHub.cs:20`,
  `ChatHub.cs:16`), Chat's `InternalChatHub`
  (`src/Kombats.Chat/Kombats.Chat.Api/Hubs/InternalChatHub.cs:24`).
- **BFF is a per-connection relay**: `BattleHubRelay.JoinBattleAsync`
  opens a fresh outbound `HubConnection` to
  `${Services:Battle:BaseUrl}/battlehub` per inbound invocation
  (`src/Kombats.Bff/Kombats.Bff.Application/Relay/BattleHubRelay.cs:46-82`,
  URL at line 55). All server-emitted events fan in to BFF and BFF
  re-sends per-frontend-connection through `HubContextBattleSender`
  (`src/Kombats.Bff/Kombats.Bff.Api/Hubs/HubContextBattleSender.cs:15`).
- **Battle hub is the authoritative event source**: all per-battle
  group sends fire from `SignalRBattleRealtimeNotifier`
  (`src/Kombats.Battle/.../SignalRBattleRealtimeNotifier.cs:44, 59, 76, 93, 140, 170`).

**Why `maxReplicas: 1` is load-bearing.** `Clients.Group(...)` resolves
group membership from an **in-process** map. A group send on replica A
reaches only connections registered with A's local group table; if the
player's downstream is parked on replica B, B's entry is invisible to
A. The Azure Bicep ceiling is the deployment-level acknowledgement of
this — scaling out without a backplane silently breaks the game.

**BFF-relay caveat.** The per-connection relay is architecturally
questionable (doubles connection count; `BattleHubRelay._connections` at
`BattleHubRelay.cs:19` is per-process). We **accept it for Chapter 3** —
smell logged in `SIGNALR_SURFACE_MAP.md` J.3, separate chapter to
address. Concrete consequence: scaling BFF >1 replica would need sticky
sessions because `_connections` is process-local. This chapter keeps BFF
at 1 and scales only Battle.

## 3. Predicted failure modes on 2-replica Battle without backplane

Three predictions, each grounded in a specific code path. Combinatorics
assume `R = 2` Battle replicas and uniform per-connection replica
assignment (justified in §5).

### 3.1 `TurnDeadlineWorker` resolves on the wrong replica

**Mechanism.** Each Battle replica runs its own `TurnDeadlineWorker`
(`src/Kombats.Battle/Kombats.Battle.Bootstrap/Workers/TurnDeadlineWorker.cs:47`).
The worker calls `stateStore.ClaimDueBattlesAsync` (line 56) — Redis
atomic — so exactly one worker resolves each due turn, but **which**
worker is racy. Resolution invokes `SignalRBattleRealtimeNotifier`
(lines 44, 59, 76, 93, 140, 170), each one a
`Clients.Group($"battle:{battleId}")` send hitting only the resolving
replica's local group table.

**Math model.** With `R = 2`, uniform DNS assignment:

- `P(player X's downstream on replica r) = 1/R = 0.5`.
- `P(worker on r wins the claim) ≈ 1/R = 0.5` (uniform; workers poll on
  `IdleDelayMinMs=200, BacklogDelayMs=30`,
  `src/Kombats.Battle/.../appsettings.json:97-99`).
- `P(player X sees the resolved turn) = Σᵣ P(X on r) · P(worker on r) =
  0.5·0.5 + 0.5·0.5 = 0.5`.
- `P(both see) = 0.5² = 0.25` ⇒ **`P(at least one blind) = 0.75`** per
  single event.

The same logic applies to in-band `SubmitTurnAction`: hub-method
resolution runs on the *submitter's* replica, so the other player sees
events with probability 0.5 per submission.

**Per-battle aggregation.** A battle resolves T turns. Per turn,
`P(both players see the resolved event) = 0.25`, so `P(at least one
critical event lost across the battle) ≈ 1 − 0.25^T`. For `T = 3` this
is already 98.4 %. The per-event 75 % figure is the **lower bound**;
the observable in 3b will be closer to "nearly all battles affected on
multi-turn matches".

**Precondition.** This model assumes uniform DNS rotation between
Battle replicas (i.e. successive `HubConnection.StartAsync` calls from
BFF land on different Battle instances). This assumption is currently
**unverified** — see §5. The pre-flight smoke check in §5 must pass
before the §3.1 prediction is valid. If DNS does not rotate (e.g. .NET
caches the first resolved IP), all connections land on one replica and
the experiment shows nothing.

**Observable symptom.** The **vast majority** of battles either stall
or end as `DoubleForfeit`; only the rare short-battle cases that win
the co-location lottery every turn complete normally. Iteration-log
shifts: `battle_ms` null / clamped to `PerBotTimeout` on the bulk of
iterations; `outcome` distribution dominated by `Timeout` and
`DoubleForfeit`; `total_ms` p95 saturates at `PerBotTimeout`.

**Grafana signal.** `active_battles{service="battle"}` (Hot Path panel,
`observability/grafana/dashboards/kombats-overview.json:85-103`) peaks
≥2× baseline and decays slowly post-run (timed-out battles aren't ended).
The new per-replica panel (§7) confirms non-zero connections on both
replicas.

### 3.2 `CreateBattleConsumer` → `BattleReady` / first `TurnOpened`

**Mechanism.** Matchmaking publishes `CreateBattle` via
MassTransit/RabbitMQ in competing-consumer mode; exactly one Battle
replica consumes (`src/Kombats.Battle/.../Consumers/CreateBattleConsumer.cs:78`).
That replica's `BattleLifecycleAppService.HandleBattleCreatedAsync`
fires `NotifyBattleReadyAsync` + `NotifyTurnOpenedAsync` for turn 1
(`src/Kombats.Battle/.../Lifecycle/BattleLifecycleAppService.cs:131-132`),
both `Clients.Group($"battle:{battleId}")` on that replica's local
group.

**Timing nuance.** Players invoke `JoinBattle` only after polling
`/queue/status` → `Matched`. The consumer typically completes (and
emits) *before* either player joins the group, so `BattleReady` lands in
an empty group. The frontend covers this via the snapshot returned by
`JoinBattle` (`SIGNALR_SURFACE_MAP.md:311`) — so `BattleReady` loss is
invisible. But turn-1 `TurnOpened` is broadcast-only; the snapshot
carries `DeadlineUtc` but a player joining mid-flight may have a snapshot
predating the broadcast.

**Math model.** Identical to 3.1 for the broadcast-vs-listener race: per
player, `P(consumer-resolving replica = player's downstream replica) =
0.5`. For `BattleReady`: irrelevant (snapshot covers). For turn-1
`TurnOpened`: 50 % of players miss it; some recover via snapshot
`DeadlineUtc`, others wait.

**Observable symptom.** Higher `wait_first_turn_ms` (gap between
`RunOneBattleAsync` start and first `SubmitTurnAction`). Stale-turnIndex
submits get silently downgraded to `NoAction`
(`src/Kombats.Battle/.../ActionIntakeService.cs:46-49`) — uptick in
`DoubleForfeit` outcomes when both bots are blind to turn 1.

**Grafana signal.** First-turn `turn_resolution_duration` histogram
develops a fat tail. Current dashboard doesn't directly plot first-turn
latency; visible as bucket skew.

### 3.3 `PresenceSweepWorker` (Chat — documented, not exercised)

`SIGNALR_SURFACE_MAP.md` J.1 item 2:
`src/Kombats.Chat/.../PresenceSweepWorker.cs:75` calls
`SignalRChatNotifier.BroadcastPlayerOfflineAsync`
(`src/Kombats.Chat/Kombats.Chat.Api/Hubs/SignalRChatNotifier.cs:20`) —
`Clients.Group("global")` send. Same break as 3.1, but **chat has no
load coverage** (`SIGNALR_SURFACE_MAP.md:261`). We **document and
defer** to a future chat-focused chapter; the same one-line backplane
fix would close it.

## 4. Solution — Redis backplane

**One-line code change.** In
`src/Kombats.Battle/Kombats.Battle.Bootstrap/Program.cs:128-129`, chain
`.AddStackExchangeRedis(connectionString)` onto the existing
`AddSignalR(...).AddJsonProtocol(...)` builder.

**Connection string.** Reuse existing `ConnectionStrings:Redis`
(`src/Kombats.Battle/Kombats.Battle.Bootstrap/appsettings.json:14`,
docker-compose env `ConnectionStrings__Redis=redis:6379,abortConnect=false`).
Same key already used by `IBattleStateStore` and the Redis health check
— no new config key needed.

**Package.** `Microsoft.AspNetCore.SignalR.StackExchangeRedis` — one
`<PackageVersion>` to `Directory.Packages.props` + one
`<PackageReference>` to Battle Bootstrap csproj. No `Kombats.Common`,
Chat, or BFF changes — this chapter touches only Battle.

**What we get "for free":** group membership replicated through Redis
PubSub (so `Clients.Group(...)` on replica A reaches members on B);
`Groups.AddToGroupAsync` (`BattleHub.cs:48`) propagates;
`Clients.Client(connectionId)` works cross-replica too.

**What it does NOT give us:** connection migration on replica death
(player on dead replica loses connection); strict cross-replica message
ordering (Redis PubSub is best-effort); trace-context through SignalR
frames; replacement for existing `BattleStateStore` Redis use (state
and backplane are independent concerns).

**Risk — Redis unreachable mid-test.** **Unresolved.** The
`Microsoft.AspNetCore.SignalR.StackExchangeRedis` package documents
graceful reconnect; whether cross-replica sends queue/drop/error during
an outage is version-specific. Pre-implementation TODO: induce a Redis
disconnect during a smoke run and observe.

## 5. Multi-replica Docker Compose setup

**Adding a second Battle replica.** Two options:

- **(A) `docker compose up --scale battle=2`.** No file edits. Loses the
  explicit host port mapping (`docker-compose.yml:225-226`) because two
  containers can't bind one host port — fine, bots reach Battle via
  BFF only.
- **(B) Add `battle-2` as a named service** cloning the existing
  `battle` block, with `aliases: [battle]` so DNS still resolves
  `battle` to both. Slightly more verbose; logs are deterministic.

Recommendation: **(B)** for log-grep determinism (q12.2).

**Networking for bots.** Bots target `http://localhost:5000`
(`tests/Kombats.LoadTests/appsettings.json:3`,
`tests/Kombats.LoadTests/Configuration/LoadTestOptions.cs:13`) — i.e.
BFF, never Battle direct. Host port collisions on Battle are
irrelevant.

**BFF → Battle routing.** BFF builds the Battle hub URL per inbound
`JoinBattle` (`BattleHubRelay.cs:55`) from `Services:Battle:BaseUrl =
http://battle:8080`. Docker Compose's embedded DNS returns all matching
container IPs and rotates the order per lookup, so each new
`HubConnection.StartAsync` in `BattleHubRelay.JoinBattleAsync` is
expected to land on a different replica.

**Co-location nuance.** The established WebSocket is **pinned** to its
replica until disconnect. With `R = 2`, `P(both bots' downstreams on
the same replica) = 0.5` — half of WITHOUT-backplane battles
accidentally co-locate at turn 1. That dilution is already absorbed by
the §3.1 per-battle aggregation: any deadline resolved by the
cross-replica worker still misses everyone, so co-locating at turn 1
only delays the divergence by a few turns.

**Step 0 of 3b — DNS rotation pre-flight (mandatory before any load
test run).** From inside the BFF container, hit
`http://battle:8080/health/ready` ten times (`docker compose exec bff
sh -c 'for i in $(seq 1 10); do curl -sf http://battle:8080/health/ready;
echo; done'`) and cross-reference responses with each replica's logged
`service.instance.id` at startup. If `service_instance_id` does **not**
vary across the 10 hits, **stop** — the
math model in §3.1 is invalidated and the experiment design must be
revisited. Plan-B options (out of Chapter 3 scope unless the pre-flight
forces them): set `SocketsHttpHandler.PooledConnectionLifetime` to
force per-connection DNS refresh; or use explicit per-replica hostnames
(`battle-1`, `battle-2`) with BFF round-robin selection in
`BattleHubRelay.JoinBattleAsync`.

## 6. BFF horizontal scaling — out of scope, rationale

Chapter 3 keeps BFF at a single replica. This section documents **why**
that's a deliberate scope choice and what a future chapter would need.

`BattleHubRelay._connections` is process-local
(`BattleHubRelay.cs:19`). A frontend that reconnects must hit the same
BFF process or its downstream is orphaned. Three options for handling
this if BFF were scaled:

- **(a) Stay at one BFF.** No sticky sessions needed.
- (b) nginx/Traefik with `ip_hash` / cookie affinity. Real infra work;
  only useful when BFF is actually scaled — premature.
- (c) Service mesh (Consul/Caddy in front of Compose). Out of scope.

**Decision: BFF stays single-replica in Chapter 3.** The cross-replica
Battle failure mode exists regardless of BFF replica count, so
single-BFF + multi-Battle is sufficient to prove the backplane thesis.
BFF horizontal scaling — with whichever of (b)/(c) — is a separate
"BFF scale-out" chapter.

## 7. Pre-work — per-replica Grafana panel (3a)

The current panel
(`observability/grafana/dashboards/kombats-overview.json:85-110`) uses
`sum by (service) (active_signalr_connections)` (line 103). Aggregation
masks per-replica distribution — exactly what we need to interpret 3b.

**New panel PromQL** (add as a second panel under "Hot Path"):

```
active_signalr_connections{service="battle"}
```

Returns one series per `(service, service_instance_id)`. Legend
`{{service_instance_id}}` — two curves on 2-replica, one on
single-replica. Keep the aggregated panel; add a new block adjacent
(around line 85-110) with its own `id` and `gridPos`.

**Sanity check before 3a lands.** Confirm `service.instance.id` reaches
Prometheus labels. OTel collector has `resource_to_telemetry_conversion:
enabled: true` (`observability/otel-collector/config.yaml:44`), so
resource attributes become labels (`.`→`_`). Verify:

```
curl -s 'http://localhost:9090/api/v1/series?match[]=active_signalr_connections' | jq .
```

If `service_instance_id` is absent (the .NET OTel SDK doesn't always
auto-populate it), the fallback is to set it explicitly via
`OTEL_RESOURCE_ATTRIBUTES=service.instance.id=$HOSTNAME` per service in
docker-compose. **Unresolved — verify before 3a.**

## 8. Sanity checks — what must NOT change

If any of these regress, the backplane is doing more than it should:

- **Single-replica baseline.** With backplane enabled on `R = 1`:
  `queue_wait_ms` p50 within ±50 ms of Ch2 post-fix (≈1018 ms);
  `total_ms` p50 unchanged within noise.
- **Turn resolution latency.** `turn_resolution_duration_milliseconds`
  p99 ≤ +5 ms vs. baseline. Predicted overhead: ~1-2 ms per Redis
  PubSub publish (loopback Redis); histogram is measured *inside*
  `ResolveTurnAsync` before the notifier fires, so should be untouched
  — verify.
- **`active_battles` correctness.** Decrement on battle end unchanged
  (logic lives in `BattleLifecycleAppService`, independent of notifier).
- **No new WARN+ log lines** in Battle during a clean run. Backplane
  startup INFO once is expected.
- **MassTransit consume counters** unchanged: backplane doesn't go
  through MassTransit.

### §8 verification runs

Two of the five load test runs in §13 exist specifically to validate
the invariants above:

- **Run #1 (single-replica WITH backplane)** validates the "turn
  resolution latency" and "single-replica baseline" invariants. Result
  recorded inline at the end of §8 as a short table: each invariant,
  baseline value, run #1 measured value, ±delta.
- **Run #4 (matchmaking smoke regression)** validates the "MassTransit
  consume counters unchanged" invariant and re-runs the Ch2
  matchmaking phase breakdown to confirm no regression. Result
  recorded inline as a short table: phase, Ch2 number, run #4 number,
  ±delta.

Format for both: simple markdown table with three columns
(invariant/phase, baseline, measured, delta). No separate report file
— the closing of §8 in the Chapter 3 write-up is where these land.

## 9. Predicted vs measured table (template — fill during 3b/3c)

| Metric | Single-replica baseline | 2-rep WITHOUT (predicted) | 2-rep WITHOUT (measured) | 2-rep WITH (predicted) | 2-rep WITH (measured) | Notes |
|---|---|---|---|---|---|---|
| `queue_wait_ms` p50 | ≈1018 ms (Ch2) | unchanged | | unchanged | | Matchmaking is single-replica |
| `queue_wait_ms` p95 | TBD | unchanged | | unchanged | | |
| `total_ms` p50 | TBD | ≥ PerBotTimeout on ≥50 % | | ±10 % of baseline | | |
| `total_ms` p95 | TBD | = PerBotTimeout | | ±10 % of baseline | | |
| `ok %` | ≈84 % (Ch1 post-heartbeat) | ≤50 % | | ≥80 % | | |
| `fail` count | Ch2 number | ≥2× baseline | | ±20 % of baseline | | |
| stuck battles post-run | 0 | ≥10/25 pairs | | 0 | | `mm:player:{guid}` keys after expected cleanup |
| `turn_resolution_duration` p50 | TBD | unchanged | | +0..+2 ms | | |
| `turn_resolution_duration` p99 | TBD | unchanged | | +0..+5 ms | | |
| RPS (battles/s) | TBD | drops as timeouts pile up | | unchanged | | |
| Battles / 2 min | Ch2 number | ≤50 % of baseline | | ±10 % of baseline | | |
| `active_battles` peak | ~3 (Ch1) | ≥2× baseline | | same as baseline | | |
| Conn split per Battle replica | n/a | ~50/50 | | ~50/50 | | New panel from §7 |

## 10. Plain-language summary (Russian — kernel for STORY chapter)

Я знал заранее, что `maxReplicas: 1` в Bicep — не каприз, а
архитектурный потолок. SignalR без backplane не умеет фанаутить
group-сообщения между процессами: если игрок А подключён к реплике 1,
а сервер резолвит ход на реплике 2, group send уходит в пустоту —
коннект игрока живёт только в in-process group table реплики 1. Чтобы
это доказать, я поднял второй Battle в docker compose и прогнал
сценарий из главы 2. Получил ту самую картину: подавляющее большинство
боёв застряли — `TurnDeadlineWorker` на одной реплике, коннекты на
другой, события в никуда. Затем добавил одну строчку:
`.AddStackExchangeRedis(connectionString)` к существующему
`AddSignalR()`. Тест снова — застрявших нет, метрики совпадают с
single-replica baseline. Это и есть шаг, превращающий single-instance
demo в horizontally scalable архитектуру. Одна строка кода — потолок
снят. Урок: backplane — не "оптимизация на потом", а **условие** для
multi-replica SignalR, и без реального теста с двумя репликами этот
факт остаётся абстракцией из документации.

## 11. Things I am deliberately NOT doing in this chapter

- **Chat backplane.** No load coverage of chat; same fix would close
  same failure mode there. Deferred to a chat-focused chapter.
- **SignalR distributed-trace propagation.** Real issue, orthogonal to
  backplane; separate chapter.
- **Removing BFF-relay architecture.** Real smell (§2,
  `SIGNALR_SURFACE_MAP.md` J.3), but multi-week rewrite — decoupled.
- **Cross-replica connection migration on replica death.** Backplane
  doesn't solve it; needs sticky sessions + reconnect logic.
- **Multi-replica BFF + sticky sessions.** See §6; out of scope.
- **Push-based match-found notification** (item 3d from handoff).
  Orthogonal.
- **NBomber 50-scenario ceiling.** Candidate for Ch4.
- **Lifting Bicep `maxReplicas: 1` in Azure.** Ch5+ — separate change
  after dev validation.
- **Other observability gaps** from `SIGNALR_SURFACE_MAP.md` J.3 (BFF
  `ChatHub` connection counter, message-send counters). Deferred unless
  3b proves we need them.

## 12. Resolved decisions

Decisions made by the architect after plan review. Q8 and Q9 were
resolved during the revision pass and remain here for completeness.

1. **Battle replicas: 2 or 3?** **Decision: 2.** Cleaner math
   (`1 − 0.25^T`); doubling beyond 2 doubles local memory/CPU without
   adding insight.
2. **`--scale battle=2` vs. named-service `battle-2`?** **Decision:
   named-service (`battle-1` / `battle-2`).** Deterministic logs for
   grep-based analysis; `--scale` produces random suffixes.
3. **Re-run Chapter 2 matchmaking smoke as regression after backplane
   lands?** **Decision: yes, after 3c.** Backplane shouldn't touch
   matchmaking (no SignalR in pairing path); cheap to prove. Recorded
   in §8 verification runs subsection.
4. **DNS-rotation pre-flight check (§5).** **Decision: manual run +
   save as reusable script** at
   `tests/Kombats.LoadTests/scripts/dns-rotation-check.sh`. 5 min to
   write, reusable in Ch4 if we go to 3 replicas, citable from STORY.
5. **`service.instance.id` to Prometheus (§7).** **Decision: verify
   first, fallback if needed.** Run the §7 probe as 3a's first step.
   If absent, add `OTEL_RESOURCE_ATTRIBUTES=service.instance.id=$HOSTNAME`
   per-service in docker-compose.
6. **Backplane connection string source.** **Decision: reuse
   `ConnectionStrings:Redis` + code comment.** Single Redis instance
   fine at our load; comment near `.AddStackExchangeRedis(...)` flags
   the split-to-dedicated-Redis option for future chapters if ops/sec
   saturates.
7. **`AddStackExchangeRedis` channel prefix.** **Decision: default,
   no override.** No multi-tenancy or environment sharing — defaults
   are safe.
8. **Implementation workflow for 3b vs 3c.** **Decision: branch-based,
   no feature flag.** Feature branch `feat/signalr-backplane` carries
   the one-line backplane addition; 3b runs from `main`, 3c from the
   branch. Matches Ch2 lease-fix workflow. Flags added "temporarily
   for testing" tend to leak into production.
9. **`PerBotTimeout` for 3b WITHOUT run.** **Decision: keep at 2 min
   on both runs.** `total_ms` distributions become directly comparable
   in §9's table; the ≈+1 h of test wall-clock is worth clean
   before/after comparability.
10. **Re-review plan after 3a Grafana panel lands?** **Decision: no.**
    Panel is additive; doesn't change experiment design.

## 13. Execution plan — load test run order

Five load test runs constitute the experimental work of Chapter 3,
ordered to: establish baseline, verify the backplane doesn't regress
single-replica behavior, prove the multi-replica failure mode, prove
the fix, confirm no matchmaking collateral. Runs 2 and 3 are the
before/after comparison the chapter exists to publish; the others
validate the experiment design.

| # | Configuration | Branch | Purpose | Expected result | Recorded in §9? |
|---|---|---|---|---|---|
| 0 | Single-replica Battle, no backplane | `main` | Re-establish Ch2 baseline as Ch3 starting point | 9.44 ticks/s pairing throughput, ≈1051 battles / 2 min, queue_wait p50 ≈1018 ms | Yes — "Single-replica baseline" column |
| 1 | Single-replica Battle, **WITH** backplane | `feat/signalr-backplane` | Sanity — backplane doesn't regress single-replica | All metrics within ±10 % of run 0; turn_resolution p99 within +5 ms (see §8) | No — fold into §8 verification runs subsection |
| 2 | **2-replica Battle**, no backplane | `main` | Prove the multi-replica ceiling exists; verify §3.1 model | ≥70 % battles in timeout / DoubleForfeit; both Battle replicas show non-zero connections in per-replica Grafana panel | Yes — "2-rep WITHOUT" column |
| 3 | **2-replica Battle**, **WITH** backplane | `feat/signalr-backplane` | Prove the fix | ≤1 % stuck battles; all metrics within ±10 % of run 0; turn_resolution p99 within +5 ms | Yes — "2-rep WITH" column |
| 4 | Matchmaking smoke (single-replica everything), backplane on | `feat/signalr-backplane` | Regression check — backplane didn't accidentally touch matchmaking | 9.44 ticks/s as in Ch2, all matchmaking phase breakdowns within ±20 % | No — fold into §8 verification runs subsection |

**Pre-flight (Step 0 of every multi-replica run).** Before runs 2 and
3, execute `tests/Kombats.LoadTests/scripts/dns-rotation-check.sh`
(Q4). On failure — stop, do not run. See §5 stop-condition.

### Mandatory teardown before each measurement run

Run 0 demonstrated — and `CLEANUP_WORKER_DIAGNOSIS.md` explained — that
Redis `battle:state:*` keys accumulate without TTL because
`BattleRedisOptions.StateTtlAfterEnd` is unwired. Under sustained
25-pair concurrency this state degrades throughput by ~3× starting
~60s into a 2-minute test, with no error signal except monotonic decay.
The fix (wiring the option through `RedisBattleStateStore`) is Chapter
2.5 territory — out of scope here. Until then, every measurement run
(#0–#4) MUST be preceded by:

```bash
docker compose -f docker-compose.yml \
  -f observability/docker-compose.observability.yml \
  -f observability/docker-compose.observability.override.yml \
  down -v
docker compose -f docker-compose.yml \
  -f observability/docker-compose.observability.yml \
  -f observability/docker-compose.observability.override.yml \
  up -d --build
# Apply EF migrations BEFORE app services try to read the schema (AD-13:
# services do not auto-migrate on startup — see Program.cs in each
# Bootstrap project). The script polls pg_isready internally, so it's
# safe to invoke immediately after `up -d`. Then bounce the five backend
# services to clear any DB-error retry backoff they entered while the
# schema was still missing.
POSTGRES_HOST=localhost POSTGRES_PORT=5432 POSTGRES_DB=kombats \
  POSTGRES_USER=postgres POSTGRES_PASSWORD=postgres \
  KEYCLOAK_DB_PASSWORD=keycloak \
  ./scripts/run-migrations.sh
docker compose -f docker-compose.yml \
  -f observability/docker-compose.observability.yml \
  -f observability/docker-compose.observability.override.yml \
  restart bff players matchmaking battle chat
# Wait ~60s for all services healthy; verify keycloak-bootstrap exited 0
cd tests/Kombats.LoadTests
dotnet run -- seed-users
dotnet run -- single-bot  # optional smoke
dotnet run -- load        # the actual measurement run
```

Why the migration step is mandatory: per AD-13 (cited verbatim in every
`Bootstrap/Program.cs` — Players, Matchmaking, Battle, Chat), services
do **not** call `Database.MigrateAsync()` on startup. Migrations are
applied by an external runner (`scripts/run-migrations.sh` locally; a
Container Apps Job in Azure — see `infra/modules/migrator-job.bicep`).
After `down -v` wipes the Postgres volume, app services come up against
an empty schema and crash on first DB hit (`relation "players.characters"
does not exist`). EF/MassTransit retry policies do not always recover
even after migrations land later, so the explicit `restart` of the five
backend services is part of the discipline — empirically verified in
Phase II (see `PHASE_2_REPORT.md` §4 question 1).

References: `RUN_0_BASELINE.md` for the empirical demonstration,
`RUN_0_DRIFT_INVESTIGATION.md` for the throughput-decay diagnostic,
`CLEANUP_WORKER_DIAGNOSIS.md` for the root cause,
`PHASE_2_REPORT.md` for the migration-step discovery.

**Branch-switching workflow** (per Q8):
- Run 0: Full teardown sequence (above) on `main`.
- Run 1: Full teardown sequence (above), then `git checkout feat/signalr-backplane`, rebuild Battle only.
- Run 2: Full teardown sequence (above) on `main`, restart with 2 Battle replicas (Q2).
- Run 3: Full teardown sequence (above), then `git checkout feat/signalr-backplane`, rebuild Battle (still 2 replicas).
- Run 4: Full teardown sequence (above) on `feat/signalr-backplane`, scale Battle to 1, run matchmaking smoke scenario.

**Wall-clock estimate.** Each run is 2 min plus ≈3-5 min rebuild plus
~3 min teardown + clean rebuild cycle plus ~1 min for migrations +
service restart; five runs ≈ 55-75 min of real time plus analysis.
Plan half a day for the experimental phase, excluding 3a (Grafana
panel) and the backplane code itself.
