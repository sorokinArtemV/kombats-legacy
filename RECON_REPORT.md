# Kombats — Backend Reconnaissance Report

Target: 2,000 concurrent players / 1,000 parallel battles load test.
All citations are `path/to/file:line` against the working tree on branch `main` (HEAD `f2432d9`).
.NET 10 monorepo (`global.json:3` pins SDK `10.0.100`; `Directory.Build.props:4` sets `TargetFramework=net10.0`).

---

## Section 1 — Service Topology

There are **5 backend services** (`src/Kombats.*` excluding `Kombats.Common`, `Kombats.Client`, `Kombats.Migrator`) plus the React SPA (`src/Kombats.Client/`, out of scope) and a one-shot migrator job (`src/Kombats.Migrator/`).

Every backend service is an ASP.NET Core `WebApplication.CreateBuilder(args)` host. Workers run as `IHostedService` in-process inside the same WebApi process — there is no standalone Worker service. All services target `net10.0` (inherited from `Directory.Build.props:4`).

### 1.1 — Kombats.Bff (Backend-for-Frontend)

- **Root**: `src/Kombats.Bff/` — `Kombats.Bff.Api`, `Kombats.Bff.Application`, `Kombats.Bff.Bootstrap`.
- **Purpose** (from class names + namespace + `src/Kombats.Bff/Kombats.Bff.Bootstrap/Program.cs:362–367`): frontend-facing aggregation layer; relays SignalR traffic to Battle and Chat, fans HTTP to Players/Matchmaking/Battle/Chat. AD-16/AD-17 prohibit it from referencing the shared `Kombats.Abstractions.Auth` package; it inlines its own JWT setup (`Program.cs:27–58`).
- **.NET version**: `net10.0` (inherited).
- **Hosting model**: WebAPI + 2 SignalR hubs (`Program.cs:363, 366`). No `BackgroundService`.
- **Inbound**: HTTP REST (Minimal API endpoints registered via `AddEndpoints(apiAssembly)` `Program.cs:64`); SignalR Hubs `/battlehub` (`Program.cs:363`) and `/chathub` (`Program.cs:366`).
- **Outbound**:
  - HTTP typed clients to Players (`Program.cs:171`), Matchmaking (`Program.cs:209`), Battle (`Program.cs:247`), Chat (`Program.cs:281`) — each wrapped with Polly resilience (`Program.cs:178–206, 216–244, 254–278, 288–312`).
  - Outbound SignalR (`Microsoft.AspNetCore.SignalR.Client`) to Battle `/battlehub` (`src/Kombats.Bff/Kombats.Bff.Application/Relay/BattleHubRelay.cs:51, 57–78`) and Chat `/chathub-internal` (`src/Kombats.Bff/Kombats.Bff.Application/Relay/ChatHubRelay.cs`).
  - No DB, no Redis, no RabbitMQ (no `AddDbContext`/`ConnectionMultiplexer`/`AddMessaging` calls in `Program.cs`).
- **Auth**: JWT validated locally with cached JWKS via Keycloak Authority (`Program.cs:33–58`). Token is also accepted in query string `access_token` for `/battlehub` and `/chathub` (`Program.cs:46–53`).

### 1.2 — Kombats.Players

- **Root**: `src/Kombats.Players/` — Api, Application, Bootstrap, Contracts, Domain, Infrastructure.
- **Purpose** (`docs/frontend/...` references and namespace): player profile, character, leveling, XP accrual.
- **.NET version**: `net10.0`.
- **Hosting model**: WebAPI (no hosted services registered in `Program.cs`).
- **Inbound**: HTTP REST (Minimal API; `Program.cs:44`). RabbitMQ consumer `BattleCompletedConsumer` (`Program.cs:139`).
- **Outbound**: Postgres via `PlayersDbContext` (`Program.cs:98–108`). RabbitMQ publisher for `PlayerCombatProfileChanged` via `MassTransitCombatProfilePublisher` (`Program.cs:90`). No Redis. No outbound HTTP to siblings.
- **Auth**: `AddKombatsAuth` (`Program.cs:38`) — shared Keycloak JWT.

### 1.3 — Kombats.Matchmaking

- **Root**: `src/Kombats.Matchmaking/` — Api, Application, Bootstrap, Contracts, Domain, Infrastructure.
- **Purpose**: queue join/leave, pairing, match→battle hand-off.
- **.NET version**: `net10.0`.
- **Hosting model**: WebAPI + 3 BackgroundServices: `MatchmakingPairingWorker` (`Program.cs:179`), `MatchTimeoutWorker` (`Program.cs:180`), `QueuePresenceSweepWorker` (`Program.cs:181`).
- **Inbound**: HTTP REST. RabbitMQ consumers `PlayerCombatProfileChangedConsumer`, `BattleCreatedConsumer`, `BattleCompletedConsumer` (`Program.cs:142–144`).
- **Outbound**: Postgres via `MatchmakingDbContext` (`Program.cs:88–97`). Redis (DB 1 — `MatchmakingRedisOptions.cs:14`, `appsettings.json:22`) via `StackExchange.Redis` (`Program.cs:108`). RabbitMQ publish `CreateBattle` via `MassTransitCreateBattlePublisher` (`Program.cs:102`).
- **Auth**: `AddKombatsAuth` (`Program.cs:40`).

### 1.4 — Kombats.Battle

- **Root**: `src/Kombats.Battle/` — Api, Application, Bootstrap, Contracts, Domain, Infrastructure, Realtime.Contracts.
- **Purpose**: in-progress battle state, turn resolution engine, SignalR battle hub, recovery.
- **.NET version**: `net10.0`.
- **Hosting model**: WebAPI + SignalR Hub + 2 BackgroundServices: `TurnDeadlineWorker` (`Program.cs:175`), `BattleRecoveryWorker` (`Program.cs:179`).
- **Inbound**: HTTP REST. SignalR Hub `/battlehub` (`Program.cs:200`) — `BattleHub` is `[Authorize]` (`src/Kombats.Battle/Kombats.Battle.Infrastructure/Realtime/SignalR/BattleHub.cs:15–16`). RabbitMQ consumers `CreateBattleConsumer`, `BattleCompletedProjectionConsumer` (`Program.cs:138–139`).
- **Outbound**: Postgres (`BattleDbContext` `Program.cs:87`). Redis (DB 0 — no `defaultDatabase=` set; `Program.cs:99–102`) as **primary store** for live battle state (`RedisBattleStateStore`). RabbitMQ publish `BattleCreated`, `BattleCompleted` via `MassTransitBattleEventPublisher` (`Program.cs:123`).
- **Auth**: `AddKombatsAuth` (`Program.cs:39`). SignalR token-in-query-string accepted by JWT bearer for hub paths (the shared `AddKombatsAuth` configures the OnMessageReceived handler — see Section 5).

### 1.5 — Kombats.Chat

- **Root**: `src/Kombats.Chat/` — Api, Application, Bootstrap, Contracts, Domain, Infrastructure.
- **Purpose**: global chat + DMs, presence, rate-limited messaging.
- **.NET version**: `net10.0`.
- **Hosting model**: WebAPI + SignalR Hub + 2 BackgroundServices: `MessageRetentionWorker` (`Program.cs:253`), `PresenceSweepWorker` (`Program.cs:254`).
- **Inbound**: HTTP REST. SignalR Hub `/chathub-internal` (`Program.cs:275`). RabbitMQ consumer `PlayerCombatProfileChangedConsumer` (`Program.cs:245`).
- **Outbound**: Postgres (`ChatDbContext` `Program.cs:156`). Redis (DB 2 — hardcoded in `src/Kombats.Chat/Kombats.Chat.Infrastructure/Redis/RedisPresenceStore.cs:61`). Outbound HTTP to Players (`Program.cs:193–199`). RabbitMQ — consumer only, no publishers in registrations.
- **Auth**: `AddKombatsAuth` (`Program.cs:42`).

### 1.6 — Topology Diagram

```
                          ┌──────────────────────────┐
                          │  Browser (React SPA)     │
                          │  src/Kombats.Client/     │
                          └────────────┬─────────────┘
                                       │ HTTPS REST + WebSocket (SignalR)
                                       │ JWT (Keycloak OIDC, local validation via JWKS)
                                       ▼
                          ┌──────────────────────────┐
                          │  Kombats.Bff             │
                          │  (no DB / no Redis / no  │
                          │   Rabbit; HTTP+SignalR   │
                          │   relay)                 │
                          └──┬────┬──────┬─────┬─────┘
                  HTTP/JWT   │    │      │     │   HTTP/JWT
        ┌──────────┐         │    │      │     │
        ▼          │         │    │      │     │
 ┌─────────────┐   │   ┌─────▼──┐ │      │     │
 │ Players API │   │   │Matchmkg│ │      │     │
 │  (HTTP)     │   │   │   API  │ │      │     │
 └──┬──────────┘   │   └──┬─────┘ │      │     │
    │SQL           │      │SQL    │SignalR│    │
    │              │      │Redis(1)│ Client│    │
    ▼              │      ▼      │       ▼     │
 ┌────────┐        │   ┌────────┐│   ┌────────┐│
 │Postgres│◄───────┼───┤Postgres││   │ Battle ││
 │schema  │ AMQP   │AMQP┤schema ││   │  API   ││
 │players │◄──┐    │   │matchmk ││   │ +Hub   ││
 └────────┘   │    │   └────────┘│   └──┬─────┘│
              │    │             │     SQL│Redis(0)
              │    │             │     ▼    ▼
              │    │             │ ┌────────┐ ┌─────┐
              │    │             │ │Postgres│ │Redis│
              │    │             │ │schema  │ │DB 0 │
              │    │             │ │battle  │ │(live│
              │    │             │ └────────┘ │state│
              │    │             │            │+lock│
              │    │             │            │+ZSET│
              │    │  ┌──────────▼─┐          └─────┘
              │    │  │ Chat API+  │
              │    │  │   Hub      │
              │    │  └──┬───┬─────┘
              │    │     │   │
              │    │  SQL│   │HTTP→Players
              │    │     ▼   │
              │    │  ┌────┐ │
              │    │  │PG  │ │
              │    │  │chat│ │
              │    │  └────┘ │
              │    │         └─Redis (DB 2)─►┌─────┐
              │    │                         │Redis│
              │    │                         │chat │
              │    │                         └─────┘
              │    └────────────────┐
              │                     ▼
              │       ┌──────────────────────┐
              └──────►│ RabbitMQ (MassTransit│
        AMQP         │  outbox/inbox, fanout │
        from all 4   │  exchanges)           │
        services     └──────────────────────┘

                  ┌───────────────────┐
        OIDC (JWT)│ Keycloak realm    │  realm: "kombats", audience: "kombats-api"
        ◄─────────┤ infra/keycloak/   │  accessTokenLifespan: 3600s
                  └───────────────────┘
```

Edge protocols:

| From | To | Protocol |
|---|---|---|
| Browser | Bff | HTTPS REST + WebSocket (SignalR) |
| Bff | Players / Matchmaking / Battle / Chat (REST endpoints) | HTTP (JWT forwarded) |
| Bff | Battle hub | SignalR (client lib, per frontend conn) |
| Bff | Chat hub | SignalR (client lib, per frontend conn) |
| Players → Postgres `players` schema | SQL (Npgsql) |
| Matchmaking → Postgres `matchmaking` schema | SQL |
| Battle → Postgres `battle` schema | SQL |
| Chat → Postgres `chat` schema | SQL |
| Matchmaking → Redis DB 1 | RESP |
| Battle → Redis DB 0 | RESP |
| Chat → Redis DB 2 | RESP |
| Players, Matchmaking, Battle, Chat ↔ RabbitMQ | AMQP (MassTransit) |
| All services ← Keycloak | HTTPS (JWKS discovery once, cached) |

Note: single shared Postgres instance, schema-per-service; single shared Redis instance, **logical-DB-per-service** (verified Section 4); single shared RabbitMQ broker.

---

## Section 2 — The Battle Hot Path

### 2.1 — SignalR Hubs

**Two `Hub` classes are involved**, plus three `IHubContext` senders/relays:

- `src/Kombats.Bff/Kombats.Bff.Api/Hubs/BattleHub.cs:19` — `BattleHub : Hub`, registered at `/battlehub` (`src/Kombats.Bff/Kombats.Bff.Bootstrap/Program.cs:363`). Public methods:
  - `Task<object> JoinBattle(Guid battleId)` (`BattleHub.cs:23`)
  - `Task SubmitTurnAction(Guid battleId, int turnIndex, string actionPayload)` (`BattleHub.cs:41`)
  - `override OnDisconnectedAsync` (`BattleHub.cs:55`)
  - This hub is a thin relay — it delegates to `IBattleHubRelay` (`BattleHub.cs:32–37, 47–52`) which proxies through a per-connection downstream SignalR Client to Battle.
- `src/Kombats.Battle/Kombats.Battle.Infrastructure/Realtime/SignalR/BattleHub.cs:16` — `BattleHub : Hub`, registered at `/battlehub` in Battle service (`src/Kombats.Battle/Kombats.Battle.Bootstrap/Program.cs:200`). Public methods:
  - `Task<BattleSnapshotRealtime> JoinBattle(Guid battleId)` (`BattleHub.cs:32`)
  - `Task SubmitTurnAction(Guid battleId, int turnIndex, string actionPayload)` (`BattleHub.cs:56`)
  - `override OnDisconnectedAsync` (`BattleHub.cs:72`)

There is also a Chat hub `src/Kombats.Bff/Kombats.Bff.Api/Hubs/ChatHub.cs` and `src/Kombats.Chat/Kombats.Chat.Api/Hubs/InternalChatHub.cs`, but they are not on the battle hot path.

No `[HubMethodName]` attributes — method names map 1:1.

### 2.2 — Connection → Battle Mapping

Two layers:

- **Battle service** uses SignalR Groups only: `Groups.AddToGroupAsync(Context.ConnectionId, $"battle:{battleId}")` (`src/Kombats.Battle/Kombats.Battle.Infrastructure/Realtime/SignalR/BattleHub.cs:38`). Broadcasts target `Clients.Group($"battle:{battleId}")` (Section 2.6). **There is no auxiliary `ConcurrentDictionary` or Redis map of connectionId↔battleId in Battle.**
- **BFF** keeps an **in-memory `ConcurrentDictionary<string, BattleConnection>`** keyed by frontend connection id, with the value holding a downstream `HubConnection` to Battle's `/battlehub`: `src/Kombats.Bff/Kombats.Bff.Application/Relay/BattleHubRelay.cs:18`. This is a process-local map — replicating BFF would break it (see Red Flags).

### 2.3 — SignalR Backplane

**NOT FOUND**. Grep over the entire `src/` for `AddStackExchangeRedis`, `AddRedisBackplane`, `AddAzureSignalR` returns 0 matches (verified). Battle calls `AddSignalR(...).AddJsonProtocol(...)` only (`src/Kombats.Battle/Kombats.Battle.Bootstrap/Program.cs:129–130`); BFF likewise (`src/Kombats.Bff/Kombats.Bff.Bootstrap/Program.cs:315–319`). With more than one replica of either Battle or BFF, the broadcast layer would partition.

### 2.4 — Battle State Storage

**Live battle state is in Redis as the primary store.** Postgres holds only the lifecycle row and append-only turn history.

`RedisBattleStateStore` (`src/Kombats.Battle/Kombats.Battle.Infrastructure/State/Redis/RedisBattleStateStore.cs`) drives Redis writes. Key patterns (all in DB 0 — see Section 4):

| Key | Type | Purpose | TTL | File:line |
|---|---|---|---|---|
| `battle:state:{battleId}` | String (JSON of `BattleState`) | Source of truth for an in-progress fight | none while alive; `StateTtlAfterEnd` is `null` so persists after end | `RedisBattleStateStore.cs:28` |
| `battle:action:{battleId}:turn:{turnIndex}:player:{playerId}` | String | Submitted action payload | `BattleRedisOptions.ActionTtl` = 12h (`BattleRedisOptions.cs:14`, `appsettings.json:41`) | `RedisBattleStateStore.cs:64`, `:396` |
| `battle:turn:{battleId}:{turnIndex}:submitted` | Hash, fields `A`/`B` | "Both submitted?" CAS marker | inherits `ActionTtl` via Lua `EXPIRE` | `RedisBattleStateStore.cs:66, 307` (Lua) |
| `battle:active` | Set of `battleId` | Recovery worker scan set | none | `RedisBattleStateStore.cs:30` |
| `battle:deadlines` | Sorted set, score = deadline unix-ms | Drives `TurnDeadlineWorker` | none | `RedisBattleStateStore.cs:31` |
| `lock:battle:{battleId}:turn:{turnIndex}` | String (lease token) | Single-resolver lock per turn | `TurnDeadlineWorkerOptions.ClaimLeaseTtl` = 12s (`appsettings.json:97`) | `RedisBattleStateStore.cs:65, 245` |

The serialized `BattleState` JSON contains Phase (enum 0..3), TurnIndex, LastResolvedTurnIndex, DeadlineUnixMs, PlayerA/B Hp, NoActionStreakBoth, Version, plus terminal fields (EndWinnerPlayerId, EndReason, EndFinalTurnIndex, EndedAtUnixMs).

Postgres rows during a battle (Section 4 has the full schema): `battles` (1 insert at create, updates on end), `battle_turns` (1 insert per turn, best-effort via `BattleTurnHistoryStore.PersistTurnAsync`).

### 2.5 — Writes per Turn

Per `SubmitTurnAction` from one player when the **opponent has not yet submitted**:

1. Redis: Lua script `StoreActionAndCheckBothSubmitted` — `SET` action key + `HSET` submission marker + `EXPIRE` + return `{alreadySubmitted, bothSubmitted, wasStored}` (`BattleTurnAppService.cs:376`; script body in `RedisScripts.cs:279–320`).
2. No DB writes, no message publish, no broadcast (turn is not resolved yet).

Per `SubmitTurnAction` from the **second** player (`bothSubmitted=true`), `ResolveTurnAsync` runs:

3. Redis: Lua `TryMarkTurnResolving` — CAS from `TurnOpen` → `Resolving` (`BattleTurnAppService.cs:172`; script `RedisScripts.cs:60–75`).
4. Redis: 2× `StringGetAsync` to read both action payloads (`BattleTurnAppService.cs:270`).
5. CPU only: `BattleEngine.ResolveTurn` — pure deterministic resolution, no I/O (`src/Kombats.Battle/Kombats.Battle.Domain/Engine/BattleEngine.cs:14–289`; seed from turn index per `DeterministicTurnRng.Create`).
6. Redis: Lua `MarkTurnResolvedAndOpenNextAsync` **or** `EndBattleAndMarkResolvedAsync` — atomic phase transition + HP update + (next turn) push new deadline into `battle:deadlines` ZSET or (end) remove from `battle:active` (`BattleTurnAppService.cs:195, 236`; scripts `RedisScripts.cs:90–117, 139–177`).
7. SignalR broadcasts (Section 2.6).
8. Postgres: 1 best-effort INSERT into `battle.battle_turns` via `BattleTurnHistoryStore.PersistTurnAsync` (`BattleTurnAppService.cs:534`). On battle end only: 1 `SaveChangesAsync` flushes outbox + `BattleCompleted` event (`BattleTurnAppService.cs:472`) plus MassTransit `Publish<BattleCompleted>` via outbox row (`BattleTurnAppService.cs:455`, `MassTransitBattleEventPublisher.cs:27–71`).

**Steady-state writes per resolved turn (mid-battle):** ~4 Redis ops (2 Lua scripts + 2 GETs), 1 Postgres INSERT, 0 broker publishes. **Per battle-ending turn:** ~5 Redis ops, 1 Postgres INSERT + 1 outbox row commit, 1 RabbitMQ `BattleCompleted` publish.

### 2.6 — Broadcasts per Turn

All broadcasts go through `SignalRBattleRealtimeNotifier` (`src/Kombats.Battle/Kombats.Battle.Infrastructure/Realtime/SignalR/SignalRBattleRealtimeNotifier.cs`) using `IHubContext<BattleHub>.Clients.Group($"battle:{battleId}").SendAsync(...)` at lines `44, 59, 76, 93, 140, 170`.

Mid-battle (turn continues) sequence (`BattleTurnAppService.cs:561–605`):

1. `PlayerDamaged` × N (1 per damage event, typically 2)
2. `TurnResolved`
3. `TurnOpened`
4. `BattleStateUpdated`

Battle-ending turn (`BattleTurnAppService.cs:375–443`):

1. `PlayerDamaged` × N
2. `TurnResolved`
3. `BattleEnded`
4. `BattleStateUpdated` (phase = Ended)

DTOs and approximate JSON sizes (per `src/Kombats.Battle/Kombats.Battle.Realtime.Contracts/`):

| DTO | Fields | ~JSON bytes |
|---|---|---|
| `BattleSnapshotRealtime` | 13 fields incl. names, HP, max HP, Phase, DeadlineUtc | ~600–700 |
| `TurnResolvedRealtime` | BattleId, TurnIndex, PlayerA/B action strings, full `TurnResolutionLogRealtime` (2× `AttackResolutionRealtime`) | ~700–900 |
| `PlayerDamagedRealtime` | BattleId, PlayerId, Damage, RemainingHp, TurnIndex | ~150 |
| `BattleStateUpdatedRealtime` | same shape as snapshot | ~600–700 |
| `BattleEndedRealtime` | BattleId, Reason, WinnerPlayerId, EndedAt, WinnerXp, LoserXp | ~200 |
| `BattleTurnOpenedRealtime` | BattleId, TurnIndex, DeadlineUtc | ~150 |

**Per-turn total per battle (mid-battle):** ~1.9 KB across 4 messages (2 damage + TurnResolved + TurnOpened + StateUpdated). For 1,000 parallel battles resolving once every ~30s (the turn timer — Section 2.8), steady-state SignalR broadcast volume is ~63 KB/s outbound from Battle, doubled because BFF re-broadcasts to its frontend connections.

In addition, BFF generates a `BattleFeedUpdated` narration message per `TurnResolved` and per `BattleEnded` (`src/Kombats.Bff/Kombats.Bff.Application/Relay/BattleHubRelay.cs:99–186, 28`).

### 2.7 — Per-Battle Locking

All concurrency control is Redis-only via Lua scripts and a per-turn lease:

- **CAS via Lua `TryMarkTurnResolvingAsync`**: only one resolver succeeds in moving `TurnOpen` → `Resolving` (`BattleTurnAppService.cs:172`; script `RedisScripts.cs:60–75, 67–68`). Losers exit early.
- **Per-turn lease lock** at `lock:battle:{battleId}:turn:{turnIndex}` (SET NX PX), acquired inside `ClaimDueBattlesScript` (`RedisScripts.cs:245`) by the deadline worker. TTL 12 s (`appsettings.json:97`).

**No `SemaphoreSlim`, no `lock(...)`, no RedLock, no `BeginTransaction`** anywhere on the battle hot path (verified via grep).

### 2.8 — Turn Deadline Worker

`src/Kombats.Battle/Kombats.Battle.Bootstrap/Workers/TurnDeadlineWorker.cs`:

- Adaptive loop. Per tick (`:34`): `RedisBattleStateStore.ClaimDueBattlesAsync(now, batchSize=50, leaseTtl=12s)` returns up to 50 `(battleId, turnIndex)` pairs whose deadline is past and whose lease was just acquired (`:56`). For each, `BattleTurnAppService.ResolveTurnAsync` runs (`:77`).
- Adaptive backoff: idle delay 200 ms → 1000 ms exponential (`:62–64`); backlog delay 30 ms (`:69`); error delay 200 ms (`:39`). Config: `Battle:TurnDeadlineWorker.{BatchSize, IdleDelayMinMs, IdleDelayMaxMs}` = 50/200/1000 (`appsettings.json:96–98`).
- Turn timer itself: `Battle:Rulesets:Versions["1"].TurnSeconds = 30` (`appsettings.json:48`).

### 2.9 — BFF Relay Behavior

`BattleHubRelay` (`src/Kombats.Bff/Kombats.Bff.Application/Relay/BattleHubRelay.cs`) is registered as singleton (`src/Kombats.Bff/Kombats.Bff.Bootstrap/Program.cs:334`). On each frontend `JoinBattle`:

- Creates a brand-new `HubConnection` to Battle (`:57–78`), forwards the JWT (`:60`), starts it (`:245`), and registers per-connection event handlers for `BattleReady`, `TurnOpened`, `PlayerDamaged` (`:25, 83–95`), `TurnResolved` (`:99`), `BattleEnded` (`:143`), `BattleStateUpdated` (`:189`).
- Stores the `HubConnection` in `_connections` dict (`:18, 241, 282`). One downstream connection per frontend connection ⇒ **2,000 concurrent players = 2,000 outbound HubConnections from BFF to Battle**.
- On `SubmitTurnAction` (`:309`), dispatches `InvokeAsync("SubmitTurnAction", ...)` on the stored downstream connection (`:334`).

---

## Section 3 — Matchmaking & Battle Lifecycle

### 3.1 — Queue Join Endpoint

- Route: `POST /api/v1/matchmaking/queue/join` (`src/Kombats.Matchmaking/Kombats.Matchmaking.Api/Endpoints/Queue/JoinQueueEndpoint.cs:15`).
- Handler: dispatches to `ICommandHandler<JoinQueueCommand, JoinQueueResult>` (`JoinQueueEndpoint.cs:18`); request DTO `JoinQueueRequest { Variant, ConnectionRef? }` (`:52`).
- `JoinQueueHandler` writes to Redis:
  - `IMatchQueueStore.TryJoinQueueAsync` (`:64`)
  - `IPlayerMatchStatusStore.SetSearchingAsync` (`:65`)
  - `IQueuePresenceStore.RegisterAsync` (`:69`)

No Postgres write on queue join.

### 3.2 — Queue Storage (Redis DB 1)

Key patterns (`src/Kombats.Matchmaking/Kombats.Matchmaking.Infrastructure/Redis/`):

| Key | Type | Purpose | TTL | File:line |
|---|---|---|---|---|
| `mm:queue:{variant}` | List | FIFO of player IDs waiting | none | `RedisMatchQueueStore.cs:31` |
| `mm:queued:{variant}` | Set | Dedup membership check | none | `RedisMatchQueueStore.cs:32` |
| `mm:canceled:{variant}` | Sorted set (score = leave time unix s) | Soft-cancel after `/leave` | `CancelTtlSeconds` = 600 (`appsettings.json:24`) | `RedisMatchQueueStore.cs:33` |
| `mm:player:{playerId}` | String (JSON `StoredPlayerMatchStatus`) | Player status: Searching / Matched / In Battle | `StatusTtlSeconds` = 1800 (`appsettings.json:23`) | `RedisPlayerMatchStatusStore.cs:38, 104` |
| `mm:queue:presence:online` | Sorted set (score = unix ms) | All online identities | n/a | `RedisQueuePresenceStore.cs:19` |
| `mm:queue:presence:refs:{identityId}` | Set of connection refs | Per-tab heartbeat refs | `PresenceTtlSeconds` = 15 (`appsettings.json:35`) | `RedisQueuePresenceStore.cs:68` |
| `mm:lease:matchmaking:{variant}` | String (lease token) | Distributed singleton lock for pairing | `LockTtlMs` = 5000 (`RedisLeaseLock.cs:15`) | `RedisLeaseLock.cs:181` |

### 3.3 — Pairing & Timeout Workers

`src/Kombats.Matchmaking/Kombats.Matchmaking.Bootstrap/Workers/`:

| Worker | Tick | Behavior | Distributed? | Cite |
|---|---|---|---|---|
| `MatchmakingPairingWorker` | `TickDelayMs` = 100 ms (`appsettings.json:27`) | Acquires `MatchmakingLeaseService` (Redis lease), then runs `ExecuteMatchmakingTickHandler` | Yes — Redis lease `mm:lease:matchmaking:{variant}`, TTL 5s, renewed every ~1.67s (`RedisLeaseLock.cs:15–16, 44–51, 91–98`) | `MatchmakingPairingWorker.cs:51–57, 72` |
| `MatchTimeoutWorker` | `ScanIntervalMs` = 5000 ms (`appsettings.json:30`) | Scans `matches` for stale rows: `TimeoutSeconds` = 60s for `BattleCreateRequested`, `BattleCreatedTimeoutSeconds` = 600s for `BattleCreated` (`appsettings.json:31–32`) | No lease — per-instance; UPDATE-based CAS | `MatchTimeoutWorker.cs:30, 41–43, 50` |
| `QueuePresenceSweepWorker` | `SweepIntervalSeconds` = 20 s (`appsettings.json:37`) | Removes stale presence entries; for each stale identity, removes from queue + status store; checks Postgres first to skip if player has active match | No lease; atomic ZREM is the contention gate (`RedisQueuePresenceStore.cs:169`) | `QueuePresenceSweepWorker.cs:55, 64–117, 87–95` |

### 3.4 — Pairing Logic

`ExecuteMatchmakingTickHandler` (`src/Kombats.Matchmaking/Kombats.Matchmaking.Application/UseCases/ExecuteMatchmakingTick/ExecuteMatchmakingTickHandler.cs`):

- **FIFO** — no MMR / no skill rating. Lua script `TryPopPairAsync` (`:41`) pops the two oldest non-canceled players in one atomic call (script body `src/Kombats.Matchmaking/Kombats.Matchmaking.Infrastructure/Redis/RedisScripts.cs:91–176`).
- Fetches combat profiles from local Postgres projection (`player_combat_profiles`); re-queues on missing profile (`:50–64`).
- Returns `MatchmakingTickResult(matchCreated, matchId, battleId)` (`:98`).

### 3.5 — Battle Creation Sequence

When the tick handler pairs two players, the following happens atomically in one transactional outbox commit (`MassTransitCreateBattlePublisher.cs:26–88`):

1. **Postgres (matchmaking schema, `matches` table):** INSERT row with `MatchId, BattleId, PlayerAId, PlayerBId, Variant, State='BattleCreateRequested', CreatedAtUtc, UpdatedAtUtc` (`MassTransitCreateBattlePublisher.cs:71–75`).
2. **MassTransit outbox row:** `CreateBattle` command queued in the same EF transaction (`:77–85`). Atomic with the matches INSERT via `SaveChangesAsync` (`:88`).

The outbox publisher then ships the message to RabbitMQ. `CreateBattleConsumer` in Battle (`src/Kombats.Battle/Kombats.Battle.Infrastructure/Messaging/Consumers/CreateBattleConsumer.cs`) consumes it:

3. **Postgres (battle schema, `battles` table):** INSERT row with `BattleId, MatchId, PlayerAId, PlayerBId, State='ArenaOpen', CreatedAt, PlayerAName, PlayerBName` (`:44–60`).
4. **Redis (DB 0):** `BattleLifecycleAppService.HandleBattleCreatedAsync` initializes state via Lua scripts — sets `battle:state:{battleId}`, adds to `battle:active`, opens Turn 1, pushes deadline into `battle:deadlines` ZSET (`:78–87`).
5. **RabbitMQ publish `BattleCreated`** (`:102–109`) — consumed by `Matchmaking.BattleCreatedConsumer` (transitions match to `BattleCreated`) and Players (no consumer for `BattleCreated` registered; only `BattleCompleted` — see Section 1.2).

### 3.6 — Battle End Pipeline

When `BattleEngine` returns a terminal result, `BattleTurnAppService.DispatchResolutionResult` triggers:

1. Redis Lua `EndBattleAndMarkResolvedAsync` — phase = `Ended`, removes from `battle:active` (`BattleTurnAppService.cs:236`; script `RedisScripts.cs:139–177`). `battle:state:{battleId}` remains (TTL `null` after end, per `appsettings.json:42`).
2. Postgres: 1 `SaveChangesAsync` for the terminal turn row + outbox row containing `BattleCompleted` (`BattleTurnAppService.cs:472`).
3. RabbitMQ `BattleCompleted` (`MassTransitBattleEventPublisher.cs:27–71`) carrying `BattleId, MatchId, PlayerAId, PlayerBId, WinnerIdentityId, LoserIdentityId, Reason, TurnCount, DurationMs, RulesetVersion, OccurredAt` (`:47–62`).
4. SignalR broadcasts: `TurnResolved`, `BattleEnded`, `BattleStateUpdated` (Section 2.6).

Downstream:

- **Matchmaking** `BattleCompletedConsumer` (`src/Kombats.Matchmaking/Kombats.Matchmaking.Infrastructure/Messaging/Consumers/BattleCompletedConsumer.cs:31–69`): UPDATE `matches.state` → `Completed`/`TimedOut` via `MatchRepository.TryAdvanceToTerminalAsync` (`:43–47`); deletes both `mm:player:*` keys (`:63–64`).
- **Players** `BattleCompletedConsumer` (`src/Kombats.Players/Kombats.Players.Infrastructure/Messaging/Consumers/BattleCompletedConsumer.cs:21–39`): dispatches `HandleBattleCompletedCommand` (`:25–32`) which UPDATEs `characters` to award XP.
- **Battle** `BattleCompletedProjectionConsumer` (`src/Kombats.Battle/Kombats.Battle.Bootstrap/Program.cs:139`) — internal projection bookkeeping.

### 3.7 — Timeout Values (all from appsettings.json)

| Timeout | Value | File |
|---|---|---|
| Battle turn timer | 30 s | `src/Kombats.Battle/Kombats.Battle.Bootstrap/appsettings.json:48` (`Battle:Rulesets:Versions["1"].TurnSeconds`) |
| Turn deadline worker idle range | 200–1000 ms | `src/Kombats.Battle/Kombats.Battle.Bootstrap/appsettings.json:97–98` |
| Turn deadline batch size | 50 | `src/Kombats.Battle/Kombats.Battle.Bootstrap/appsettings.json:96` |
| Battle recovery stale threshold | 600 s | `src/Kombats.Battle/Kombats.Battle.Bootstrap/appsettings.json:92` |
| Queue presence TTL | 15 s | `src/Kombats.Matchmaking/Kombats.Matchmaking.Bootstrap/appsettings.json:35` |
| Queue presence "stale after" | 15 s | `src/Kombats.Matchmaking/Kombats.Matchmaking.Bootstrap/appsettings.json:36` |
| Match BattleCreateRequested timeout | 60 s | `src/Kombats.Matchmaking/Kombats.Matchmaking.Bootstrap/appsettings.json:31` |
| Match BattleCreated timeout | 600 s | `src/Kombats.Matchmaking/Kombats.Matchmaking.Bootstrap/appsettings.json:32` |
| Pairing tick delay | 100 ms | `src/Kombats.Matchmaking/Kombats.Matchmaking.Bootstrap/appsettings.json:27` |
| Matchmaking pairing lease TTL | 5 s (renew ~1.67 s) | `src/Kombats.Matchmaking/Kombats.Matchmaking.Infrastructure/Redis/RedisLeaseLock.cs:15–16` |
| Action key TTL (Redis) | 12 h | `src/Kombats.Battle/Kombats.Battle.Bootstrap/appsettings.json:41` |
| Player match status TTL (Redis) | 1800 s | `src/Kombats.Matchmaking/Kombats.Matchmaking.Bootstrap/appsettings.json:23` |
| Canceled-queue TTL (Redis) | 600 s | `src/Kombats.Matchmaking/Kombats.Matchmaking.Bootstrap/appsettings.json:24` |
| Chat presence TTL | 90 s | `src/Kombats.Chat/Kombats.Chat.Infrastructure/Redis/RedisPresenceStore.cs:10` |

---

## Section 4 — Persistence Layer

### 4.1 — Postgres / EF Core

- **EF Core** 10.0.3 (`Directory.Packages.props:8`), **Npgsql.EntityFrameworkCore.PostgreSQL** 10.0.0 (`:11`), **EFCore.NamingConventions** 10.0.1 (`:12`).
- All `DbContext` registrations use `.UseNpgsql(...).EnableRetryOnFailure().UseSnakeCaseNamingConvention()` and `MigrationsHistoryTable("__ef_migrations_history", schema)` (Battle `Program.cs:91–95`; Matchmaking `Program.cs:92–96`; Players `Program.cs:101–107`; Chat `Program.cs:159–165`). `EnableRetryOnFailure()` uses Npgsql defaults — 6 retries with built-in delay.
- **No `Database.MigrateAsync()` on startup** in any service — comments at `Battle Program.cs:183`, `Matchmaking Program.cs:188`, `Players Program.cs:149–150`, `Chat Program.cs:258` reference "AD-13". Migrations run via the `Kombats.Migrator` job (`src/Kombats.Migrator/`).

#### DbContexts and entities

| DbContext | File | Schema | DbSets |
|---|---|---|---|
| `BattleDbContext` | `src/Kombats.Battle/Kombats.Battle.Infrastructure/Data/DbContext/BattleDbContext.cs:7–66` | `battle` | `Battles` (`:15`), `BattleTurns` (`:16`); MassTransit outbox/inbox tables (`:63–65`) |
| `MatchmakingDbContext` | `src/Kombats.Matchmaking/Kombats.Matchmaking.Infrastructure/Data/MatchmakingDbContext.cs:11–76` | `matchmaking` | `Matches` (`:20`), `PlayerCombatProfiles` (`:21`); MassTransit outbox/inbox (`:73–75`) |
| `PlayersDbContext` | `src/Kombats.Players/Kombats.Players.Infrastructure/Data/PlayersDbContext.cs:8–28` | `players` | `Characters` (`:12`), `InboxMessages` (`:13`); MassTransit outbox/inbox (`:24–26`) |
| `ChatDbContext` | `src/Kombats.Chat/Kombats.Chat.Infrastructure/Data/ChatDbContext.cs:8–25` | `chat` | `Conversations` (`:12`), `Messages` (`:13`); MassTransit outbox/inbox (`:21–23`) |

#### Migrations folders

- Battle: `src/Kombats.Battle/Kombats.Battle.Infrastructure/Data/Migrations/` — `20260404055612_Baseline.cs`, `20260412074313_AddParticipantMetadata.cs`, `20260412081822_AddTurnHistory.cs`.
- Matchmaking: `src/Kombats.Matchmaking/Kombats.Matchmaking.Infrastructure/Migrations/` — `20260404055616_Baseline.cs`, `20260408062301_RemoveLegacyCustomOutboxTable.cs`, `20260420120500_AddPlayerCombatProfileAvatarId.cs`, `20260425120500_BackfillPlayerCombatProfileAvatarIds.cs`.
- Players: `src/Kombats.Players/Kombats.Players.Infrastructure/Persistence/EF/Migrations/` — `20260404055607_Baseline.cs`, `20260407054312_AddOutboxEntities.cs`, `20260420120000_AddCharacterAvatarId.cs`, `20260425120000_BackfillCharacterAvatarIds.cs`.
- Chat: `src/Kombats.Chat/Kombats.Chat.Infrastructure/Migrations/` — `20260414120615_InitialCreate.cs`.

#### Tables that see writes during a battle

- `battle.battles` — INSERT at create (`CreateBattleConsumer.cs:44–60`), UPDATE on end / orphan recovery (`BattleRecoveryRepository.cs:36–38`). PK `BattleId` (GUID, `ValueGeneratedNever()`, `BattleDbContext.cs:28`).
- `battle.battle_turns` — **the primary write-heavy table for the hot path**. One INSERT per resolved turn, per battle. Composite PK `(BattleId, TurnIndex)` (`BattleDbContext.cs:47`). Cascading delete from `battles` (`:57–60`).
- `matchmaking.matches` — INSERT at pair (`MassTransitCreateBattlePublisher.cs:71–75`), UPDATE via `MatchRepository.TryAdvanceToBattleCreatedAsync` and `TryAdvanceToTerminalAsync` using `.ExecuteUpdateAsync()` (`MatchRepository.cs:58–88`). PK `MatchId`. Indexes: `BattleId` unique, `PlayerAId`, `PlayerBId`, composite `(PlayerAId, CreatedAtUtc)`, `(PlayerBId, CreatedAtUtc)` (`MatchmakingDbContext.cs:43–47`).
- `players.characters` — UPDATE on XP award via `HandleBattleCompletedCommand.cs:99`. PK `Id`; unique index `IdentityId` (`src/Kombats.Players/Kombats.Players.Infrastructure/Configuration/CharacterConfig.cs:14, 17`).
- `players.inbox_messages` — INSERTed by MassTransit on `BattleCompleted` arrival for idempotency. PK `MessageId` (`InboxMessageConfig.cs:12`).
- `matchmaking.player_combat_profiles` — UPDATEd by `PlayerCombatProfileChangedConsumer`. PK `IdentityId`; index `CharacterId` (`MatchmakingDbContext.cs:54, 69`).
- MassTransit outbox/inbox tables (in every schema): see broker traffic on every cross-service event.

#### Connection-string pooling

All four services use **identical** Postgres connection-string parameters (verified via grep at line 13 in each `appsettings.json`):

```
Host=localhost;Port=5432;Database=kombats;Username=postgres;Password=postgres;Maximum Pool Size=20;Minimum Pool Size=5;Connection Idle Lifetime=300
```

The Azure deploy injects the same shape via secret (`infra/workload.bicep:292`): `Maximum Pool Size=20; Minimum Pool Size=5; Connection Idle Lifetime=300`.

- `Command Timeout` — not set; Npgsql default is 30 s.
- `Connection Lifetime` — not set; uses idle lifetime instead.
- **Total cluster pool ceiling per replica set:** 4 services × 1 replica × 20 = 80 connections (per the deployed scaling — Section 6).

#### N+1 / lazy loading

- `UseLazyLoadingProxies()`: **NOT FOUND** (verified by reading all DbContext `OnModelCreating`).
- `.Include(...)`: not used on hot paths. `BattleHistoryRepository.GetBattleHistoryAsync` (`src/Kombats.Battle/Kombats.Battle.Infrastructure/Data/BattleHistoryRepository.cs:17–51`) does two explicit `AsNoTracking()` queries (battle row + turn rows) — no N+1.
- `MatchRepository.GetActiveForPlayerAsync` (`src/Kombats.Matchmaking/Kombats.Matchmaking.Infrastructure/Repositories/MatchRepository.cs:24–34`) is a single filtered query.

### 4.2 — Redis

- **Client:** `StackExchange.Redis` 2.8.16 (`Directory.Packages.props:55`).
- **Registration:** singleton `IConnectionMultiplexer` per service (Battle `Program.cs:99–102`, Matchmaking `Program.cs:104–108`, Chat `Program.cs:180–182`). `AbortOnConnectFail = false` everywhere.
- **Topology:** single shared Redis instance, **logical-DB-per-service**:
  - Battle → DB 0 (no `defaultDatabase` in conn string, no `GetDatabase(...)` index override; verified in `RedisBattleStateStore.cs`).
  - Matchmaking → DB **1** (`src/Kombats.Matchmaking/Kombats.Matchmaking.Infrastructure/Options/MatchmakingRedisOptions.cs:14`, `appsettings.json:22`).
  - Chat → DB **2** (hardcoded `redis.GetDatabase(2)` in `src/Kombats.Chat/Kombats.Chat.Infrastructure/Redis/RedisPresenceStore.cs:61`).

#### Key namespaces grouped by purpose

| Namespace | Purpose | Usage class |
|---|---|---|
| `battle:state:*`, `battle:action:*`, `battle:turn:*:submitted`, `battle:active`, `battle:deadlines` | Live battle state | Primary store |
| `lock:battle:*:turn:*` | Per-turn resolver lease | Distributed lock |
| `mm:queue:*`, `mm:queued:*`, `mm:canceled:*`, `mm:player:*` | Matchmaking queue + status | Primary store (queue + status) |
| `mm:queue:presence:*` | Queue heartbeat | Ephemeral cache |
| `mm:lease:matchmaking:*` | Pairing worker singleton lock | Distributed lock |
| `chat:presence:*`, `chat:presence:refs:*` | Online players, refs | Primary store (presence) |
| `chat:ratelimit:*` | Per-surface rate limiter buckets | Cache |
| `chat:playerinfo:*` | Player display name cache | Cache |

#### TTLs (cited)

- `battle:action:*` and submission marker: `ActionTtl` = 12h (`BattleRedisOptions.cs:14`, `appsettings.json:41`); applied via `KeyExpireAsync` (`RedisBattleStateStore.cs:396`) and Lua `EXPIRE` (Lua at `:307`).
- `battle:state:*` after end: `StateTtlAfterEnd` = `null` (never expires by config) — `appsettings.json:42`.
- `lock:battle:*`: 12 s (`appsettings.json:97`).
- `mm:player:*`: 1800 s (`MatchmakingRedisOptions.cs:20`, `appsettings.json:23`).
- `mm:canceled:*`: 600 s (`appsettings.json:24`).
- `mm:queue:presence:refs:*`: 15 s (`appsettings.json:35`).
- `mm:lease:matchmaking:*`: 5000 ms (`RedisLeaseLock.cs:15`).
- `chat:presence:*`: 90 s (`RedisPresenceStore.cs:10`).
- `chat:ratelimit:*`: per-window via `KeyExpireAsync(TimeSpan.FromSeconds(windowSeconds))` (`RedisRateLimiter.cs`).

### 4.3 — RabbitMQ / MassTransit

- **Library:** MassTransit 8.5.9 (`Directory.Packages.props:16`) with `MassTransit.RabbitMQ` and `MassTransit.EntityFrameworkCore` (outbox).
- **Central setup:** `src/Kombats.Common/Kombats.Messaging/DependencyInjection/MessagingServiceCollectionExtensions.cs:25–120` — extension `AddMessaging<TDbContext>(builder, prefix, configureConsumers, configure)`. EF outbox at `:49–54`; per-bus knobs `cfg.PrefetchCount = options.Transport.PrefetchCount` (`:85`) and `cfg.ConcurrentMessageLimit = options.Transport.ConcurrentMessageLimit` (`:86`); retry pipeline `:94–101`; redelivery `:103–112`.
- **Defaults** (`src/Kombats.Common/Kombats.Messaging/Options/MessagingOptions.cs`):
  - `PrefetchCount` = 32 (`:29`)
  - `ConcurrentMessageLimit` = 8 (`:30`)
  - Outbox `QueryDelaySeconds` = 1 (`:58`)
  - Retry: exponential, count 5, min 200 ms, max 5000 ms, delta 200 ms (`:42–46`)
- **Naming:** `src/Kombats.Common/Kombats.Messaging/Naming/` — global `EntityNamePrefix = "combats"` (`MessagingOptions.cs:36`), kebab-case enabled (`:37`), per-service `EndpointPrefix` ("battle"/"matchmaking"/"players"/"chat") passed in by the `AddMessaging` call.

#### Consumers and contracts

| Service | Consumer | Contract consumed | Publisher of that contract |
|---|---|---|---|
| Battle | `CreateBattleConsumer` (`Program.cs:138`) | `CreateBattle` (`src/Kombats.Battle/Kombats.Battle.Contracts/Battle/CreateBattle.cs`) | Matchmaking (`MassTransitCreateBattlePublisher.cs:26–88`) |
| Battle | `BattleCompletedProjectionConsumer` (`Program.cs:139`) | `BattleCompleted` (own) | Battle itself (`MassTransitBattleEventPublisher.cs:27–71`) |
| Matchmaking | `PlayerCombatProfileChangedConsumer` (`Program.cs:142`) | `PlayerCombatProfileChanged` (`src/Kombats.Players/Kombats.Players.Contracts/`) | Players (`MassTransitCombatProfilePublisher`) |
| Matchmaking | `BattleCreatedConsumer` (`Program.cs:143`) | `BattleCreated` (`src/Kombats.Battle/Kombats.Battle.Contracts/`) | Battle (in `BattleLifecycleAppService`) |
| Matchmaking | `BattleCompletedConsumer` (`Program.cs:144`) | `BattleCompleted` | Battle |
| Players | `BattleCompletedConsumer` (`Program.cs:139`) | `BattleCompleted` | Battle |
| Chat | `PlayerCombatProfileChangedConsumer` (`Program.cs:245`) | `PlayerCombatProfileChanged` | Players |

Chat does not publish any integration events (no publisher registrations in `Program.cs`).

---

## Section 5 — Authentication Flow

- **Shared extension `AddKombatsAuth`** lives at `src/Kombats.Common/Kombats.Abstractions/Auth/KombatsAuthExtensions.cs:13–37`. It reads `Keycloak:Authority` and `Keycloak:Audience` from config (`:24`), sets `RequireHttpsMetadata = false` (`:26`), `SaveToken = true` (`:29`), `NameClaimType = "preferred_username"` (`:31`). Called by Battle (`Program.cs:39`), Matchmaking (`Program.cs:40`), Players (`Program.cs:38`), Chat (`Program.cs:42`).
- **BFF inlines** its own JWT bearer (`src/Kombats.Bff/Kombats.Bff.Bootstrap/Program.cs:33–58`) — the CLAUDE.md note "AD-17" prohibits BFF from referencing `Kombats.Abstractions`. The inlined config matches the shared one and adds an `OnMessageReceived` handler that copies `?access_token=...` to `context.Token` when the path starts with `/battlehub` or `/chathub` (`:42–57`).
- **Validation mode: local JWKS, not introspection.** `JwtBearerOptions.Authority` (`KombatsAuthExtensions.cs:24`, BFF `Program.cs:36`) uses OIDC discovery to fetch and cache JWKS once. No `IntrospectionUrl`, no per-request HTTP call to Keycloak. JWKS cache lifetime is .NET default (cached per `ConfigurationManager`, refresh on signature failure).
- **SignalR `OnConnectedAsync`** — no per-connection Keycloak call:
  - Battle: `BattleHub` does not override `OnConnectedAsync`; user identity is read from `Context.User` claims inside hub methods (`src/Kombats.Battle/Kombats.Battle.Infrastructure/Realtime/SignalR/BattleHub.cs:78–90`).
  - Chat: `InternalChatHub.OnConnectedAsync` reads identity from `Context.User?.GetIdentityId()`, then enters per-identity group and dispatches `ConnectUserCommand` — still no Keycloak call (`src/Kombats.Chat/Kombats.Chat.Api/Hubs/InternalChatHub.cs:33–57`).
  - BFF: `BattleHub` and `ChatHub` do not override `OnConnectedAsync`.
- **Keycloak realm**: `infra/keycloak/realm.json:2` defines realm `kombats`. Audience claim mapper named `kombats-api-audience` (`realm.json:107`). Authority URLs in deployed env are `http://keycloak:8080/realms/kombats` (`docker-compose.yml:123, 157, 186, 217, 248`, `infra/workload.bicep:249–254`).
- **Token lifetimes** (`infra/keycloak/realm.json:17–22`):
  - `accessTokenLifespan: 3600` (1 h)
  - `ssoSessionMaxLifespan: 36000` (10 h)
  - `ssoSessionIdleTimeout: 36000`
  - `ssoSessionMaxLifespanRememberMe`/`ssoSessionIdleTimeoutRememberMe`: 2,592,000 (30 d)

**Implication for the load test:** Keycloak does NOT need to be in the load test if test users have pre-minted tokens that expire after the planned run window — every validation is local. Keycloak is only hit at login, refresh, and JWKS warm-up.

---

## Section 6 — Deployment & Scaling Config

### 6.1 — Deployment artifacts

- `docker-compose.yml` — full local stack (build-from-Dockerfile mode).
- `docker-compose.local.yml` — IDE-mode (infra only; backends run from Rider/VS).
- `docker-compose.override.yml` — small override.
- `infra/main.bicep`, `infra/main.bicepparam`, `infra/workload.bicep`, `infra/modules/` — Azure deploy.
- `azure-pipelines.yml` (root) and `pipelines/build-backend.yml`, `pipelines/deploy-stack.yml`, `pipelines/teardown.yml`.
- `scripts/deploy-stack.sh`, `scripts/run-migrations.sh`, etc.
- GitHub Actions: `.github/workflows/` — NOT EXAMINED (the deploy pipeline is Azure DevOps; the recent commit `f1aa085 ci: replace Smoke tests sleep with retry loop` is in `pipelines/deploy-stack.yml`).

### 6.2 — Azure target

**Azure Container Apps**, single managed environment `cae-kombats-demo` (`infra/workload.bicep:51, 87–99`), backed by Log Analytics workspace `log-kombats-demo` (`:50, 74–83`, `retentionInDays: 30`).

Three stateful workloads run **as Container Apps** alongside the stateless services (`infra/workload.bicep:128–184`) via `modules/stateful-app.bicep`:

| Workload | Image | CPU | Mem | Port | Persistent volume |
|---|---|---|---|---|---|
| `postgres` | `postgres:16-alpine` | 0.5 | 1Gi | 5432 | **false** (`workload.bicep:136`) — demo ephemeral |
| `redis` | `redis:7-alpine` | 0.25 | 0.5Gi | 6379 | false |
| `rabbitmq` | `rabbitmq:3-management-alpine` | 0.5 | 1Gi | 5672 | false |
| `keycloak` | custom GHCR image | n/a | n/a | n/a | Azure File share-backed for realm import (`workload.bicep:198–214`) |

No Azure Database for PostgreSQL Flexible Server, no Azure Cache for Redis, no Azure Service Bus, no Azure SignalR Service.

### 6.3 — Replicas per service

Default scale (`infra/modules/backend-app.bicep:40, 43` per the reconnaissance — `minReplicas: 0`, `maxReplicas: 1`) is overridden only for BFF:

| Service | minReplicas | maxReplicas | Source |
|---|---|---|---|
| battle | 0 | 1 | module defaults (`backend-app.bicep:40, 43`) |
| players | 0 | 1 | module defaults |
| matchmaking | 0 | 1 | module defaults |
| chat | 0 | 1 | module defaults |
| bff | **1** | **1** | `infra/workload.bicep:425–426` |

CPU/memory per backend container (`backend-app.bicep:103–106`): `0.25` vCPU, `0.5Gi` memory.

The BFF cap comment is explicit (`infra/workload.bicep:387–390`): "maxReplicas=1 because SignalR is sticky-session and we have no Redis/Service-Bus backplane (Session 10 territory)." The default cap of 1 for Battle/Chat is implicit, but the same constraint applies for their SignalR hubs.

### 6.4 — Statelessness & SignalR sticky requirement

- **BFF stores per-frontend-connection state in process memory** (`BattleHubRelay._connections` `BattleHubRelay.cs:18`). Stateful → must remain at 1 replica without redesign. Sticky session is moot since maxReplicas=1.
- **Battle uses SignalR Groups** with no backplane (`Program.cs:129–130`, Section 2.3). Multiple Battle replicas would partition group state. Currently capped at 1.
- **Matchmaking pairing** is already distributed-lock-safe via Redis lease (`RedisLeaseLock`) — multiple replicas would serialize behind the lock, not break correctness.
- **Players, Chat** workers are also single-instance under current caps; if scaled, `MessageRetentionWorker` and `PresenceSweepWorker` would race (no lease).

### 6.5 — Health checks

- `/health/live` (predicate `_ => false` → always returns 200): Battle `Program.cs:202`, Matchmaking `Program.cs:205`, Players `Program.cs:167`, Chat `Program.cs:277`. All `.AllowAnonymous()`.
- `/health/ready` (full checks): same files, line below. BFF: NOT FOUND — BFF Program.cs lacks `MapHealthChecks` calls (verified).
- Bicep `backend-app.bicep` does not configure explicit probes (NOT EXAMINED in detail — the module file was not opened; Container Apps default HTTP probing will hit `:8080/`).

### 6.6 — Rate limiting

**NOT FOUND** at the API surface. Grep for `AddRateLimiter`/`UseRateLimiter`/`EnableRateLimiting` across `src/` returns 0 matches (verified).

- BFF uses **Polly HTTP resilience** per downstream typed client (`src/Kombats.Bff/Kombats.Bff.Bootstrap/Program.cs:178–206, 216–244, 254–278, 288–312`): timeout, retry (GET-only), circuit breaker (50% threshold, 30s break, 5-failure minimum throughput).
- Chat has per-user message rate limiting via `RedisRateLimiter` (`src/Kombats.Chat/Kombats.Chat.Bootstrap/Program.cs:186`) — bucket-style in Redis. This is application-level, inside hub methods, not at the ingress.

### 6.7 — Pipelines

- `pipelines/build-backend.yml` — CI: builds 7 Docker images (bff, battle, players, matchmaking, chat, keycloak custom, migrator) and pushes to GHCR tagged `Build.BuildId` and `latest`.
- `pipelines/deploy-stack.yml` — CD: applies Bicep, builds and uploads React frontend to SWA, patches BFF CORS via `az containerapp update`, uploads Keycloak realm.json to the file share, runs migrator job, restarts Keycloak, runs smoke health probes (retry loop, no fixed sleep, per recent commit `f1aa085`).
- `pipelines/teardown.yml` — destroys the resource group.
- `azure-pipelines.yml` (root) — top-level orchestrator (NOT READ in detail).

### 6.8 — Migrator

`src/Kombats.Migrator/` is a one-shot container job deployed via `infra/modules/migrator-job.bicep` (`workload.bicep:218–233`). It applies EF migrations for all four service schemas using their respective DbContexts. Migrations are never applied at service startup (AD-13 comments throughout `Program.cs`).

---

## Section 7 — Observability

### 7.1 — Logging

- **Serilog** (`Serilog.AspNetCore` 10.0.0 — `Directory.Packages.props:59`) wired via `builder.Host.UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration))` in Battle `Program.cs:35–36`, BFF `Program.cs:24–25`, Matchmaking `Program.cs:36–37`, Players `Program.cs:34–35`, Chat `Program.cs:38–39`.
- `appsettings.json` `Serilog` section in every service configures a **single Console sink only** with `outputTemplate: [{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}`. Confirmed in Battle `appsettings.json:128–131`, Chat `appsettings.json:68–71`, BFF `appsettings.json:50–56`.
- Enrichers: `FromLogContext`, `WithMachineName`, `WithThreadId`; Battle also `WithExceptionDetails`.
- **No file, Seq, ApplicationInsights, or OTLP log sink.** In Azure, Container Apps captures stdout/stderr into the Log Analytics workspace `log-kombats-demo` (`workload.bicep:74–83, 87–99`).
- `app.UseSerilogRequestLogging()` is enabled in every service.

### 7.2 — Metrics

**NOT FOUND.** No `Meter(`, `CreateCounter`, `CreateHistogram`, `prometheus-net`, `AddPrometheusExporter`, `AddMeter` anywhere under `src/` (verified). No custom application metrics are emitted. The OpenTelemetry registrations only wire **tracing**, not metrics (`.WithTracing(...)` calls only — see below).

### 7.3 — Tracing

OpenTelemetry tracing in every service (`OpenTelemetry` 1.15.0 — `Directory.Packages.props:66`):

| Service | Source name | Instrumentations | OTLP endpoint config |
|---|---|---|---|
| Battle | `Kombats.Battle` | AspNetCore + HttpClient + `Npgsql` source | `OpenTelemetry:OtlpEndpoint` (`Program.cs:156–170`) |
| Matchmaking | `Kombats.Matchmaking` | AspNetCore + HttpClient + `Npgsql` | `Program.cs:162–176` |
| Players | `Kombats.Players` | AspNetCore + HttpClient + `Npgsql` | `Program.cs:117–131` |
| Chat | `Kombats.Chat` | AspNetCore + HttpClient + `Npgsql` | `Program.cs:138–152` |
| BFF | `Kombats.Bff` | AspNetCore + HttpClient (no Npgsql; BFF has no DB) | `Program.cs:70–83` |

OTLP exporter is **only enabled if** `OpenTelemetry:OtlpEndpoint` is set (`if (!string.IsNullOrEmpty(otlpEndpoint))` pattern — Battle `Program.cs:166–169`, etc.). The default `appsettings.json` does not set it (verified for Battle `appsettings.json:110` per the persistence agent's report). **Without an OTLP collector endpoint, traces are discarded.**

No custom `ActivitySource` declarations were found.

### 7.4 — Dashboards

**NOT FOUND.** No `.kql`, no Grafana `.json`, no `dashboards/` / `monitoring/` / `docs/observability/` folders in the repo (verified by listing top-level and `docs/` subtree). Operating diagnostics in deployed env relies on raw Log Analytics queries against Container Apps stdout.

---

## Section 8 — Existing Tests

### 8.1 — Test projects

Twenty test `csproj` files under `tests/`:

```
tests/Kombats.Bff/Kombats.Bff.Api.Tests/
tests/Kombats.Bff/Kombats.Bff.Application.Tests/
tests/Kombats.Players/Kombats.Players.Api.Tests/
tests/Kombats.Players/Kombats.Players.Infrastructure.Tests/
tests/Kombats.Players/Kombats.Players.Domain.Tests/
tests/Kombats.Players/Kombats.Players.Application.Tests/
tests/Kombats.Battle/Kombats.Battle.Application.Tests/
tests/Kombats.Battle/Kombats.Battle.Domain.Tests/
tests/Kombats.Battle/Kombats.Battle.Infrastructure.Tests/
tests/Kombats.Battle/Kombats.Battle.Api.Tests/
tests/Kombats.Chat/Kombats.Chat.Infrastructure.Tests/
tests/Kombats.Chat/Kombats.Chat.Application.Tests/
tests/Kombats.Chat/Kombats.Chat.Api.Tests/
tests/Kombats.Chat/Kombats.Chat.Domain.Tests/
tests/Kombats.Integration/Kombats.Integration.Tests/
tests/Kombats.Common/Kombats.Messaging.Tests/
tests/Kombats.Matchmaking/Kombats.Matchmaking.Api.Tests/
tests/Kombats.Matchmaking/Kombats.Matchmaking.Infrastructure.Tests/
tests/Kombats.Matchmaking/Kombats.Matchmaking.Application.Tests/
tests/Kombats.Matchmaking/Kombats.Matchmaking.Domain.Tests/
```

Classification:

| Project | Kind |
|---|---|
| `*.Domain.Tests` | Pure unit (no infra) |
| `*.Application.Tests` | Unit + handler-level |
| `*.Infrastructure.Tests` (Battle, Chat, Matchmaking, Players) | Integration with Testcontainers Postgres/Redis |
| `*.Api.Tests` (Battle, Chat, Matchmaking, Players, Bff) | API tests via `WebApplicationFactory<Program>` |
| `Kombats.Integration.Tests` | Cross-service flow (`I01_PlayersToMatchmakingFlowTests.cs`, `I02_MatchmakingToBattleFlowTests.cs`, `I03_BattleCompletionFlowTests.cs`, `I04_EndToEndGameplayLoopTests.cs`) — Testcontainers for Postgres + RabbitMQ |
| `Kombats.Messaging.Tests` | Integration: outbox/inbox flow with Testcontainers Postgres + RabbitMQ |

### 8.2 — Testcontainers / fixtures

- Central package versions (`Directory.Packages.props:95–97`): `Testcontainers.PostgreSql` 4.3.0, `Testcontainers.Redis` 4.3.0, `Testcontainers.RabbitMq` 4.3.0.
- Battle API tests use `BattleWebApplicationFactory : WebApplicationFactory<Program>` (lines 20, 30–38 per the agent's findings) starting `PostgreSqlBuilder`, `RedisBuilder`, `RabbitMqBuilder` in `InitializeAsync` and replacing connection strings + `IConnectionMultiplexer` in DI (lines 59–71, 85–87).
- Chat: separate `ChatApiFactory` and `ChatHubFactory` to keep API and Hub tests isolated.
- Integration project: each flow test class manages its own Testcontainers lifecycle — **no shared cross-suite fixture**.
- `Program.cs` files for Battle, Players, Chat end with `public partial class Program;` (Battle `:208`, Players `:173`, Chat `:283`) to support `WebApplicationFactory<Program>`.

### 8.3 — Performance / load / benchmark tests

**NOT FOUND.** Grep for `BenchmarkDotNet`, `NBomber`, `k6`, `loadtest`, `bombardier`, `wrk` across the full repo returns 0 matches. No benchmark project, no `.k6.js`, no load-test infrastructure exists.

---

## Section 9 — Red Flags Under 2,000 Concurrent / 1,000 Parallel Battles

- **No SignalR backplane anywhere.** Battle `Program.cs:129–130` and BFF `Program.cs:315–319` configure only `AddSignalR()`. Scaling either service > 1 replica partitions Groups/Connections — this is why BFF is hard-capped at `maxReplicas: 1` (`infra/workload.bicep:425–426`).
- **BFF stores per-connection downstream `HubConnection`s in a process-local `ConcurrentDictionary`.** `src/Kombats.Bff/Kombats.Bff.Application/Relay/BattleHubRelay.cs:18`. At 2,000 concurrent players, that's 2,000 outbound SignalR connections to Battle from one BFF replica plus 2,000 inbound — single point of contention and a hard ceiling on a single CPU/memory budget of `0.25 vCPU / 0.5Gi` (`infra/modules/backend-app.bicep:103–106`).
- **BFF and Battle each run on 0.25 vCPU / 0.5Gi.** With 1 replica each, this is the entire compute budget for 2,000 players. Source: `infra/modules/backend-app.bicep:103–106` and the lack of an override in `workload.bicep`.
- **Postgres pool ceiling 20 per service.** Connection string in every deployed service: `Maximum Pool Size=20` (`infra/workload.bicep:292`, `src/*/Bootstrap/appsettings.json:13`). Total cluster-wide ceiling is 80 connections at the current 1-replica caps. Under bursty turn resolutions, pool waits will dominate latency before raw Postgres is saturated.
- **Postgres runs as a Container App with no persistent volume.** `infra/workload.bicep:136` — `usePersistentVolume: false`. Restart loses data. CPU 0.5 / Mem 1Gi (`:137–138`).
- **Redis runs as a Container App with no persistent volume, single instance, no Sentinel/cluster.** `infra/workload.bicep:158–170`. Failure loses every in-flight battle's live state (which is **primary store**, not cache).
- **RabbitMQ runs as a Container App single instance, no clustering, no persistent volume.** `infra/workload.bicep:172–184`. Outbox-published messages survive only as long as the broker process; restart blanks the queue.
- **MassTransit consumer concurrency = 8, prefetch = 32, per service.** `MessagingOptions.cs:29–30`. At 1 replica per service, total in-flight consumer parallelism is 8 for each contract type per service. `Matchmaking.BattleCompletedConsumer` deletes Redis status keys synchronously inside the handler (`BattleCompletedConsumer.cs:63–64`) — at 1,000 battles ending in a narrow window, this is a bottleneck.
- **No API-layer rate limiting** anywhere. `AddRateLimiter`/`UseRateLimiter` not found (verified). The only ingress protection at BFF is Polly circuit-breaker per downstream typed client (`Bff Program.cs:178–206 etc.`) — those protect BFF from downstream failure, not BFF from clients.
- **Per-turn database INSERT into `battle.battle_turns` is best-effort inside the resolution path.** `BattleTurnAppService.cs:534`. 1,000 simultaneous resolved turns = 1,000 INSERTs in a burst; the table PK is composite `(BattleId, TurnIndex)` and no fillfactor/partitioning is configured (no migration adds it).
- **Battle Postgres pool 20 vs. 1,000 parallel battles.** Even with the engine pure-in-memory, every battle-ending turn does `SaveChangesAsync` for outbox row (`BattleTurnAppService.cs:472`). 1,000 game-ending events at the same time will queue behind 20 pool connections.
- **`TurnDeadlineWorker.BatchSize = 50` + tick floor 200 ms.** `src/Kombats.Battle/Kombats.Battle.Bootstrap/Workers/TurnDeadlineWorker.cs` and `appsettings.json:96–98`. Resolving 1,000 simultaneously-expired turns takes at minimum 20 tick passes ⇒ at busy/backlog delay 30 ms that's ~600 ms before the last battle gets its turn resolved — observable client lag in the worst case.
- **Singleton `BattleHubRelay` retains a `BattleConnectionState` per connection with HP and participant data.** `BattleHubRelay.cs:264–279`. Memory grows linearly with active battles on the BFF process — no eviction except on `Disconnect`/`BattleEnded`.
- **No idempotency on `SubmitTurnAction` at BFF layer.** Frontend retries land as duplicate downstream `InvokeAsync("SubmitTurnAction", ...)` (`BattleHubRelay.cs:334`). Idempotency exists only inside Battle via the Lua `StoreActionAndCheckBothSubmitted` (returns `alreadySubmitted`).
- **Matchmaking pairing is FIFO with a single global lease.** `MatchmakingLeaseService` + `RedisLeaseLock` (`appsettings.json:27` 100 ms tick, lease key `mm:lease:matchmaking:{variant}`). Only one Matchmaking replica produces matches at a time. At 2,000 players queuing simultaneously and ~500 pairs to form, the worker grinds through them under one lease.
- **`QueuePresenceSweepWorker` has no lease.** `QueuePresenceSweepWorker.cs:55, 64–117`. With > 1 Matchmaking replica it would do duplicate work across replicas (mitigated by atomic ZREM — `RedisQueuePresenceStore.cs:169` — but still wasteful).
- **`MessageRetentionWorker` and `PresenceSweepWorker` in Chat have no lease.** `src/Kombats.Chat/Kombats.Chat.Bootstrap/Program.cs:253–254`. Same race profile as above if Chat is ever scaled.
- **OTLP endpoint config defaults to empty.** Traces are discarded unless `OpenTelemetry:OtlpEndpoint` is set. Bicep does not appear to inject it (no `OpenTelemetry__` env in `commonBackendEnv` `infra/workload.bicep:247–284`). The load test will have **no distributed traces** without setting this.
- **No application metrics** — there is no way to observe per-turn resolution latency, queue depth, hub group size, downstream HubConnection count, or Postgres pool wait time from within the application (Section 7.2). Pure log-based observation through Log Analytics.
- **`battle:state:{battleId}` never expires after a battle ends** (`StateTtlAfterEnd: null`, `appsettings.json:42`). 1,000 ended battles = 1,000 stale Redis strings forever, only cleaned by recovery logic if any.
- **Per-frontend-connection downstream HubConnection startup cost.** Each `JoinBattle` rebuilds a brand-new `HubConnectionBuilder().WithUrl(...).Build()` + `StartAsync` (`BattleHubRelay.cs:57–78, 245`) — a full WebSocket handshake against Battle. At a 2,000-player thundering herd at match start, that's 2,000 simultaneous SignalR handshakes hitting one Battle replica.
- **`OnDisconnectedAsync` does not call `Groups.RemoveFromGroupAsync` in Battle hub** (`src/Kombats.Battle/Kombats.Battle.Infrastructure/Realtime/SignalR/BattleHub.cs:72–76`). SignalR usually cleans up groups on disconnect, but no explicit teardown means orphaned group memberships if SignalR's automatic cleanup misses an edge case.
- **`SubmitTurnAction` in BFF relay uses `CancellationToken.None` deliberately** (`BattleHubRelay.cs:339`). Stuck downstream invokes wait for SignalR's 30 s server timeout — they cannot be cancelled by the frontend.

---

## Section 10 — Open Questions

1. **Is the Azure deploy the actual load-test target, or is there a separate prod/perf environment?** Current bicep deploys all infra as Container Apps single-instance. If the load test runs against this stack, Postgres/Redis/RabbitMQ are themselves capped at 0.25–0.5 vCPU containers (`infra/workload.bicep:128–184`).
2. **Is Postgres ever swapped for Azure Database for PostgreSQL Flexible Server in a prod variant?** `infra/main.bicepparam` was not read; no module references managed Postgres. Confirm with user.
3. **Is Redis ever swapped for Azure Cache for Redis?** Same as above — no module references Azure Cache. The connection string supports Sentinel (`Program.cs:98` comment in Battle), but no Sentinel config is deployed.
4. **What's the expected turn cadence under load?** Turn timer is 30 s (`appsettings.json:48`) but battles can resolve faster when both players submit early. Average turns-per-battle and average actual turn duration are needed to size Redis ops/s and Postgres `battle_turns` inserts/s.
5. **Number of turns per battle in expected gameplay.** Bicep+code don't tell; `BattleEngine` ends on HP ≤ 0 (`src/Kombats.Battle/Kombats.Battle.Domain/Engine/BattleEngine.cs`) and is data-driven by ruleset. Affects per-battle DB write count.
6. **Will BFF be scaled out for load testing?** With `maxReplicas: 1` and process-local `_connections` map, the load test on the current Bicep cannot exceed one BFF replica's capacity for SignalR connections. If the answer is "yes, change the bicep", a SignalR backplane (Redis or Azure SignalR) is required first.
7. **Where is the Bicep replica default actually set?** The `backend-app.bicep` module file was not read in this recon (only its parameters as called from `workload.bicep`). Confirm `minReplicas: 0, maxReplicas: 1` defaults vs. consumer override.
8. **What's the value of `OpenTelemetry:OtlpEndpoint` in the live deploy?** The `commonBackendEnv` in `infra/workload.bicep:247–284` does not appear to set it. If unset, the load test will have no distributed traces — confirm.
9. **Is there a Keycloak token bulk-creation script for load tests?** Test users + pre-minted JWTs are required to avoid hammering Keycloak. No such script was found in `scripts/`.
10. **`infra/main.bicep` content was not read** in detail; only `workload.bicep` (which it calls as a module). Confirm subscription-scope deploys, RG creation, and any extra resources defined at the parent scope.
11. **GitHub Actions vs. Azure Pipelines.** Both `azure-pipelines.yml` (root) and `pipelines/*.yml` exist. The recent commits reference smoke tests in `pipelines/deploy-stack.yml`. `.github/workflows/` was not inventoried.
12. **Are there idle/disconnect timeouts on the SignalR transport itself?** Default `ServerTimeout`/`HandshakeTimeout` are used (no `services.AddSignalR(opts => opts.HandshakeTimeout = ...)` overrides found beyond `EnableDetailedErrors` in Battle `Program.cs:129`). Confirm whether the load test client must keep-alive.
13. **Frontend WebSocket transport vs. fallbacks.** SignalR negotiates Long Polling / Server-Sent Events fallbacks; under load these are much heavier than WebSocket. The `Kombats.Client/src/transport/` was not inspected here — confirm WebSocket is forced.
14. **Recovery worker scope.** `BattleRecoveryWorker` exists (`Battle Program.cs:179`) but its concrete behavior, scan cost, and interaction with `battle:active`/stale state at 1,000 ended battles was not traced.
15. **Outbox publication cadence.** `Outbox.QueryDelaySeconds = 1` (`MessagingOptions.cs:58`) — confirm under load whether burst-end-of-battle outbox flushes saturate Postgres.
16. **Is RabbitMQ a load-test bottleneck at the planned event rate?** With prefetch=32 / concurrency=8 per service replica, sustained throughput is bounded; a single broker process at 0.5 vCPU is the unknown.

---

*Generated against working tree at `f2432d9` on branch `main` (development is the actual main branch per the repo convention). All citations verified against the live source tree.*
