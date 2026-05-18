# Kombats — Learning Map for Interview Prep

> **Purpose.** Reverse-engineering study plan for the owner of this codebase. The system was built with heavy AI assistance: the owner owns the architectural decisions but does not own the line-level details. This map says **where each interview-relevant concept lives**, **how deep the implementation actually is**, **the single best file to read**, **what an interviewer can hook onto in *this* code**, and **where shallow understanding will most likely show**.
>
> Builds on `docs/kombats-recon-overview.md` (top-level structure) — read that first.
>
> Honest grading: SHALLOW and ABSENT are used freely. No inflation.

---

## A. C# / .NET language & runtime (as actually used)

**1. Where it lives**
- `src/Kombats.Battle/Kombats.Battle.Application/UseCases/Turns/BattleTurnAppService.cs` — async orchestration, idempotent re-entry, `CancellationToken` discipline.
- `src/Kombats.Bff/Kombats.Bff.Application/Relay/BattleHubRelay.cs` — `IAsyncDisposable`, sealed records, `ConcurrentDictionary`, exception-fan-out on async callbacks.
- `src/Kombats.Common/Kombats.Abstractions/` — `Result`/`Error` records, `ICommand`/`IQuery` markers, nullable-enabled.
- `src/Kombats.Players/Kombats.Players.Domain/Entities/Character.cs` — domain methods and language-level invariants.
- `Directory.Build.props` — `Nullable=enable`, `TreatWarningsAsErrors`, `LangVersion`.

**2. Depth rating: MODERATE.** Idiomatic modern C# (records, primary constructors in workers, `sealed`, `Result`/`Error`, file-scoped namespaces, `internal sealed` consumers, `IAsyncDisposable`). No `Span<T>`/`Memory<T>`/`unsafe`. No `IAsyncEnumerable` in production paths. `ConfigureAwait` is not used (correct in ASP.NET Core — synchronization context is absent).

**3. Best file:** `BattleTurnAppService.cs` — shows real cancellation, exception swallow for best-effort calls (early resolution, turn-history persist), and the consequence-aware decision to *not* propagate `CancellationToken` to a downstream `InvokeAsync` in `BattleHubRelay.SubmitTurnActionAsync` (lines 351–384). That snippet is the strongest "I know what I did" anecdote.

**4. Interview hooks**
- `BattleHubRelay.SubmitTurnActionAsync` deliberately drops the caller's `CancellationToken` and passes `CancellationToken.None` downstream. Why? (Answer: action is already committed to Redis; cancelling only loses the ack, not the side effect.)
- `MatchmakingLeaseService.TryExecuteUnderLeaseAsync` builds a linked `CancellationTokenSource` from `stoppingToken` + a `leaseLostSource`. Walk me through how a lost lease aborts the in-progress callback.
- `Result`/`Error` pattern is used instead of throwing for domain failures, but `InvalidOperationException` is thrown for hub-level violations in `BattleHub.SubmitTurnAction`. Where's the line?
- Why are MassTransit consumers `internal sealed` rather than `public`?

**5. Likely gaps**
- The difference between `ValueTask` and `Task` — codebase uses `Task` exclusively.
- Why a `linkedCancellation` is needed in `MatchmakingLeaseService` instead of just observing both tokens manually.
- That `sealed` is a runtime devirtualization hint, not just style.
- The "best-effort" exception-swallow pattern in `BattleHubRelay` looks lazy in isolation; explaining *which* failures it's protecting against (downstream SignalR closed mid-broadcast) takes specific knowledge.

---

## B. Concurrency & multithreading

**1. Where it lives**
- `src/Kombats.Matchmaking/Kombats.Matchmaking.Infrastructure/Redis/MatchmakingLeaseService.cs` — full distributed lease-with-renewal pattern.
- `src/Kombats.Matchmaking/Kombats.Matchmaking.Infrastructure/Redis/RedisLeaseLock.cs` — `SET NX PX` + Lua check-and-extend / check-and-delete.
- `src/Kombats.Battle/Kombats.Battle.Infrastructure/State/Redis/RedisScripts.cs` — `StoreActionAndCheckBothSubmittedScript` (first-write-wins via `SET NX`), `ClaimDueBattlesScript` (lock-per-battle-turn).
- `src/Kombats.Bff/Kombats.Bff.Application/Relay/BattleHubRelay.cs` — `ConcurrentDictionary<string, BattleConnection>` keyed by frontend connection id.
- `src/Kombats.Chat/Kombats.Chat.Infrastructure/Redis/RedisRateLimiter.cs` — Redis `INCR`+`EXPIRE` window counter with in-memory `ConcurrentDictionary` fallback (`AddOrUpdate`).
- 7 workers (`Kombats.Battle.Bootstrap/Workers/`, `Kombats.Matchmaking.Bootstrap/Workers/`, `Kombats.Chat.Infrastructure/Workers/`) — `BackgroundService` + adaptive backoff in `TurnDeadlineWorker`.

**2. Depth rating: DEEP.** This is the strongest interview surface in the codebase. The lease renewal loop, the ZSET-based deadline claim with per-turn Lua-acquired locks, and the rate-limiter's hot-path fallback are all non-trivial.

**3. Best file:** `MatchmakingLeaseService.cs` (146 lines). It is small enough to memorize and covers: lease acquisition, renewal at TTL/3, linked-token cancellation on lease loss, a deliberate comment explaining why `Task.Delay` must bind to *both* tokens (otherwise end-of-tick cleanup waits up to RenewalIntervalMs), and best-effort release. Second-best: `RedisScripts.cs` `ClaimDueBattlesScript`.

**4. Interview hooks**
- "Why TTL/3 for renewal?" Answer must distinguish from "TTL/2 is also valid" — the trade-off is missed renewals vs. clock skew tolerance.
- `RenewLeaseAsync` returns `0` if the value doesn't match. Walk me through the race where that happens. (Answer: GC pause / network blip pushed past TTL, another instance took the lock, our renewal sees a different value.)
- `ClaimDueBattlesScript` postpones the ZSET score by `leaseWindowMs` *before* the worker processes the turn. Why? (Crash recovery — if the worker dies mid-tick, the lock expires (`PX`) *and* the ZSET puts it back on the radar at the same horizon. Without that postpone, two workers could race the same turn the moment the lock expires.)
- `BattleTurnAppService.ResolveTurnAsync` handles "stuck in Resolving" by re-attempting (line 247). What failure does this protect against? (Process crash after `TryMarkTurnResolvingAsync` but before `MarkTurnResolvedAndOpenNextAsync` — `BattleRecoveryWorker` re-enters this path.)
- `RedisRateLimiter` catches an explicit grab-bag (`RedisException or RedisTimeoutException or InvalidOperationException or ObjectDisposedException`). Why so many? (Each one is the symptom of a different teardown path inside the StackExchange.Redis pipe.)

**5. Likely gaps**
- The owner can probably explain *what* the lease does but may not articulate *why* it's a lease and not a Redlock-style multi-node algorithm. Answer: single Redis, primary/replica failover handled by Sentinel, single lock is correct here. Redlock is for *independent* Redis masters.
- Why the renewal loop binds `Task.Delay` to a *linked* CTS (so `Cancel()` shortens the wait) — the comment in the code explains it; the owner should be able to recreate that explanation.
- Difference between `SemaphoreSlim` (per-process), `lock` (per-process, sync), and the Redis lease (cross-process). `SemaphoreSlim` does not appear in production paths; `lock` is rare.
- `ConcurrentDictionary.AddOrUpdate` semantics: the update factory can run more than once on contention. The rate-limiter fallback relies on this being correct; the owner should be able to defend it.

---

## C. Dependency Injection & service lifetimes

**1. Where it lives**
- `src/Kombats.Bff/Kombats.Bff.Bootstrap/Program.cs` (354 lines) — exhaustive Polly resilience wiring per typed HttpClient, four downstream services.
- `src/Kombats.Battle/Kombats.Battle.Bootstrap/Program.cs` — DbContext, Redis multiplexer (singleton), SignalR backplane, `BackgroundService` registration, options pattern.
- `src/Kombats.Matchmaking/Kombats.Matchmaking.Bootstrap/Program.cs` — `RedisLeaseLock` and `MatchmakingLeaseService` as **singletons**, `InstanceIdService` as singleton (so the lease value is process-stable).
- `src/Kombats.Common/Kombats.Messaging/DependencyInjection/MessagingServiceCollectionExtensions.cs` — central MassTransit registration with EF outbox.

**2. Depth rating: MODERATE.** Standard patterns done carefully: `IConnectionMultiplexer` as singleton (correct), `DbContext` scoped (`AddDbContext`), `IClock` singleton, MassTransit consumers default-scoped, `BackgroundService`s acquire their own scope via `IServiceScopeFactory.CreateScope()` (the textbook fix for the "scoped-from-singleton" anti-pattern — see `TurnDeadlineWorker.ProcessClaimBasedTickAsync`).

**3. Best file:** `src/Kombats.Battle/Kombats.Battle.Bootstrap/Program.cs`. Compact (~200 lines) and shows every lifetime category, the options pattern with `ValidateOnStart()`, health checks, OpenTelemetry registration with the explicit comment that Redis tracing must come *after* the `IConnectionMultiplexer` registration.

**4. Interview hooks**
- Why is `IConnectionMultiplexer` registered as a singleton but `IBattleStateStore` is scoped?
- `MatchmakingLeaseService` is a singleton, but inside `MatchmakingPairingWorker` you build a scope per tick and resolve `ICommandHandler<ExecuteMatchmakingTickCommand,...>` from it. Why two different lifetimes interacting?
- The options pattern: `BattleRulesetsOptions` uses `.Bind(...).Validate(RulesetsOptionsValidator.Validate).ValidateOnStart()`. What does `ValidateOnStart` actually do?
- Why is `IClock` a singleton with `SystemClock` while *everything else* in the application layer is scoped?

**5. Likely gaps**
- The exact lifetime rules: when does scope get created for incoming HTTP requests, how is scope-per-tick set up in workers, why singletons can't capture scoped dependencies.
- `AddHttpClient<TClient>` builds a typed factory with `IHttpClientFactory` — the lifetime story of the underlying `HttpMessageHandler` (kept ~2 min default) is non-obvious and a classic interview question.
- Difference between `services.AddSingleton<T>` and `services.AddSingleton<T>(sp => ...)` (factory) — the Redis multiplexer uses the factory form to call `ConnectionMultiplexer.Connect(redisConfig)` after parsing.

---

## D. ASP.NET Core internals

**1. Where it lives**
- `src/Kombats.Bff/Kombats.Bff.Bootstrap/Program.cs` — middleware ordering: HttpsRedirection → SerilogRequestLogging → CORS → Authentication → Authorization → ExceptionHandlingMiddleware → endpoints → `MapHub`.
- `src/Kombats.Battle/Kombats.Battle.Bootstrap/Program.cs` — same pattern + `UseExceptionHandler` with RFC 7807 ProblemDetails.
- `src/Kombats.Bff/Kombats.Bff.Api/Endpoints/` and `src/Kombats.*/Kombats.*.Api/Endpoints/` — minimal-API endpoint classes (assembly-scanned via `AddEndpoints(apiAssembly)`).
- BFF aggregation: `src/Kombats.Bff/Kombats.Bff.Application/Composition/GameStateComposer.cs` — parallel HTTP fan-out to Players + Matchmaking.
- `src/Kombats.Bff/Kombats.Bff.Application/Clients/JwtForwardingHandler.cs` — `DelegatingHandler` for service-to-service auth.

**2. Depth rating: MODERATE.** Pure minimal-API style (no MVC controllers), endpoint-per-class with a registration scanner. JwtBearer is configured with a non-trivial bit: `OnMessageReceived` reads `access_token` from the query string for SignalR-over-WebSocket paths (`/battlehub`, `/chathub`). That hook is the most interview-worthy ASP.NET detail in BFF.

**3. Best file:** `src/Kombats.Bff/Kombats.Bff.Bootstrap/Program.cs`. Reading top-to-bottom you see the full pipeline order, Polly resilience (timeout → retry → circuit breaker → timeout-per-attempt), and the SignalR-WS-token quirk.

**4. Interview hooks**
- Why does `UseAuthentication()` come before `UseAuthorization()` and what happens if you swap them?
- The JwtBearer `OnMessageReceived` handler. Why is it needed for `/battlehub` and `/chathub`? (Browsers can't set headers on WebSocket upgrades; SignalR clients smuggle the token as `access_token` query param.)
- BFF builds `AddResilienceHandler` with `ShouldHandle` filtering retries to GET only. Why not retry POST? (Idempotency — Matchmaking `JoinQueue` POST is *not* idempotent at the HTTP layer.)
- BFF has no `Domain` and no `Infrastructure` project. Defend that layering choice. (Pure aggregator; downstream services are its "database.")

**5. Likely gaps**
- Middleware execution order and why endpoint routing is special (middleware that runs *after* endpoint selection but before the endpoint executes is registered between `UseRouting()` and `UseEndpoints()` — except this codebase uses the new top-level routing where that's implicit).
- The Polly v8 `ResiliencePipelineBuilder` semantics: a chained timeout-retry-CB-timeout has two distinct timeouts (overall + per-attempt). Worth practicing aloud.
- That `AddHttpMessageHandler<JwtForwardingHandler>` runs on every retry attempt — not once per request.

---

## E. EF Core & data access

**1. Where it lives**
- `src/Kombats.Battle/Kombats.Battle.Infrastructure/Data/DbContext/BattleDbContext.cs` (and equivalents in `Players`, `Matchmaking`, `Chat`).
- `src/Kombats.Players/Kombats.Players.Infrastructure/Persistence/EF/Migrations/` — 8 migrations incl. `20260407054312_AddOutboxEntities.cs`, `20260408062301_RemoveLegacyCustomOutboxTable.cs`.
- `src/Kombats.Players/Kombats.Players.Infrastructure/Persistence/Repository/InboxRepository.cs` — `AsNoTracking()` for the existence check, tracked add for the dedup row.
- `Players`/`Matchmaking`/`Battle`/`Chat`: each has its own `DesignTimeDbContextFactory.cs`.
- Connection string + Npgsql config: every `Program.cs` registers `UseNpgsql(...).UseSnakeCaseNamingConvention().ReplaceService<IHistoryRepository, SnakeCaseHistoryRepository>()`.

**2. Depth rating: MODERATE.** Standard "schema per service" with code-first migrations, snake-case naming convention, no shared DbContext. Migrations are applied by a separate `Kombats.Migrator` Job (see Bicep `migrator-job.bicep`) — explicitly forbidden to run at startup (AD-13: `// NOTE: No Database.MigrateAsync() on startup`). No EF projections, no compiled queries, no DbContext pooling. Repositories are thin and per-aggregate.

**3. Best file:** `src/Kombats.Players/Kombats.Players.Infrastructure/Persistence/Repository/InboxRepository.cs` (28 lines). Tiny, but shows the exact `AsNoTracking` choice and where tracking *is* used. Pair it with `HandleBattleCompletedCommand` (Application layer) to see the inbox dedup → publish → `SaveChangesAsync` flow.

**4. Interview hooks**
- Why does `IsProcessedAsync` use `AsNoTracking` but `AddProcessedAsync` doesn't?
- Migrations run from a separate Job. What's the cost? (No automatic rollback on bad migration; deploy order matters.)
- Each service has its own schema (`__ef_migrations_history` is namespaced). Why? (Service ownership, blast radius, can't accidentally `JOIN` across services in code.)
- `npgsql.EnableRetryOnFailure()` is on. What does this *not* retry? (Anything inside a user-controlled transaction.)

**5. Likely gaps**
- The change tracker: when does `SaveChangesAsync` flush, what happens to entities loaded with `AsNoTracking`, when do navigation property changes get picked up.
- That `UseEntityFrameworkOutbox<TDbContext>` mutates the DbContext's `SaveChangesAsync` to also flush an outbox row — this is **the** mechanism gluing the messaging story to the persistence story, and the owner needs to be able to explain it without saying "MassTransit handles it."
- The N+1 problem isn't really visible in this codebase (small aggregates, repository per aggregate). An interviewer may probe for it generically — be ready.

---

## F. Messaging & distributed systems

**1. Where it lives**
- `src/Kombats.Common/Kombats.Messaging/DependencyInjection/MessagingServiceCollectionExtensions.cs` — **the central file**. Registers `AddEntityFrameworkOutbox<TDbContext>` with `UsePostgres()` + `UseBusOutbox()`, exponential retry, optional delayed redelivery, message-name conventions via `EntityNameConvention`, endpoint formatter `CombatsEndpointNameFormatter`.
- Producer side: `Kombats.Players.Infrastructure/Messaging/MassTransitCombatProfilePublisher.cs`, `Kombats.Matchmaking.Infrastructure/Messaging/MassTransitCreateBattlePublisher.cs`, `Kombats.Battle.Infrastructure/Messaging/Publisher/MassTransitBattleEventPublisher.cs`.
- Consumer side: `Kombats.Battle.Infrastructure/Messaging/Consumers/CreateBattleConsumer.cs`, `Kombats.Matchmaking.Infrastructure/Messaging/Consumers/{BattleCreatedConsumer, BattleCompletedConsumer, PlayerCombatProfileChangedConsumer}.cs`, `Kombats.Players.Infrastructure/Messaging/Consumers/BattleCompletedConsumer.cs`, `Kombats.Chat.Infrastructure/Messaging/Consumers/PlayerCombatProfileChangedConsumer.cs`.
- Inbox: `Kombats.Players.Application/Abstractions/IInboxRepository.cs`, `Kombats.Players.Infrastructure/Persistence/Repository/InboxRepository.cs`. Used in `HandleBattleCompletedCommand`.
- Contracts: `Kombats.Battle.Contracts`, `Kombats.Players.Contracts`, etc. — POCOs only.

**2. Depth rating: DEEP.** Uses MassTransit EF-Core Outbox + `UseBusOutbox()` so that `IPublishEndpoint.Publish(...)` inside a request actually writes to outbox tables in the *same DbContext*, then `SaveChangesAsync` commits both the domain change and the outbox row atomically. A separate background job inside MassTransit (the bus outbox delivery service) drains the outbox to RabbitMQ. Inbox is implemented manually as a dedup table (`InboxMessage(MessageId, ProcessedAt)`) on the consumer side — **only** Players uses it (Matchmaking relies on idempotent state transitions instead).

**3. Best file:** `src/Kombats.Common/Kombats.Messaging/DependencyInjection/MessagingServiceCollectionExtensions.cs` — every messaging service is registered through it; understanding it = understanding the messaging architecture. Pair with `HandleBattleCompletedCommand.cs` to see Inbox + Publish + Save sequenced correctly.

**4. Interview hooks**
- "What is your delivery guarantee?" Answer must distinguish: producer side = exactly-once-from-DB (Outbox), bus = at-least-once, consumer side = at-most-once-side-effect (Inbox in Players; idempotent state machine in Matchmaking).
- "Why is `IPublishEndpoint.Publish` called *before* `_uow.SaveChangesAsync` in `HandleBattleCompletedHandler` (lines 99–112)?" (Bus-outbox buffers publishes on the DbContext change tracker; without `SaveChangesAsync` the outbox row never gets written.)
- Players has an `InboxRepository`. Matchmaking does not. Why the asymmetry?
- The retry policy is exponential, and there's an optional delayed-redelivery stage. Walk me through the consumer failure ladder. (Immediate exponential retries → if exhausted, requeue with delay via `UseDelayedMessageScheduler` → poison queue eventually.)
- `EntityNameConvention` strips a configured prefix and kebab-cases names. Why kebab-case? (RabbitMQ convention, avoids case-sensitivity bugs across clients.)

**5. Likely gaps**
- The owner can probably wave hands at "Outbox pattern" but needs to explicitly know: outbox rows are written **in the same transaction** as the business change; a separate poller reads them. If the process crashes between `SaveChangesAsync` and the poller, the row is still safe.
- The difference between MassTransit's `AddConfigureEndpointsCallback` (per-endpoint outbox wiring) and `AddEntityFrameworkOutbox` (one-time bus wiring). The codebase does both; the owner must know why.
- Why Inbox is a *table* and not a Bloom filter / Redis set. (Atomicity with the business change.)
- `BattleCompleted` is consumed by **three** services (Players, Matchmaking, Battle's own projection). What does "fan-out" look like in MassTransit-on-RabbitMQ? (Each consumer gets its own queue bound to the exchange; RabbitMQ topology owns the duplication.)

---

## G. Redis usage patterns

**1. Where it lives**
- `src/Kombats.Battle/Kombats.Battle.Infrastructure/State/Redis/RedisBattleStateStore.cs` (577 lines) — battle JSON state, ZSET deadlines, action storage, active-battles set, locks.
- `src/Kombats.Battle/Kombats.Battle.Infrastructure/State/Redis/RedisScripts.cs` — **5 Lua scripts**: `TryOpenTurnScript`, `TryMarkTurnResolvingScript`, `MarkTurnResolvedAndOpenNextScript`, `EndBattleAndMarkResolvedScript`, `ClaimDueBattlesScript`, `StoreActionAndCheckBothSubmittedScript`.
- `src/Kombats.Matchmaking/Kombats.Matchmaking.Infrastructure/Redis/` — queue store, presence store, lease lock (DB index 1), `RedisScripts.cs` (matchmaking-side Lua).
- `src/Kombats.Chat/Kombats.Chat.Infrastructure/Redis/RedisRateLimiter.cs` — sliding-window counter (DB index 2).
- SignalR backplane: `Battle.Bootstrap/Program.cs` line 131 — `.AddStackExchangeRedis(redisConnectionString)`.

**2. Depth rating: DEEP.** The Lua scripts are the highest-skill code in this codebase. Each one encodes a state-machine guard:
- `TryOpenTurnScript`: only opens turn N if `LastResolvedTurnIndex == N-1` and phase ∈ {ArenaOpen, Resolving}.
- `MarkTurnResolvedAndOpenNextScript`: atomic phase transition + HP write + ZSET deadline update.
- `EndBattleAndMarkResolvedScript`: returns 0/1/2 (NotCommitted / EndedNow / AlreadyEnded) — three-valued return is the dedup signal that prevents double-publish of `BattleCompleted`.
- `ClaimDueBattlesScript`: ZRANGEBYSCORE → per-battle `SET NX PX` lock → postpone ZSET score to lease horizon, all atomically. Output is `[battleId, turnIndex, battleId, turnIndex, ...]`.
- `StoreActionAndCheckBothSubmittedScript`: `SET NX EX` for first-write-wins on each player's action, `HSET` submission marker, returns `[alreadySubmitted, bothSubmitted, wasStored]`.

Database indices: Battle uses DB 0, Matchmaking lease uses DB 1, Chat rate-limit uses DB 2. SignalR backplane shares DB 0 with Battle state (explicitly noted in `Program.cs` comments as something to split in production).

**3. Best file:** `src/Kombats.Battle/Kombats.Battle.Infrastructure/State/Redis/RedisScripts.cs` (323 lines). The whole file is dense, self-contained, atomic-by-construction logic. Memorize `ClaimDueBattlesScript` and `EndBattleAndMarkResolvedScript`.

**4. Interview hooks**
- "Why Lua for these operations?" (Single-shot atomicity. Lua runs inside Redis's single-threaded event loop, so a script that touches the state JSON, the deadlines ZSET, and the active set runs without any interleaving.)
- `EndBattleAndMarkResolvedScript` returns 0/1/2. Why three values? (1 = first to end the battle → publish the integration event. 2 = lost the race → already published, skip. 0 = preconditions wrong → bug/retry.)
- `ClaimDueBattlesScript` postpones the ZSET score by `leaseWindowMs` *before* the worker has done the work. Defend that decision under "what if the worker crashes?"
- The state JSON is read-modify-written wholesale inside Lua scripts. Why no per-field `HSET`? (Easier to evolve schema, single-key SET, but read amplification; defend the trade-off.)
- SignalR backplane uses the same Redis instance as battle state. What's the production migration story? (Comment in `Battle.Bootstrap/Program.cs` line 128: "production deployments should split the two onto dedicated Redis nodes." Backplane is a thundering-herd risk co-located with hot game state.)

**5. Likely gaps**
- Specifics of `redis.call('SET', key, value, 'NX', 'PX', ttl)` vs `'EX'` and the absence of `'XX'` / `'GET'` flags.
- That Lua scripts are loaded by SHA and invoked by `EVALSHA` (StackExchange.Redis caches them). The first invocation `EVAL`s and subsequent ones `EVALSHA`.
- Why JSON-in-Redis instead of HASH: comparing fields is harder, but multi-field updates are trivial in Lua (decode → mutate → encode → SET). The owner needs an opinion.
- The `cjson` behavior: `cjson.null`, empty array vs empty object, integer-vs-float coercion — easy follow-up questions. Note `EndBattleAndMarkResolvedScript` explicitly handles `cjson.null` (line 165).

---

## H. Real-time (SignalR)

**1. Where it lives**
- **Internal hubs** (per-service, server-to-server): `src/Kombats.Battle/Kombats.Battle.Infrastructure/Realtime/SignalR/BattleHub.cs`, `src/Kombats.Chat/Kombats.Chat.Api/Hubs/InternalChatHub.cs`.
- **External hubs** (BFF, frontend-facing): `src/Kombats.Bff/Kombats.Bff.Api/Hubs/BattleHub.cs`, `ChatHub.cs`, `HubContextBattleSender.cs`, `HubContextChatSender.cs`.
- **Relay** (BFF holds a `HubConnection` to the downstream Battle hub per frontend connection): `src/Kombats.Bff/Kombats.Bff.Application/Relay/BattleHubRelay.cs` (443 lines).
- **Backplane**: `Battle.Bootstrap/Program.cs` line 131 — `.AddStackExchangeRedis(redisConnectionString)`.
- **Notifier**: `src/Kombats.Battle/Kombats.Battle.Infrastructure/Realtime/SignalR/SignalRBattleRealtimeNotifier.cs` (port impl).
- **Connection state**: `ConcurrentDictionary<string, BattleConnection>` keyed by frontend connection id in `BattleHubRelay`.

**2. Depth rating: DEEP.** The Relay pattern is unusual interview material:
1. Frontend opens WebSocket to BFF `/battlehub`.
2. BFF, upon `JoinBattle`, opens an *outbound* `HubConnection` from BFF to Battle's `/battlehub` (`HubConnectionBuilder`).
3. BFF wires `connection.On<T>(eventName, …)` handlers that forward events to the original frontend connection via `IHubContext`.
4. Critical detail: **`SkipNegotiation = true` and `Transports = HttpTransportType.WebSockets`** — comment in `BattleHubRelay.cs` lines 67-78 explains: the Redis backplane replicates `HubLifetimeManager` but **not** `HttpConnectionManager`'s per-connection token map; the negotiate POST and handshake GET can land on different replicas → 404. Forcing WebSockets pins both into a single upgrade.

JWT smuggling on WebSocket upgrades: `Bff/Bootstrap/Program.cs` `OnMessageReceived` (lines 41–56) reads `access_token` from the query string for `/battlehub` and `/chathub` paths.

**3. Best file:** `BattleHubRelay.cs` (443 lines, single class). It carries the most subtle real-world SignalR knowledge in the entire repo. The block comment about `SkipNegotiation` (lines 67–78) is, by itself, a complete interview anecdote.

**4. Interview hooks**
- "Why does the BFF maintain a per-frontend outbound HubConnection to the Battle service?" (Auth boundary — frontend has only a Keycloak token; BFF re-uses that token to authenticate to Battle. Plus, the Relay can perform server-side enrichment — see `INarrationPipeline` injection in lines 23, 117.)
- The `SkipNegotiation = true` block. Walk me through the failure mode without it. (Multi-replica Battle, DNS rotation, negotiate POST hits replica A, handshake GET hits replica B, B doesn't know the connection token → 404.)
- Why use `IHubContext<BattleHub>` and not `Clients.Caller`? (`Clients.Caller` is only valid inside a hub method; the relay needs to push from a SignalR-client callback, which is *not* a hub method.)
- `SubmitTurnActionAsync` (lines 351–384) does **not** propagate the caller's `CancellationToken` to the downstream `InvokeAsync`. Defend that.
- Backplane co-tenancy with battle state in Redis — what's the failure mode? (Backplane bursts on big games can crowd out battle state ops; SLOs degrade.)

**5. Likely gaps**
- What the SignalR backplane actually *replicates* (HubLifetimeManager: group memberships, broadcast invocations) vs what it doesn't (per-connection token map, handshake state). The owner has this in a comment — they need to internalize it.
- Sticky sessions vs `SkipNegotiation` — both solve the same problem. Why pick the latter? (Operationally simpler: no infra change required at the load balancer.)
- The auto-cleanup of downstream connections on `BattleEnded` (lines 199–204). What happens if `DisconnectAsync` is called from a hub callback that's still processing a queued message? (`HubConnection.StopAsync` waits.)
- Difference between SignalR's `HubConnection` (client SDK) and `Hub`/`IHubContext` (server SDK). Both appear in the same file.

---

## I. Architecture & patterns

**1. Where it lives**
- Layering: every service is `*.Api / *.Application / *.Bootstrap / *.Domain / *.Infrastructure / *.Contracts` (BFF omits Domain + Infrastructure).
- `src/Kombats.Common/Kombats.Abstractions/` — `Result`/`Error` records, `ICommand`/`IQuery`/`ICommandHandler`/`IQueryHandler` markers (no MediatR — explicit DI registrations in `Program.cs`).
- `*.Application/Ports/` — outbound port interfaces (e.g., `IBattleStateStore`, `IBattleRealtimeNotifier`, `IBattleEventPublisher`, `IBattleUnitOfWork`, `IActionIntake`).
- `*.Infrastructure/` — port implementations.
- BFF: `Kombats.Bff.Application/Composition/GameStateComposer.cs` — pure aggregation.
- Contracts: `Kombats.Battle.Contracts` (integration events), `Kombats.Battle.Realtime.Contracts` (SignalR DTOs — separate so realtime events can evolve independently of integration events).
- Recovery: `Kombats.Battle.Application/UseCases/Recovery/BattleRecoveryService.cs` — crash-window recovery.

**2. Depth rating: DEEP** for the BFF + Battle slice; **MODERATE** elsewhere (Chat, Matchmaking, Players are conventional DDD-lite). The pattern story that interviewers will care about:
- **No MediatR.** Handlers are registered directly in DI (e.g., `services.AddScoped<ICommandHandler<JoinQueueCommand, JoinQueueResult>, JoinQueueHandler>()`). Defended by simplicity.
- **Result/Error** instead of exceptions for domain failures (`HandleBattleCompletedHandler` returns `Result.Failure(Error.NotFound(...))`).
- **Ports & Adapters** is real here (Application/Ports = interfaces, Infrastructure = adapters).
- **BFF as a pattern** — owns no data, only aggregation + relay + narration.
- **Two contract layers** — integration events vs realtime events.

**3. Best file:** `src/Kombats.Battle/Kombats.Battle.Application/UseCases/Turns/BattleTurnAppService.cs` (668 lines). It is the longest, hardest, and most architecturally illustrative file: orchestrates the engine (Domain), the state store (Port), the notifier (Port), the event publisher (Port), and the unit of work — explicit comments (lines 481–485) call out why bus-outbox flush must happen at the right point.

**4. Interview hooks**
- "You chose direct DI for handlers instead of MediatR. Why?"
- The `ICommand` / `IQuery` distinction is enforced only by interface, not by infrastructure. What does that buy you? (Documentation; no runtime cost.)
- "What's the difference between `Kombats.Battle.Contracts` and `Kombats.Battle.Realtime.Contracts`?" (Integration events are MassTransit message contracts; realtime contracts are SignalR DTOs. Evolve at different speeds; realtime is internal to BFF↔Battle.)
- "BFF has no Domain. Where do BFF-only invariants live?" (Validation in `Api/Validation/` and orchestration in `Application/Composition/Narration/Relay/`.)
- The `BattleEndOutcome` is persisted onto Redis state inside `EndBattleAndMarkResolvedScript`. Why? (Crash-window recovery — without it, `BattleRecoveryService` would have to fabricate a "draw" for any battle that crashed between Redis-end and outbox-flush.)

**5. Likely gaps**
- "What does Clean Architecture actually buy us?" — easy to recite, hard to defend with this specific codebase. The owner should be able to point at concrete `Ports` interfaces and say "this is why I can swap Redis for X without touching Domain."
- Why `Application` references `Domain` but not `Infrastructure`, and the compile-time guarantee that provides.
- The Result pattern's failure mode: deep call stacks pass `Result` everywhere; if anyone throws, the contract breaks. The owner needs to defend the cost.
- BFF's "Narration" subsystem is unusual interview material — it's not a pattern, it's a feature. Be ready to skip past it or treat it as application-layer logic.

---

## J. Testing

**1. Where it lives**
- Unit tests per service per layer (`Kombats.Battle.Api.Tests`, `.Application.Tests`, `.Domain.Tests`, `.Infrastructure.Tests`) × 4 services + Bff (Api/Application only) + Common Messaging tests. ~16 projects.
- Integration: `tests/Kombats.Integration/Kombats.Integration.Tests/` — **4 scenarios**:
    - `I01_PlayersToMatchmakingFlowTests.cs`
    - `I02_MatchmakingToBattleFlowTests.cs`
    - `I03_BattleCompletionFlowTests.cs`
    - `I04_EndToEndGameplayLoopTests.cs` — full E2E with **3 separate PostgreSQL Testcontainers** (one per service DbContext), `IAsyncLifetime`, real migrations, mocked `ConsumeContext`.
- Contract tests: `tests/Kombats.Integration/Kombats.Integration.Tests/ContractSerializationTests.cs`.
- Load: `tests/Kombats.LoadTests/` — two stacks (C# `Scenarios/` + `VirtualPlayer/`, Python `locust/`). 167 .cs files total across all test projects.

**2. Depth rating: MODERATE** for unit/integration, **DEEP** for load. The integration test approach is genuinely thoughtful: real Postgres (not in-memory), three separate containers to match production schema isolation, consumer boundaries tested by direct invocation with mocked `IConsumeContext`. Load testing has a multi-chapter story with markdown reports (`RUN_0..RUN_5`, `CHAPTER_2.5/3/4`, `PHASE_B0/B1A`).

**3. Best file:** `tests/Kombats.Integration/Kombats.Integration.Tests/I04_EndToEndGameplayLoopTests.cs`. The 11-step header comment (lines 27–45) is a complete walkthrough of the cross-service handoff chain — reading it gives a clearer picture of the system than any architecture diagram.

**4. Interview hooks**
- Why three Postgres containers in `I04` instead of one shared instance with three schemas? (Matches production isolation — catches cross-service `JOIN` regressions.)
- The integration tests mock `IConsumeContext`. Why not use a MassTransit test harness? (Speed; the test wants to verify the *handler*, not MassTransit's plumbing. Trade-off: doesn't catch envelope/serialization regressions — that's what `ContractSerializationTests` is for.)
- The load test has a "Chapter 3" report titled `feat(battle): SignalR Redis backplane + skip-negotiation`. Walk me through what changed and why.
- Snapshot tests are explicitly avoided (CLAUDE.md frontend rule, but the philosophy applies). Why? (Brittle, low-signal, encode the current behavior as the spec.)

**5. Likely gaps**
- Testcontainers lifecycle. `IAsyncLifetime`'s `InitializeAsync` runs *per test class instance*; xUnit creates one per test method by default. That's expensive — the owner should know it.
- What "consumer boundary" actually means in this codebase and why the integration test mocks `IConsumeContext<T>` instead of publishing through MassTransit's in-memory bus.
- Load test methodology: open-loop vs closed-loop, virtual users vs connections, p50/p99 measurement (the owner should be able to talk about load shapes — and the chapter reports suggest they have).
- No mutation testing, no contract testing against schema registries, no chaos engineering harness. Be honest about that if asked.

---

## K. Observability & ops

**1. Where it lives**
- `src/Kombats.Common/Kombats.Observability/KombatsObservabilityExtensions.cs` — central OTel registration: AspNetCore + HttpClient + Redis + Npgsql + MassTransit instrumentation, custom Meter (`Kombats.{service}`), OTLP exporter conditionally attached, runtime+process metric instrumentation.
- `src/Kombats.Common/Kombats.Observability/KombatsMetrics.cs` — custom instruments: `TurnResolutionDurationMs` (histogram), `ActiveBattles` (up-down counter), `ActiveSignalRConnections`, `DownstreamHubConnections`, `PairingDurationMs`, `QueuedPlayers`.
- `observability/` — Grafana, Prometheus, OTel Collector configs.
- `infra/main.bicep`, `infra/workload.bicep`, `infra/modules/` — 8 Bicep modules (backend-app, env-storage, keycloak-app, migrator-job, stateful-app, static-web-app, storage).
- `azure-pipelines.yml` + `pipelines/{build-backend.yml,deploy-stack.yml,teardown.yml}`.
- `src/Kombats.Migrator/Dockerfile` — image-only project, run as a Job/Container App Job in production.

**2. Depth rating: MODERATE** for observability, **MODERATE** for IaC, **SHALLOW** for pipelines (interview-wise — three YAML files). The custom Meter setup is clean, the metric-export-interval override (60s default → tunable) has a real-world story attached: "short load-test battles complete entirely within one default window and never surface in Prometheus" (`KombatsObservabilityExtensions.cs` lines 96–101). The Migrator-Job pattern is interview-worthy on its own: deploy never runs migrations; a separate Job does, then the deploy proceeds.

**3. Best file:** `KombatsObservabilityExtensions.cs` (110 lines). It is small, every line is intentional, and the inline comments explain three real-world quirks (silent OTLP skip, defensive stderr warn, metric export interval).

**4. Interview hooks**
- The OTLP endpoint check (lines 45–52) writes to `Console.Error` from inside a DI extension. Why not `ILogger`? (Pre-DI-build; logger isn't available yet. Standard .NET pattern for startup-time diagnostics.)
- Why is the metric export interval set to a non-default value? (Default is 60s; battles complete in seconds; metrics for short battles never reached Prometheus.)
- The Migrator is a separate image with no code, just `Dockerfile`. What does this give you that `Database.MigrateAsync()` doesn't? (Deploy/migrate separation; can run from CI without bringing up the app; explicit observability of migration outcomes.)
- `KombatsMetrics` is registered as a singleton. Where does the Meter get *disposed*? (Process exit — DI container shutdown calls `IDisposable.Dispose()`. The comment in `KombatsMetrics.cs` line 5 explicitly says "Instruments are created once on the service's Meter ... and held for the lifetime of the process.")

**5. Likely gaps**
- The difference between OpenTelemetry's exporters (OTLP, Prometheus, Console) and what `AddOtlpExporter` defaults to (gRPC vs HTTP, what port). The owner has it configured generically; an interviewer may push specifics.
- That `Meter` names are global per-process; collision between two libraries with the same name is undefined behavior. `Kombats.{serviceName}` is the namespacing.
- Bicep depth — modules are split functionally but the owner may not be able to defend specific decisions (e.g., why Postgres is in a stateful-app vs managed service).
- Pipeline coverage is shallow as code (3 YAML files); an interviewer asking about deployment safety nets (canary, blue-green, smoke tests) will quickly run out of code to point at. Be ready to talk about *intent* if the implementation is thin.

---

## Dependency-ordered study sequence

Topics earlier in this list unlock later ones — study in order.

1. **C/D — DI, lifetimes, ASP.NET pipeline.** Required to read any `Program.cs`. Don't try to grok the Lua scripts before you can mentally model "where does `IBattleStateStore` come from at this call site."
2. **A — modern C# in this codebase.** Especially `Task`/`CancellationToken` semantics and `Result`/`Error`. Required to talk about anything.
3. **E — EF Core basics.** `AsNoTracking`, change tracker, `SaveChangesAsync`, migrations.
4. **G — Redis.** Especially the **non-Lua** patterns first (string GET/SET, ZSET, SET NX PX). Then read each Lua script with the state-machine diagram in front of you.
5. **B — concurrency.** Layered on top of (G): the lease-locking pattern only makes sense once you know `SET NX PX` cold. Then read `MatchmakingLeaseService` and `ClaimDueBattlesScript`.
6. **F — messaging.** Read `MessagingServiceCollectionExtensions` first. Then trace one event end-to-end (`PlayerCombatProfileChanged`: Players → Matchmaking + Chat; or `BattleCompleted`: Battle → Players + Matchmaking + Battle projection). Inbox/Outbox only makes sense once you understand "the Publish is buffered on the DbContext."
7. **H — SignalR.** The Relay pattern requires (D), (C), and (G/backplane) to be in place. Don't try to defend `SkipNegotiation = true` before you know what negotiation is.
8. **I — architecture & patterns.** Reread `BattleTurnAppService.cs` *after* (B), (F), (G), (H). It is the synthesis exam.
9. **J — testing.** Easy to walk through; mostly mechanical.
10. **K — observability & ops.** Easy to walk through; mostly mechanical.

---

## Crown jewels — the 5–7 pieces most worth explaining fluently

These are the strongest interview material. Be able to walk through each one cold, on a whiteboard, with no notes.

1. **`MatchmakingLeaseService.cs` + `RedisLeaseLock.cs`** — distributed lease with TTL/3 renewal, linked-token cancellation on lease loss, atomic check-and-extend Lua. ~200 lines total; memorizable.
2. **`RedisScripts.cs::ClaimDueBattlesScript`** — ZRANGEBYSCORE + per-battle `SET NX PX` lock + ZSET-score postponement, all atomic. The single best Redis story in the codebase.
3. **`RedisScripts.cs::EndBattleAndMarkResolvedScript`** — three-valued return (0/1/2) used to dedup `BattleCompleted` publish across crash recovery. Pair with `BattleTurnAppService.CommitAndNotifyBattleEnded` (lines 341–510) to show how the integer return controls integration-event emission.
4. **MassTransit EF Outbox flow in `HandleBattleCompletedHandler`** — Inbox check → domain mutation → Publish (buffered) → `SaveChangesAsync` (commits outbox row + Inbox row + domain row in one transaction). Pair with `MessagingServiceCollectionExtensions.cs` to show the DI side. This is the strongest *distributed-systems* story in the codebase.
5. **`BattleHubRelay.cs` with `SkipNegotiation = true`** — multi-replica SignalR over Redis backplane, BFF re-creates an outbound HubConnection per frontend connection, forwards events with `IHubContext`. The block comment at lines 67–78 is a complete interview anecdote.
6. **`BattleTurnAppService.ResolveTurnAsync` — stuck-in-Resolving recovery** (lines 246–250) — the case for "the service crashed after CAS-to-Resolving but before MarkTurnResolvedAndOpenNext"; how `BattleRecoveryWorker` re-enters this path.
7. **`KombatsObservabilityExtensions.cs`** — small, intentional, every comment is a real-world story. The metric-export-interval override (lines 96–101) is a "we hit this in production" anecdote.

---

## Thin ice — areas where a deep interviewer can expose shallow understanding fast

These look impressive in the file tree but are weaker than they appear. If pressed, the owner needs to either know the answer cold or honestly say "I made that decision but I'd want to dig deeper before changing it."

1. **"Clean Architecture / DDD"** — the layering is real, but `Domain` is genuinely thin in Matchmaking (two files: `Match.cs`, `MatchState.cs`) and Chat. Battle is the only service where `Domain` carries weight (`BattleEngine.cs`, 622 lines). If asked "what does your DDD layer protect you from?" — point at Battle, not Matchmaking.
2. **The lease's safety story** — distributed locks on a single Redis can be defended (Sentinel, primary fails over), but if an interviewer asks "what if the Redis primary fails over mid-lease and the new primary doesn't have the lock?" the answer requires knowledge of Sentinel's promote semantics and what "asynchronous replication" means for correctness. The codebase doesn't address this; the comment says "Sentinel-ready" but doesn't claim correctness.
3. **CQRS** — the `ICommand` / `IQuery` interfaces *suggest* CQRS, but there's no separate read/write store, no projections-on-write, no eventual-consistency between them. It's command/query *naming*. Don't oversell.
4. **Outbox pattern** — easy to say "we have an outbox," harder to explain: outbox table schema, the poller's batch size and visibility behavior, what happens if the outbox grows faster than it drains. The codebase relies on MassTransit's defaults; the owner has not tuned them.
5. **Recovery semantics** — `BattleRecoveryService` plus `EndWinnerPlayerId`/`EndReason`/`EndFinalTurnIndex`/`EndedAtUnixMs` persisted onto Redis state is interview-strong, but only **Battle** has recovery. Matchmaking has none (`MatchTimeoutWorker` is a different concept — it expires *queue* matches, not in-flight processes). If asked "how does Matchmaking recover from a process crash mid-tick?" the answer is "the lease expires and the next tick picks up; idempotent state transitions in Redis mean no harm." Defend that, don't bluff.
6. **Inbox asymmetry** — Players uses an Inbox; Matchmaking does not. The defensible answer is "Matchmaking's state machine is idempotent end-to-end (every transition gates on current state)" — that requires actually proving it for the BattleCreated + BattleCompleted consumers. If the owner hasn't traced that, this is thin ice.
7. **SignalR backplane co-tenancy with battle state** — the code comment acknowledges this should be split in production but isn't. An interviewer will ask "what failure mode does this expose?" The answer (backplane bursts crowding battle-state ops; head-of-line blocking on Redis) is *not* in the code; the owner needs to formulate it.
8. **Load test results** — the markdown reports are extensive. If the owner can't articulate what each Chapter/Run measured and what the failure modes were, the reports become liability instead of asset.
9. **No gRPC, no service mesh, no consumer groups** — these are fine choices, but be ready to defend "why all REST + RabbitMQ" rather than gRPC, why no Istio/Linkerd, why no Kafka. The defensible answer is "RPS budget, tooling, team size" — not "didn't think about it."
10. **Frontend lane is "active"** — per CLAUDE.md, backend is hardening-only. An interviewer who reads the repo will notice the frontend hasn't been touched (`src/Kombats.Client/` is sparse). If asked about full-stack ownership, the honest answer is "backend is shipped, frontend is in progress." Don't claim end-to-end shipping if you haven't shipped end-to-end.

---

*End of map.*
