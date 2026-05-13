# SignalR Surface Map — Kombats

Read-only reconnaissance ahead of Chapter 3 (Redis backplane + multi-replica). Every claim references `file:line` against the working tree under `/Users/artemsorokin/Desktop/k/Kombats`.

## Section A — Hub inventory

Three production hubs across three services. (A fourth `TestChatHub` lives in `tests/Kombats.Bff/Kombats.Bff.Application.Tests/Relay/ChatHubRelayBehaviorTests.cs:303` and is not deployed.)

| # | Hub | Class declaration | Map route | Auth | Host service | Local port (launchSettings → docker) |
|---|---|---|---|---|---|---|
| 1 | `Kombats.Battle.Infrastructure.Realtime.SignalR.BattleHub` | `src/Kombats.Battle/Kombats.Battle.Infrastructure/Realtime/SignalR/BattleHub.cs:17` | `/battlehub` at `src/Kombats.Battle/Kombats.Battle.Bootstrap/Program.cs:187` | `[Authorize]` at `BattleHub.cs:16` | Battle (`Kombats.Battle.Bootstrap`) | `5003` (`src/Kombats.Battle/Kombats.Battle.Bootstrap/Properties/launchSettings.json:12`), container `8080` → host `5003` (`docker-compose.yml:225-226`) |
| 2 | `Kombats.Bff.Api.Hubs.BattleHub` | `src/Kombats.Bff/Kombats.Bff.Api/Hubs/BattleHub.cs:20` | `/battlehub` at `src/Kombats.Bff/Kombats.Bff.Bootstrap/Program.cs:349` | `[Authorize]` at `BattleHub.cs:19` | BFF (`Kombats.Bff.Bootstrap`) | `5000` (`src/Kombats.Bff/Kombats.Bff.Bootstrap/Properties/launchSettings.json:12`), container `8080` → host `5000` (`docker-compose.yml:133-134`) |
| 3 | `Kombats.Bff.Api.Hubs.ChatHub` | `src/Kombats.Bff/Kombats.Bff.Api/Hubs/ChatHub.cs:16` | `/chathub` at `src/Kombats.Bff/Kombats.Bff.Bootstrap/Program.cs:352` | `[Authorize]` at `ChatHub.cs:15` | BFF (`Kombats.Bff.Bootstrap`) | same as #2 — `5000` |
| 4 | `Kombats.Chat.Api.Hubs.InternalChatHub` | `src/Kombats.Chat/Kombats.Chat.Api/Hubs/InternalChatHub.cs:24` | `/chathub-internal` at `src/Kombats.Chat/Kombats.Chat.Bootstrap/Program.cs:262` | `[Authorize]` at `InternalChatHub.cs:23` | Chat (`Kombats.Chat.Bootstrap`) | `5004` (`src/Kombats.Chat/Kombats.Chat.Bootstrap/Properties/launchSettings.json:12`), container `8080` → host `5004` (`docker-compose.yml:257-258`) |

`AddSignalR` registrations:
- Battle: `src/Kombats.Battle/Kombats.Battle.Bootstrap/Program.cs:128-129` (detailed errors in Dev + `JsonStringEnumConverter`).
- BFF: `src/Kombats.Bff/Kombats.Bff.Bootstrap/Program.cs:301-305` (`JsonStringEnumConverter`).
- Chat: `src/Kombats.Chat/Kombats.Chat.Bootstrap/Program.cs:211` (no JSON customisation).

CSProjects referencing `Microsoft.AspNetCore.SignalR` directly: `src/Kombats.Bff/Kombats.Bff.Application/Kombats.Bff.Application.csproj`, `tests/Kombats.LoadTests/Kombats.LoadTests.csproj`, `tests/Kombats.Battle/Kombats.Battle.Api.Tests/...`, `tests/Kombats.Chat/Kombats.Chat.Api.Tests/...`. The hub-hosting services pull SignalR via the ASP.NET Core framework reference.

## Section B — Hub methods

### B.1 Battle service — `Kombats.Battle.Infrastructure.Realtime.SignalR.BattleHub`

Client-invokable:
- `JoinBattle(Guid battleId) : Task<BattleSnapshotRealtime>` — `BattleHub.cs:42`. Adds caller to `battle:{battleId}` group and returns a per-player snapshot.
- `SubmitTurnAction(Guid battleId, int turnIndex, string actionPayload) : Task` — `BattleHub.cs:66`. Delegates to `BattleTurnAppService.SubmitActionAsync`.

Lifecycle overrides:
- `OnConnectedAsync` — `BattleHub.cs:36`. Increments `KombatsMetrics.ActiveSignalRConnections` by 1.
- `OnDisconnectedAsync` — `BattleHub.cs:82`. Decrements `ActiveSignalRConnections`. **No `Groups.RemoveFromGroupAsync` call** — relies on SignalR's automatic group cleanup on disconnect.

Auth helper: `GetAuthenticatedUserId` at `BattleHub.cs:89`, reads `ClaimTypes.NameIdentifier` then `"sub"`.

### B.2 BFF — `Kombats.Bff.Api.Hubs.BattleHub`

Client-invokable:
- `JoinBattle(Guid battleId) : Task<object>` — `BattleHub.cs:31`. Forwards to `IBattleHubRelay.JoinBattleAsync`, opening a fresh downstream HubConnection to Battle's `/battlehub`.
- `SubmitTurnAction(Guid, int, string) : Task` — `BattleHub.cs:49`. Forwards to relay.

Lifecycle overrides:
- `OnConnectedAsync` — `BattleHub.cs:25`. Increments `ActiveSignalRConnections`.
- `OnDisconnectedAsync` — `BattleHub.cs:63`. Decrements; calls `IBattleHubRelay.DisconnectAsync` to tear down the downstream HubConnection.

### B.3 BFF — `Kombats.Bff.Api.Hubs.ChatHub`

Client-invokable:
- `JoinGlobalChat() : Task<object?>` — `ChatHub.cs:53`.
- `LeaveGlobalChat() : Task` — `ChatHub.cs:56`.
- `SendGlobalMessage(string content) : Task` — `ChatHub.cs:59`.
- `SendDirectMessage(Guid recipientPlayerId, string content) : Task<object?>` — `ChatHub.cs:62`.

All four are pure relay wrappers over `IChatHubRelay`, which holds an outbound `HubConnection` per frontend connection.

Lifecycle overrides:
- `OnConnectedAsync` — `ChatHub.cs:20`. **Opens** the downstream relay synchronously (calls `IChatHubRelay.ConnectAsync`); aborts the frontend connection if downstream fails. No metric increment here (unlike BFF BattleHub) — `OnConnectedAsync` for chat does NOT add to `ActiveSignalRConnections`.
- `OnDisconnectedAsync` — `ChatHub.cs:43`. Tears down downstream relay.

### B.4 Chat service — `Kombats.Chat.Api.Hubs.InternalChatHub`

Client-invokable (called by BFF's `ChatHubRelay` over an authenticated downstream HubConnection):
- `JoinGlobalChat() : Task<JoinGlobalChatResponse?>` — `InternalChatHub.cs:89`. Runs the join command then joins the `ChatGroups.Global` SignalR group.
- `LeaveGlobalChat() : Task` — `InternalChatHub.cs:113`. Removes from global group.
- `SendGlobalMessage(string content) : Task` — `InternalChatHub.cs:116`.
- `SendDirectMessage(Guid recipientPlayerId, string content) : Task<SendDirectMessageResponse?>` — `InternalChatHub.cs:135`.

Lifecycle overrides:
- `OnConnectedAsync` — `InternalChatHub.cs:33`. Joins the caller to `identity:{identityId}` group, runs `ConnectUserCommand`, starts a `HeartbeatScheduler` timer for this connection.
- `OnDisconnectedAsync` — `InternalChatHub.cs:59`. Stops the heartbeat, **explicitly** removes the connection from both `ChatGroups.Global` and `ChatGroups.ForIdentity`, runs `DisconnectUserCommand`.

### B.5 `IHubFilter` / `AddFilter<>`

`rg "IHubFilter|AddFilter<"` over the whole tree returned no matches. **No hub filters are registered anywhere.**

## Section C — Addressing patterns (CRITICAL)

Every server-to-client send. The first three patterns are the multi-replica break points; `Clients.Caller` from inside a hub method is safe because the hub instance already owns the connection.

### C.1 `Clients.Group(...)` — multi-replica unsafe without backplane

| # | file:line | Exact call | Context | Audience |
|---|---|---|---|---|
| C1 | `src/Kombats.Battle/Kombats.Battle.Infrastructure/Realtime/SignalR/SignalRBattleRealtimeNotifier.cs:44` | `_hubContext.Clients.Group($"battle:{battleId}").SendAsync(RealtimeEventNames.BattleReady, ...)` | External notifier (`IBattleRealtimeNotifier`), invoked from `BattleLifecycleAppService` which is called by `CreateBattleConsumer` (MassTransit) | per-battle group |
| C2 | `SignalRBattleRealtimeNotifier.cs:59` | `Clients.Group($"battle:{battleId}").SendAsync(TurnOpened, ...)` | External notifier, invoked from `BattleTurnAppService` (hub method **and** `TurnDeadlineWorker`) | per-battle group |
| C3 | `SignalRBattleRealtimeNotifier.cs:76` | `Clients.Group($"battle:{battleId}").SendAsync(TurnResolved, ...)` | Same — both hub method and worker paths | per-battle group |
| C4 | `SignalRBattleRealtimeNotifier.cs:93` | `Clients.Group($"battle:{battleId}").SendAsync(PlayerDamaged, ...)` | Same | per-battle group |
| C5 | `SignalRBattleRealtimeNotifier.cs:140` | `Clients.Group($"battle:{battleId}").SendAsync(BattleStateUpdated, ...)` | Same | per-battle group |
| C6 | `SignalRBattleRealtimeNotifier.cs:170` | `Clients.Group($"battle:{battleId}").SendAsync(BattleEnded, ...)` | Same | per-battle group |
| C7 | `src/Kombats.Chat/Kombats.Chat.Api/Hubs/SignalRChatNotifier.cs:10` | `hub.Clients.Group(ChatGroups.Global).SendAsync(GlobalMessageReceived, ...)` | External notifier, invoked from `SendGlobalMessageHandler` (hub method path) | global chat group |
| C8 | `SignalRChatNotifier.cs:13` | `hub.Clients.Group(ChatGroups.ForIdentity(recipientIdentityId)).SendAsync(DirectMessageReceived, ...)` | External notifier, invoked from `SendDirectMessageHandler` (hub method path) | per-identity group `identity:{guid}` |
| C9 | `SignalRChatNotifier.cs:17` | `hub.Clients.Group(ChatGroups.Global).SendAsync(PlayerOnline, ...)` | External notifier, invoked from `ConnectUserHandler` (hub method path) | global chat group |
| C10 | `SignalRChatNotifier.cs:20` | `hub.Clients.Group(ChatGroups.Global).SendAsync(PlayerOffline, ...)` | External notifier, invoked from `DisconnectUserHandler` (hub method path) **and** `PresenceSweepWorker` (hosted service) | global chat group |

### C.2 `Clients.Client(connectionId)` — multi-replica unsafe without backplane

| # | file:line | Exact call | Context | Audience |
|---|---|---|---|---|
| C11 | `src/Kombats.Bff/Kombats.Bff.Api/Hubs/HubContextBattleSender.cs:15` | `hubContext.Clients.Client(connectionId).SendCoreAsync(eventName, args, ct)` | `IFrontendBattleSender` impl, invoked from `BattleHubRelay` event handlers (downstream HubConnection callbacks running on the threadpool, outside any hub method scope) | single connection by id |
| C12 | `src/Kombats.Bff/Kombats.Bff.Api/Hubs/HubContextChatSender.cs:15` | `hubContext.Clients.Client(connectionId).SendCoreAsync(eventName, args, ct)` | `IFrontendChatSender` impl, invoked from `ChatHubRelay` event handlers and timeout/closed handlers (outside hub method scope) | single connection by id |

### C.3 `Clients.Caller` — safe (sender is the hub instance, on the replica owning the connection)

| # | file:line | Exact call | Context |
|---|---|---|---|
| C13 | `src/Kombats.Chat/Kombats.Chat.Api/Hubs/InternalChatHub.cs:161` | `Clients.Caller.SendAsync(ChatHubEvents.ChatError, payload, Context.ConnectionAborted)` | Hub method (`SendErrorAsync` helper called from `JoinGlobalChat`, `SendGlobalMessage`, `SendDirectMessage`) |

### C.4 Other patterns

`rg` for `Clients.All`, `Clients.User`, `Clients.Others`, `GroupExcept`, `OthersInGroup` returned **no matches** in production source. Only the test fixture `tests/Kombats.Bff/Kombats.Bff.Application.Tests/Relay/ChatHubRelayBehaviorTests.cs:328` uses `Clients.Caller`.

`Clients.User(...)` is not used — there is no `IUserIdProvider`, so per-user fan-out is implemented via groups (`identity:{guid}`) instead. See Section E.

### C.5 Summary of replica-boundary exposure

- **External-to-hub group sends:** 10 call sites (C1–C10), all in `IBattleRealtimeNotifier` and `IChatNotifier` implementations.
- **External-to-hub by-connection-id sends:** 2 call sites (C11, C12), both in BFF relay event handlers.
- **In-hub `Clients.Caller`:** 1 site (C13), safe by construction.

## Section D — Group membership lifecycle

### D.1 `Groups.AddToGroupAsync`

| # | file:line | Naming convention |
|---|---|---|
| D1 | `src/Kombats.Battle/Kombats.Battle.Infrastructure/Realtime/SignalR/BattleHub.cs:48` | `"battle:{battleId}"` (with Guid in default format) |
| D2 | `src/Kombats.Chat/Kombats.Chat.Api/Hubs/InternalChatHub.cs:43` | `ChatGroups.ForIdentity(identityId)` → `"identity:{guid:D}"` (`src/Kombats.Chat/Kombats.Chat.Api/Hubs/ChatGroups.cs:15`). Added in `OnConnectedAsync` so every connection joins its per-identity DM group automatically. |
| D3 | `InternalChatHub.cs:108` | `ChatGroups.Global` → `"global"` (`ChatGroups.cs:9`). Added inside `JoinGlobalChat` hub method on successful join. |

The BFF hubs add **no** group memberships — they are pure relays; group routing lives downstream.

### D.2 `Groups.RemoveFromGroupAsync`

| # | file:line | Removed group | Trigger |
|---|---|---|---|
| D4 | `src/Kombats.Chat/Kombats.Chat.Api/Hubs/InternalChatHub.cs:68` | `ChatGroups.Global` | `OnDisconnectedAsync` |
| D5 | `InternalChatHub.cs:69` | `ChatGroups.ForIdentity(identityId)` | `OnDisconnectedAsync` |
| D6 | `InternalChatHub.cs:114` | `ChatGroups.Global` | `LeaveGlobalChat` hub method |

Battle's `BattleHub.OnDisconnectedAsync` (`BattleHub.cs:82`) does **not** call `RemoveFromGroupAsync` for the `battle:{battleId}` group; SignalR's own per-connection group bookkeeping removes the connection on disconnect, but the group name itself persists. A connection that drops mid-battle simply loses its group membership when the server detects the disconnect — no application code runs.

### D.3 Mid-battle disconnect behaviour

- The frontend (`src/Kombats.Client/src/transport/signalr/battle-hub.ts:98`) and the load-test client (`tests/Kombats.LoadTests/SignalR/BattleHubClient.cs:83`) reconnect and call `JoinBattle` again, which re-adds the group on `BattleHub.cs:48`.
- Battle progression itself does NOT depend on group membership: `TurnDeadlineWorker` claims due battles from Redis and resolves them via `BattleTurnAppService` regardless of who is listening. Group membership only governs whether a particular replica's hub forwards the notifier event to a still-connected client.

## Section E — User → Connection mapping

### E.1 `IUserIdProvider`

`rg "IUserIdProvider"` returned **no production matches**. **There is no custom `IUserIdProvider` registered in any service.** Consequently `Clients.User(...)` would resolve via the default ASP.NET Core provider (the `NameIdentifier` claim) — but the code never calls it.

### E.2 Identity claim handling

Battle's `BattleHub.GetAuthenticatedUserId` (`src/Kombats.Battle/Kombats.Battle.Infrastructure/Realtime/SignalR/BattleHub.cs:89-101`) reads `ClaimTypes.NameIdentifier` then falls back to `"sub"`, parses to `Guid`. Chat's `InternalChatHub` uses the shared `ClaimsPrincipal.GetIdentityId()` extension at `src/Kombats.Common/Kombats.Abstractions/Auth/IdentityIdExtensions.cs:11-22`, which reads the same two claims and returns `Guid?`. Both reduce to the Keycloak `sub` claim.

### E.3 Custom userId → connectionId dictionaries

There is **no** `userId → connectionId` map. The two `ConcurrentDictionary` stores keyed on a SignalR connection id are different:

- `BattleHubRelay._connections : ConcurrentDictionary<string frontendConnectionId, BattleConnection>` at `src/Kombats.Bff/Kombats.Bff.Application/Relay/BattleHubRelay.cs:19`. Maps **frontend SignalR connection id → an outbound `HubConnection` to Battle**. Written on `JoinBattleAsync` (`BattleHubRelay.cs:246, 288`), removed on `DisconnectAsync` (`BattleHubRelay.cs:375`), iterated on `DisposeAsync` (`BattleHubRelay.cs:387`).
- `ChatHubRelay._connections : ConcurrentDictionary<string frontendConnectionId, ChatConnection>` at `src/Kombats.Bff/Kombats.Bff.Application/Relay/ChatHubRelay.cs:40`. Same shape for chat. Written on `ConnectAsync` (`ChatHubRelay.cs:128`), removed in the Closed handler / `DisconnectAsync` / timeout teardown (`ChatHubRelay.cs:157, 171, 195`).
- `HeartbeatScheduler._timers : ConcurrentDictionary<string connectionId, ITimer>` at `src/Kombats.Chat/Kombats.Chat.Api/Hubs/HeartbeatScheduler.cs:20`. Per-connection heartbeat timers used by `InternalChatHub` to refresh presence TTLs.

### E.4 In-process scope

All three dictionaries are **process-local** singletons (registered via `AddSingleton`). `BattleHubRelay` and `ChatHubRelay` are registered at `src/Kombats.Bff/Kombats.Bff.Bootstrap/Program.cs:320, 324`; the heartbeat scheduler at `src/Kombats.Chat/Kombats.Chat.Bootstrap/Program.cs:213`. They do not survive a process restart and are not shared across replicas. Per Chapter 3 framing: if a downstream Closed handler in one BFF replica wants to notify a frontend whose connection lives on a different BFF replica, today's `_connections` dictionary cannot find it — but that scenario does not arise as long as each frontend SignalR connection is sticky to the BFF replica that opened the downstream HubConnection. The replica-boundary risk is on the **inbound** hub side (Sections C and F), not these dictionaries.

## Section F — Senders from outside the Hub (MOST IMPORTANT)

Every `IHubContext<T>` injection point and what it does. Found via `rg -ln "IHubContext<"`.

### F.1 `IHubContext<Battle.Infrastructure.Realtime.SignalR.BattleHub>` consumers

- `Kombats.Battle.Infrastructure.Realtime.SignalR.SignalRBattleRealtimeNotifier` — `src/Kombats.Battle/Kombats.Battle.Infrastructure/Realtime/SignalR/SignalRBattleRealtimeNotifier.cs:17`. Injected via constructor (line 24). Registered Scoped at `src/Kombats.Battle/Kombats.Battle.Bootstrap/Program.cs:121`. Targets `Clients.Group($"battle:{battleId}")` — see C1–C6.
  - **Trigger inventory** (from `BattleTurnAppService` and `BattleLifecycleAppService`):
    - `CreateBattleConsumer.Consume` (MassTransit `IConsumer<CreateBattle>`, `src/Kombats.Battle/Kombats.Battle.Infrastructure/Messaging/Consumers/CreateBattleConsumer.cs:78`) → `BattleLifecycleAppService.HandleBattleCreatedAsync` → `NotifyBattleReadyAsync` + `NotifyTurnOpenedAsync` (`src/Kombats.Battle/Kombats.Battle.Application/UseCases/Lifecycle/BattleLifecycleAppService.cs:131-132`).
    - `BattleHub.SubmitTurnAction` (in-hub) → `BattleTurnAppService.SubmitActionAsync` → may resolve turn → notifier calls at `BattleTurnAppService.cs:391, 409, 419, 438, 582, 591, 599, 605`.
    - `TurnDeadlineWorker.ProcessClaimBasedTickAsync` (`src/Kombats.Battle/Kombats.Battle.Bootstrap/Workers/TurnDeadlineWorker.cs:77`) → `BattleTurnAppService.ResolveTurnAsync` → same notifier calls.
    - `BattleRecoveryWorker` — also invokes notifier paths via `BattleRecoveryService` (`tests/Kombats.Battle/Kombats.Battle.Application.Tests/Recovery/BattleRecoveryServiceTests.cs:26` confirms the service depends on `IBattleRealtimeNotifier`).

### F.2 `IHubContext<Chat.Api.Hubs.InternalChatHub>` consumers

- `Kombats.Chat.Api.Hubs.SignalRChatNotifier` — `src/Kombats.Chat/Kombats.Chat.Api/Hubs/SignalRChatNotifier.cs:7`. Primary constructor injection. Registered Singleton at `src/Kombats.Chat/Kombats.Chat.Bootstrap/Program.cs:212`. Targets `Clients.Group(...)` — see C7–C10.
  - **Trigger inventory**:
    - `InternalChatHub.JoinGlobalChat`/`SendGlobalMessage`/`SendDirectMessage`/`OnConnectedAsync` (in-hub, but the **send** goes through `SignalRChatNotifier` not `Clients.X`; from the SignalR backplane perspective each `IHubContext` send is still "external" to the hub method's own `Clients` proxy).
    - `PresenceSweepWorker.RunOnceAsync` (`src/Kombats.Chat/Kombats.Chat.Infrastructure/Workers/PresenceSweepWorker.cs:75`) — `IHostedService` running on a timer; calls `IChatNotifier.BroadcastPlayerOfflineAsync` for each identity it sweeps out of the presence ZSET. This is the **purest** "external sender" — no hub method, no consumer, no HTTP request behind it.
    - `PlayerCombatProfileChangedConsumer` (`src/Kombats.Chat/Kombats.Chat.Infrastructure/Messaging/Consumers/PlayerCombatProfileChangedConsumer.cs`) — runs `HandlePlayerProfileChangedHandler`; presently does not broadcast (unresolved from source — needs full handler read to confirm), but lives on the same `IHubContext` path if it ever does.
  - **Service-level note:** Chat's `BroadcastPlayerOnlineAsync` and `BroadcastPlayerOfflineAsync` from `ConnectUserHandler` / `DisconnectUserHandler` execute on the hub-method thread but address `Clients.Group("global")`. On multi-replica with no backplane, the broadcast still hits only the local replica's "global" group — sibling replicas' "global" members miss the event.

### F.3 `IHubContext<Bff.Api.Hubs.BattleHub>` consumers

- `Kombats.Bff.Api.Hubs.HubContextBattleSender` — `src/Kombats.Bff/Kombats.Bff.Api/Hubs/HubContextBattleSender.cs:11`. Primary constructor injection. Registered Singleton at `src/Kombats.Bff/Kombats.Bff.Bootstrap/Program.cs:309`. Targets `Clients.Client(connectionId)` — see C11.
  - **Trigger inventory**: invoked from inside `BattleHubRelay` (`src/Kombats.Bff/Kombats.Bff.Application/Relay/BattleHubRelay.cs:91, 108, 134, 152, 174, 214, 232, 294`) — these are downstream `HubConnection.On<>` callbacks (line 87, 103, 147, 193) and the downstream `Closed` handler (line 224). All run on the threadpool, not inside an inbound hub method.

### F.4 `IHubContext<Bff.Api.Hubs.ChatHub>` consumers

- `Kombats.Bff.Api.Hubs.HubContextChatSender` — `src/Kombats.Bff/Kombats.Bff.Api/Hubs/HubContextChatSender.cs:11`. Singleton registered at `src/Kombats.Bff/Kombats.Bff.Bootstrap/Program.cs:323`. Targets `Clients.Client(connectionId)` — see C12.
  - **Trigger inventory**: invoked from `ChatHubRelay` blind-relay event handlers (`src/Kombats.Bff/Kombats.Bff.Application/Relay/ChatHubRelay.cs:116`), the downstream `Closed` handler (`ChatHubRelay.cs:146`), and the timeout teardown (`ChatHubRelay.cs:286`). All external to inbound hub method scope.

### F.5 Total "external sender" call sites

12 distinct `SendAsync`/`SendCoreAsync` invocations live outside a hub method:
- 6 in `SignalRBattleRealtimeNotifier` (C1–C6) — reachable from MassTransit consumer, hub method, and two hosted services.
- 4 in `SignalRChatNotifier` (C7–C10) — reachable from hub methods and `PresenceSweepWorker`.
- 1 in `HubContextBattleSender` (C11) — reached 8 times from `BattleHubRelay` callbacks.
- 1 in `HubContextChatSender` (C12) — reached 3 times from `ChatHubRelay` callbacks.

## Section G — Messages sent during a battle (lifecycle trace)

A full battle, from `CreateBattle` arrival to `BattleEnded`. All sends use `IHubContext<Battle.Infrastructure.Realtime.SignalR.BattleHub>` targeting `battle:{battleId}` — i.e. the **Battle-service** hub, not the BFF. The BFF then **re-emits** each event to its frontend connection via `Clients.Client(connectionId)` from `HubContextBattleSender`.

### G.1 Battle service → its hub (the upstream events)

| Order | Event name | Sender file:line | Sending context | Recipient (upstream group) |
|---|---|---|---|---|
| 1 | `BattleReady` | `SignalRBattleRealtimeNotifier.cs:44-47` (sent via `BattleLifecycleAppService.cs:131`) | `CreateBattleConsumer` (MassTransit) | `battle:{battleId}` |
| 2 | `TurnOpened` (turnIndex=1) | `SignalRBattleRealtimeNotifier.cs:59-62` (sent via `BattleLifecycleAppService.cs:132`) | `CreateBattleConsumer` (MassTransit) | `battle:{battleId}` |
| 3 | `PlayerDamaged` (0..N per resolved turn) | `SignalRBattleRealtimeNotifier.cs:93-96` (sent via `BattleTurnAppService.cs:391, 582`) | `BattleHub.SubmitTurnAction` (hub method) or `TurnDeadlineWorker` (hosted service) | `battle:{battleId}` |
| 4 | `TurnResolved` (per resolved turn) | `SignalRBattleRealtimeNotifier.cs:76-79` (sent via `BattleTurnAppService.cs:409, 591`) | Hub method or `TurnDeadlineWorker` | `battle:{battleId}` |
| 5 | `TurnOpened` (turnIndex=N+1, for every continuing turn) | `SignalRBattleRealtimeNotifier.cs:59-62` (sent via `BattleTurnAppService.cs:599`) | Hub method or `TurnDeadlineWorker` | `battle:{battleId}` |
| 6 | `BattleStateUpdated` (after every turn close — continuing) | `SignalRBattleRealtimeNotifier.cs:140-143` (sent via `BattleTurnAppService.cs:605`) | Hub method or `TurnDeadlineWorker` | `battle:{battleId}` |
| 7 | `BattleEnded` (final turn) | `SignalRBattleRealtimeNotifier.cs:170-173` (sent via `BattleTurnAppService.cs:419`) | Hub method or `TurnDeadlineWorker` | `battle:{battleId}` |
| 8 | `BattleStateUpdated` (trailing post-end reconcile) | `SignalRBattleRealtimeNotifier.cs:140-143` (sent via `BattleTurnAppService.cs:438`) | Same as #7 | `battle:{battleId}` |

Order on the killing-blow turn (`BattleTurnAppService.cs:380-457`): damage → TurnResolved → BattleEnded → trailing BattleStateUpdated. Order on continuing turns (`BattleTurnAppService.cs:577-610`): damage → TurnResolved → TurnOpened → BattleStateUpdated.

### G.2 BFF relay → frontend (the downstream re-emit)

The BFF holds a downstream `HubConnection` per frontend connection (Section E.3). Its `On<>` handlers re-emit raw events 1:1 plus two **synthesised** events:

| Event | BFF emitter file:line | Trigger |
|---|---|---|
| `BattleReady`, `TurnOpened`, `PlayerDamaged` (blind relay) | `src/Kombats.Bff/Kombats.Bff.Application/Relay/BattleHubRelay.cs:87-99` | Each upstream event of that name |
| `TurnResolved` (raw relay) | `BattleHubRelay.cs:108` | Upstream `TurnResolved` |
| `BattleFeedUpdated` (synthesised narration) | `BattleHubRelay.cs:134, 174, 294` | After raw `TurnResolved`, after raw `BattleEnded`, and once at `JoinBattle` |
| `BattleStateUpdated` (raw relay) | `BattleHubRelay.cs:214` | Upstream `BattleStateUpdated` |
| `BattleEnded` (raw relay) | `BattleHubRelay.cs:152` | Upstream `BattleEnded` |
| `BattleConnectionLost` (synthesised) | `BattleHubRelay.cs:232` | Downstream `connection.Closed` |

All BFF emits go through `HubContextBattleSender.SendAsync` (C11) → `Clients.Client(frontendConnectionId)`.

### G.3 Visible miss on a replica boundary

If a second Battle replica resolves the turn (because the deadline worker on replica B claimed the lease) while the player's connection is parked on replica A: events 3–8 above fire from replica B's `IHubContext` but reach only replica B's local `battle:{battleId}` group. The player on replica A sees nothing — no damage, no turn resolution, no battle end. The BFF's downstream HubConnection is unaffected only if it happens to be open against the same replica; the BFF has no replica affinity logic, so the boundary risk lives at the Battle hub.

The same logic applies to chat: `PresenceSweepWorker` running on Chat replica B broadcasting `PlayerOffline` to the local `global` group (C10) misses everyone whose connection is parked on Chat replica A.

## Section H — Client side (brief)

### H.1 React frontend

- Battle hub URL: `${config().bff.baseUrl}/battlehub` at `src/Kombats.Client/src/transport/signalr/battle-hub.ts:95`.
- Chat hub URL: `${config().bff.baseUrl}/chathub` at `src/Kombats.Client/src/transport/signalr/chat-hub.ts:72`.

Both use `accessTokenFactory` for OIDC and `withAutomaticReconnect`. The frontend never speaks to Battle or Chat directly — always via BFF.

### H.2 Load-test VirtualPlayer

- Hub URL: `${BffBaseUrl}{BattleHubPath}` where `BattleHubPath` defaults to `/battlehub` (`tests/Kombats.LoadTests/Configuration/LoadTestOptions.cs:14`, `tests/Kombats.LoadTests/appsettings.json:4`). Connection setup at `tests/Kombats.LoadTests/SignalR/BattleHubClient.cs:50-68`. There is **no** chat hub client in the load tests.

### H.3 Joining the relevant group

Server-side join is **explicit** and client-initiated:

- Battle: the client invokes the hub method `JoinBattle(battleId)` (`battle-hub.ts:81-83` for the SPA via TanStack/Zustand; `BattleHubClient.cs:83` for VirtualPlayer; on the BFF hub at `src/Kombats.Bff/Kombats.Bff.Api/Hubs/BattleHub.cs:31`). The actual `Groups.AddToGroupAsync` happens deep down in the Battle service hub at `BattleHub.cs:48`, after BFF forwards the call through its downstream `HubConnection.InvokeAsync("JoinBattle", ...)` (`BattleHubRelay.cs:258`).
- Chat: `JoinGlobalChat()` is invoked explicitly by the client (`src/Kombats.Client/src/transport/signalr/chat-hub.ts` — confirmed by ChatHub method at `src/Kombats.Bff/Kombats.Bff.Api/Hubs/ChatHub.cs:53`). The per-identity DM group is joined **automatically** in `InternalChatHub.OnConnectedAsync` (`InternalChatHub.cs:43`), not by client invocation.

## Section I — Existing SignalR observability

### I.1 Custom metrics

All custom instruments live on the per-service `Kombats.{serviceName}` Meter (`src/Kombats.Common/Kombats.Observability/KombatsMetrics.cs:43`). SignalR-related entries:

| Metric | Type | file:line of definition | Description |
|---|---|---|---|
| `active_signalr_connections` | `UpDownCounter<long>` | `src/Kombats.Common/Kombats.Observability/KombatsMetrics.cs:55-58` | "SignalR connections currently attached to this process's hub." |
| `downstream_hub_connections` | `UpDownCounter<long>` | `KombatsMetrics.cs:60-63` | "Outbound SignalR client connections held open by this process." (BFF-only — count of `BattleHubRelay._connections` entries.) |

No other SignalR-named metrics exist. `rg -i "signalr"` on `Meter`/`CreateCounter`/`CreateUpDownCounter` returns only these two and the comment block at `KombatsMetrics.cs:24-31`. Built-in ASP.NET Core SignalR meter `Microsoft.AspNetCore.Http.Connections` is not explicitly added — `AddAspNetCoreInstrumentation()` at `KombatsObservabilityExtensions.cs:69` will pick up the standard ASP.NET Core http instrumentation, but I did not see an explicit `.AddMeter("Microsoft.AspNetCore.SignalR.Server")` line. Unresolved: whether the default AspNetCore instrumentation already enables the SignalR meter in net10.

### I.2 Increment/decrement sites

| Counter | Increment | Decrement |
|---|---|---|
| `active_signalr_connections` (Battle) | `src/Kombats.Battle/Kombats.Battle.Infrastructure/Realtime/SignalR/BattleHub.cs:38` (in `OnConnectedAsync`) | `BattleHub.cs:84` (in `OnDisconnectedAsync`) |
| `active_signalr_connections` (BFF) | `src/Kombats.Bff/Kombats.Bff.Api/Hubs/BattleHub.cs:27` (in `OnConnectedAsync`) | `Bff/.../BattleHub.cs:65` (in `OnDisconnectedAsync`) |
| `active_signalr_connections` (BFF ChatHub) | **No increment** found in `src/Kombats.Bff/Kombats.Bff.Api/Hubs/ChatHub.cs` — only the Battle hubs are instrumented. Same for `InternalChatHub` (no `_metrics` field). | n/a |
| `downstream_hub_connections` (BFF) | `src/Kombats.Bff/Kombats.Bff.Application/Relay/BattleHubRelay.cs:247` (in `JoinBattleAsync`) | `BattleHubRelay.cs:311` (failure path), `BattleHubRelay.cs:377` (in `DisconnectAsync`) |
| `downstream_hub_connections` (Chat relay) | **Not instrumented** — `ChatHubRelay` never touches `KombatsMetrics`. | n/a |

### I.3 Per-replica vs aggregated

Each `KombatsMetrics` is constructed once in process (`KombatsObservabilityExtensions.cs:35` registers it as a singleton). The `Meter` name is `Kombats.{serviceName}` — same name across replicas, but the OTel resource attribute `service.name` (`KombatsObservabilityExtensions.cs:46`) plus the deployment environment differ per process. The Prometheus dashboard at `observability/grafana/dashboards/kombats-overview.json:85-103` confirms the intent by querying `sum by (service) (active_signalr_connections)` — i.e. it aggregates **across replicas of the same service**.

So with multi-replica:
- A single Prometheus time series per `(service, replica)` pair will be emitted by the OTLP exporter.
- The PromQL in the existing dashboard sums them by `service`, masking per-replica skew. To see "is one replica getting all the connections", a panel with `active_signalr_connections` un-aggregated (or grouped by `instance` / `pod`) is needed for Chapter 3 — flagging this for the load-test plan but **not changing it**.

The "Battle vs BFF" segmentation is intentional (`KombatsMetrics.cs:24-26` comment): both services name the counter `active_signalr_connections` and segment by `service.name`.

## Section J — Author's flagging

### J.1 First-to-break call sites on multi-replica without a backplane

1. **`SignalRBattleRealtimeNotifier` group sends on the killing-blow path** — `src/Kombats.Battle/Kombats.Battle.Infrastructure/Realtime/SignalR/SignalRBattleRealtimeNotifier.cs:44, 59, 76, 93, 140, 170`. The lease-claim model (`TurnDeadlineWorker` at `src/Kombats.Battle/Kombats.Battle.Bootstrap/Workers/TurnDeadlineWorker.cs:56`) explicitly distributes turn resolution across whichever replica wins the Redis claim. If replica B claims a battle while both players' SignalR connections are pinned to replica A, **every** event in Section G.1 fires on B's `battle:{battleId}` group, which is empty on B — A's clients see no damage, no TurnResolved, no BattleEnded. The players will hit `PerBotTimeout` (`VirtualPlayer.cs:43`) or wait indefinitely. This is the highest-impact break because the worker is the dominant resolver path under load.

2. **`SignalRChatNotifier` global broadcasts from `PresenceSweepWorker`** — `src/Kombats.Chat/Kombats.Chat.Api/Hubs/SignalRChatNotifier.cs:20` invoked from `src/Kombats.Chat/Kombats.Chat.Infrastructure/Workers/PresenceSweepWorker.cs:75`. The sweeper runs **independently on each Chat replica** on a timer. It ZREMs presence atomically (so only one replica per identity broadcasts), but the broadcast goes to that replica's local `global` group — clients sitting on other replicas never see the `PlayerOffline`. Direct messages (`SignalRChatNotifier.cs:13`) have the same shape: a DM to user X reaches X only if X's connection is on the same Chat replica that ran `SendDirectMessageHandler`. With two replicas it's a coin flip per message.

3. **`CreateBattleConsumer` → `BattleReady` / `TurnOpened`** — `src/Kombats.Battle/Kombats.Battle.Application/UseCases/Lifecycle/BattleLifecycleAppService.cs:131-132`. MassTransit dispatches `CreateBattle` to one replica's consumer. If both players' SignalR connections are on a different Battle replica, the initial `BattleReady` and turn-1 `TurnOpened` never arrive at the clients, so neither player sees the arena open. The frontend has a `JoinBattle` retry loop (`battle-hub.ts:27`, mirrored at `BattleHubClient.cs:18`) that may paper over a delayed `BattleReady` because `JoinBattle` calls `GetBattleSnapshotForPlayerAsync` and returns synchronously — but `TurnOpened` for turn 1 is **only** delivered via the group broadcast (it is not in the snapshot's deadline at the time of the in-band response under all races), so this is a real visible gap.

(Honourable mention: the BFF's `HubContextBattleSender.SendAsync` at `HubContextBattleSender.cs:15` targets `Clients.Client(connectionId)`. With BFF on multiple replicas and no backplane, this only works if the frontend connection happens to be on the same BFF replica as the one holding the downstream HubConnection. Today that's guaranteed because `BattleHubRelay` opens the downstream connection in the SAME `OnConnectedAsync`/`JoinBattle` call as the inbound connection — they always live in the same process. So this is structurally safe even without a backplane, **as long as** session affinity routes the same client back to the same BFF replica for the duration of the WebSocket. It's worth confirming the ingress is sticky for WebSockets in Chapter 3 deployment.)

### J.2 Ambiguity (unresolved from source)

- **`IUserIdProvider` defaulting:** with no custom provider registered, the SignalR default reads `ClaimTypes.NameIdentifier` (which in Battle/Chat collapses to the Keycloak `sub`). The codebase never calls `Clients.User(...)`, so this is moot today — but Chapter 3 should not introduce `Clients.User` without a custom provider that aligns with `IdentityIdExtensions.GetIdentityId` (`src/Kombats.Common/Kombats.Abstractions/Auth/IdentityIdExtensions.cs:11`). **Unresolved from source**: whether net10's default `IUserIdProvider` returns `sub` or `NameIdentifier` when both are present. Resolution: read `Microsoft.AspNetCore.SignalR.DefaultUserIdProvider` source, or write a probe.
- **Default ASP.NET Core SignalR meter:** Section I.1 — unclear if `AddAspNetCoreInstrumentation()` pulls in the `Microsoft.AspNetCore.SignalR.Server` meter under net10. Resolution: hit a running service's `/metrics`/OTLP endpoint and grep for `signalr_`.
- **`BattleRecoveryWorker` exact notify path:** `src/Kombats.Battle/Kombats.Battle.Bootstrap/Workers/BattleRecoveryWorker.cs` not opened in this pass — it depends on `IBattleRealtimeNotifier` (per test) but the exact events it fires on recovery are not inventoried here. Likely a subset of Section G.1. Resolution: read the worker source.
- **`Microsoft.AspNetCore.SignalR.Server` default meter scrape interval:** `KombatsObservabilityExtensions.cs:86` overrides the OTel periodic export to whatever `OpenTelemetry:MetricExportIntervalMs` is set to, but the default of 60s (`KombatsObservabilityExtensions.cs:24`) is too coarse to catch transient SignalR connection churn during a sub-30s smoke test. Resolution: check `OBSERVABILITY_DIAGNOSIS.md` and the dev appsettings — outside the scope of this map.

### J.3 Concerns noticed in passing (not backplane-related)

- **BFF `ChatHub` does not increment `active_signalr_connections`** — `src/Kombats.Bff/Kombats.Bff.Api/Hubs/ChatHub.cs:20-51`. Unlike the BFF `BattleHub`, neither `OnConnectedAsync` nor `OnDisconnectedAsync` touch `KombatsMetrics`. Chat connections are invisible to the SignalR connection gauge from the BFF side. Same for `InternalChatHub`.
- **No `Groups.RemoveFromGroupAsync` for `battle:{battleId}` on disconnect** — `src/Kombats.Battle/Kombats.Battle.Infrastructure/Realtime/SignalR/BattleHub.cs:82`. SignalR's automatic per-connection group cleanup handles correctness, but if a backplane is added, the empty group name lingers in the backplane index until both endpoints have evicted it. Worth checking whether Microsoft.AspNetCore.SignalR.StackExchangeRedis cleans these up.
- **`BattleHub` (Battle service) requires manual `GetAccessToken`** at `src/Kombats.Bff/Kombats.Bff.Api/Hubs/BattleHub.cs:74-93` — pulls JWT from header or `access_token` query string, throws `HubException` otherwise. Works but couples the BFF hub to a known WebSocket JWT convention. Not a backplane concern.
- **`ChatHubRelay` access token is captured at connect time** (`src/Kombats.Bff/Kombats.Bff.Application/Relay/ChatHubRelay.cs:86-90`) — long-lived chat connections will fail downstream when the JWT expires. The code comment explicitly flags this for future hardening. Not a backplane concern but will interact with longer load-test runs.
- **Hub surface size:** 3 production hubs across 3 services, 2 of those hubs are pure relays inside a single BFF process. The inventory is small enough to migrate to a backplane in one Chapter 3 PR; flagged here as "not 5+ hubs across 3+ services, so plan size is manageable".