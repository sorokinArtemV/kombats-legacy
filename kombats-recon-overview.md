# Kombats — Recon Overview

Снапшот верхнего уровня репозитория для построения учебного плана. Только факты, наблюдаемые в коде и структуре директорий. Никаких оценок качества.

---

## 1. Repo Layout

**Тип:** монорепо. Один git-репозиторий, один `Kombats.sln` в корне.

### Дерево верхнего уровня

```
Kombats/
├── .claude/                          (workflow assets — frontend-active, .claude/backend/ archived)
├── infra/                            (Bicep IaC)
│   ├── main.bicep, workload.bicep, main.bicepparam, main.json
│   ├── modules/                      (8 .bicep модулей: backend-app, env-storage,
│   │                                  keycloak-app, migrator-job, stateful-app,
│   │                                  static-web-app, storage)
│   ├── keycloak/, keycloak-themes/, postgres/
├── observability/                    (Grafana, Prometheus, OTel collector конфиги)
├── pipelines/                        (build-backend.yml, deploy-stack.yml, teardown.yml)
├── azure-pipelines.yml               (корневой entrypoint pipeline)
├── scripts/                          (deploy-stack.sh, run-migrations.{ps1,sh}, show-tree.ps1)
├── src/
│   ├── Kombats.Battle/               (5-проектный сервис + Contracts + Realtime.Contracts = 7 .csproj)
│   ├── Kombats.Bff/                  (3 .csproj: Api, Application, Bootstrap)
│   ├── Kombats.Chat/                 (6 .csproj: Api, Application, Bootstrap, Contracts, Domain, Infrastructure)
│   ├── Kombats.Client/               (React 19 + Vite SPA, TypeScript)
│   ├── Kombats.Common/               (3 .csproj: Abstractions, Messaging, Observability)
│   ├── Kombats.Matchmaking/          (6 .csproj — стандартный набор слоёв)
│   ├── Kombats.Migrator/             (только Dockerfile — образ для миграций)
│   └── Kombats.Players/              (6 .csproj — стандартный набор слоёв)
├── tests/
│   ├── Kombats.Battle/               (4 тест-проекта: Api/Application/Domain/Infrastructure)
│   ├── Kombats.Bff/                  (2: Api, Application)
│   ├── Kombats.Chat/                 (4)
│   ├── Kombats.Common/               (Messaging.Tests)
│   ├── Kombats.Integration/          (Kombats.Integration.Tests — 4 .cs-сценария I01..I04)
│   ├── Kombats.LoadTests/            (C# Scenarios + Python locust + множество отчётов)
│   ├── Kombats.Matchmaking/          (4)
│   └── Kombats.Players/              (4)
├── docker-compose.{yml,local,multi-replica,override}.yml
├── Kombats.sln                       (единственный solution-файл)
├── Directory.Build.props, Directory.Packages.props, global.json
└── Markdown-документы в корне: README, LOAD_TEST_PLAN/STORY, OBSERVABILITY_*,
                                  RECON_REPORT, KEYCLOAK_BOOTSTRAP
```

### .sln файлы

- **1 файл:** `Kombats.sln` (68 KB) в корне.

---

## 2. Сервисы

Сервисы — backend на .NET. Frontend (`Kombats.Client`) — отдельный React SPA, не .NET-сервис.

| # | Имя | Путь | Ответственность (по именам) | Архитектурный стиль | ~кол-во .cs (без bin/obj) |
|---|---|---|---|---|---|
| 1 | **Kombats.Battle** | `src/Kombats.Battle/` | Логика боя: движок (Engine, Rules), turn-history, recovery, realtime-нотификации участникам | Clean Architecture / DDD: `Domain` (Engine + Model + Rules + Events) → `Application` (UseCases + Ports + ReadModels) → `Infrastructure` (Data + Redis State + Messaging + Realtime/SignalR) → `Api` (Endpoints) → `Bootstrap` (host + Workers). Доп. проекты `Contracts` и `Realtime.Contracts` | **117** |
| 2 | **Kombats.Bff** | `src/Kombats.Bff/` | Backend-for-Frontend: HTTP-агрегатор поверх Players/Matchmaking/Battle/Chat + SignalR-релэи (`BattleHubRelay`, `ChatHubRelay`) + Narration (комментатор/feed) | 3 слоя: `Application` (Clients, Composition, Narration, Relay) + `Api` (Endpoints, Hubs, Mapping, Middleware, Validation) + `Bootstrap`. **Нет** `Domain` и `Infrastructure` (внешние сервисы через HTTP-клиенты) | **100** |
| 3 | **Kombats.Chat** | `src/Kombats.Chat/` | Чат: разговоры (Conversations) и сообщения (Messages), presence, rate-limit, ретенция | DDD-layered (Domain, Application, Infrastructure, Api, Bootstrap, Contracts). `Infrastructure` содержит `Workers` (BackgroundService) и `Redis` подсистему | **82** |
| 4 | **Kombats.Matchmaking** | `src/Kombats.Matchmaking/` | Подбор противников: очередь, lease-локи, тики подбора | DDD-layered. `Domain` очень тонкий (`Match.cs`, `MatchState.cs`). Богатая `Infrastructure/Redis` (5 stores + scripts + lease) | **78** |
| 5 | **Kombats.Players** | `src/Kombats.Players/` | Профили игроков, character/progression, allocate stat-points, обработка completion-событий боя | DDD-layered. Имеет EF-миграции с **Outbox** + `Inbox` репозиторий в `Infrastructure/Messaging/Inbox` | **87** |
| 6 | **Kombats.Migrator** | `src/Kombats.Migrator/` | Образ-раннер EF-миграций (используется как Job в k8s/Container Apps) | Только `Dockerfile`. Кода нет | 0 |
| 7 | **Kombats.Client** | `src/Kombats.Client/` | React 19 + Vite SPA. `transport/` (HTTP + SignalR) изолирован от `ui/` и `modules/` | Frontend, не .NET сервис | n/a (TS) |
| — | **Kombats.Common** | `src/Kombats.Common/` | Общие сборки: `Abstractions` (Result/Error, ICommand/IQuery, Auth helpers), `Messaging` (MassTransit DI + filters + naming), `Observability` (KombatsMetrics) | Поддерживающие библиотеки | **19** |

**Общий паттерн для всех 5 живых .NET-сервисов** (Battle / Bff / Chat / Matchmaking / Players): layered DDD-стиль с проектами `*.Api`, `*.Application`, `*.Bootstrap` (host-композиция), `*.Domain` (кроме Bff), `*.Infrastructure` (кроме Bff), `*.Contracts` (кроме Bff). Battle уникально имеет дополнительный `Realtime.Contracts`.

---

## 3. Технологии — где что

| Технология | Где (файлы/папки) |
|---|---|
| **Redis (StackExchange.Redis)** | `src/Kombats.Battle/Kombats.Battle.Infrastructure/State/Redis/` (`RedisBattleStateStore.cs`, `RedisScripts.cs` — Lua); `src/Kombats.Matchmaking/Kombats.Matchmaking.Infrastructure/Redis/` (`RedisLeaseLock.cs`, `RedisMatchQueueStore.cs`, `RedisPlayerMatchStatusStore.cs`, `RedisQueuePresenceStore.cs`, `RedisScripts.cs`, `MatchmakingLeaseService.cs`); `src/Kombats.Chat/Kombats.Chat.Infrastructure/Redis/` (`RedisPlayerInfoCache.cs`, `RedisPresenceStore.cs`, `RedisRateLimiter.cs`). Регистрация — в `*.Bootstrap/Program.cs`. |
| **RabbitMQ + MassTransit** | Общая инфраструктура: `src/Kombats.Common/Kombats.Messaging/` (DI extensions, naming conventions, consume-logging filter). Consumers по сервисам: `Kombats.Players.Infrastructure/Messaging/Consumers/BattleCompletedConsumer.cs`; `Kombats.Matchmaking.Infrastructure/Messaging/Consumers/` (PlayerCombatProfileChangedConsumer, BattleCompletedConsumer, BattleCreatedConsumer); `Kombats.Chat.Infrastructure/Messaging/Consumers/PlayerCombatProfileChangedConsumer.cs`; `Kombats.Battle.Infrastructure/Messaging/Consumers/CreateBattleConsumer.cs` + `Projections/BattleCompletedProjectionConsumer.cs`. Publishers: `MassTransitCombatProfilePublisher.cs` (Players), `MassTransitCreateBattlePublisher.cs` (Matchmaking), `MassTransitBattleEventPublisher.cs` (Battle). |
| **SignalR (Hub-классы)** | Внутренние Hub-ы сервисов: `Kombats.Battle.Infrastructure/Realtime/SignalR/BattleHub.cs` + `SignalRBattleRealtimeNotifier.cs`; `Kombats.Chat.Api/Hubs/InternalChatHub.cs` + `SignalRChatNotifier.cs`. Внешние Hub-ы (для фронта) в BFF: `Kombats.Bff.Api/Hubs/` (`BattleHub.cs`, `ChatHub.cs`, `HubContextBattleSender.cs`, `HubContextChatSender.cs`). Релэи: `Kombats.Bff.Application/Relay/` (`BattleHubRelay.cs`, `ChatHubRelay.cs`). Клиент: `src/Kombats.Client/src/transport/signalr/`. |
| **EF Core + DbContext + миграции** | `PlayersDbContext` (`Kombats.Players.Infrastructure/Data/`) + миграции `Persistence/EF/Migrations/`. `MatchmakingDbContext` (`Kombats.Matchmaking.Infrastructure/Data/`) + `Migrations/`. `ChatDbContext` (`Kombats.Chat.Infrastructure/Data/`) + `Migrations/`. `BattleDbContext` (`Kombats.Battle.Infrastructure/Data/DbContext/`) + `Migrations/`. У каждого сервиса свой DesignTimeDbContextFactory. **BFF не имеет DbContext** (читает через HTTP-клиенты). |
| **Keycloak / Auth** | Общие auth-расширения: `src/Kombats.Common/Kombats.Abstractions/Auth/` (`IdentityIdExtensions.cs`, `KombatsAuthExtensions.cs`). Bootstrap-конфиги: `appsettings.json` каждого сервиса + `KombatsAuthExtensions` подключает JwtBearer. Inter-service forwarding: `Kombats.Bff.Application/Clients/JwtForwardingHandler.cs`, `Kombats.Chat.Bootstrap/Http/PlayersAuthForwardingHandler.cs`. Bootstrap-инфра Keycloak: `infra/keycloak/`, `infra/keycloak-themes/`, корневой `KEYCLOAK_BOOTSTRAP.md`. Клиент: `Kombats.Client/src/modules/auth/`. |
| **Bicep (IaC)** | `infra/main.bicep`, `infra/workload.bicep`, `infra/main.bicepparam`, `infra/main.json` (скомпилированный ARM); 8 модулей в `infra/modules/`: `backend-app`, `env-storage`, `keycloak-app`, `migrator-job`, `stateful-app`, `static-web-app`, `storage`. |
| **CI/CD (Azure Pipelines)** | Корень: `azure-pipelines.yml`. Папка `pipelines/`: `build-backend.yml`, `deploy-stack.yml`, `teardown.yml`. Скрипты деплоя: `scripts/deploy-stack.sh`, `scripts/run-migrations.{ps1,sh}`. |
| **Тесты** | **Unit/Component**: по каждому сервису 4 проекта (`*.Api.Tests`, `*.Application.Tests`, `*.Domain.Tests`, `*.Infrastructure.Tests`) — в сумме ~16 тест-проектов + `Kombats.Messaging.Tests` для common. **Integration**: `tests/Kombats.Integration/Kombats.Integration.Tests/` — 4 файла-сценария: I01_PlayersToMatchmakingFlowTests, I02_MatchmakingToBattleFlowTests, I03_BattleCompletionFlowTests, I04_EndToEndGameplayLoopTests + ContractSerializationTests. **Load**: `tests/Kombats.LoadTests/` — двухстековая (C# `Scenarios/` + `VirtualPlayer/` и Python `locust/`); много markdown-отчётов RUN_0..RUN_5, CHAPTER_2.5/3/4, PHASE_B0/B1A. Всего по тестам **~167 .cs файлов**. |

---

## 4. Точки интереса для учебного плана

### Конкурентность / многопоточность / locks

- `src/Kombats.Matchmaking/Kombats.Matchmaking.Infrastructure/Redis/MatchmakingLeaseService.cs` — distributed leasing.
- `src/Kombats.Matchmaking/Kombats.Matchmaking.Infrastructure/Redis/RedisLeaseLock.cs` — низкоуровневый Redis-lock.
- `src/Kombats.Chat/Kombats.Chat.Infrastructure/Redis/RedisRateLimiter.cs` — распределённый rate-limit.
- `src/Kombats.Bff/Kombats.Bff.Application/Relay/BattleHubRelay.cs` и `ChatHubRelay.cs` — встречается `lock(...)`/`SemaphoreSlim`/`ConcurrentDictionary` (релэй держит состояние коннекций).
- `src/Kombats.Chat/Kombats.Chat.Api/Hubs/HeartbeatScheduler.cs` — таймерная конкурентность.
- `src/Kombats.Bff/Kombats.Bff.Application/Narration/Templates/InMemoryTemplateCatalog.cs` — кеш с конкурентным доступом.

### Workers / BackgroundService (всего 7)

- `Kombats.Matchmaking.Bootstrap/Workers/MatchmakingPairingWorker.cs` — тик подбора пар.
- `Kombats.Matchmaking.Bootstrap/Workers/MatchTimeoutWorker.cs` — таймауты матчей.
- `Kombats.Matchmaking.Bootstrap/Workers/QueuePresenceSweepWorker.cs` — очистка протухшего presence в очереди.
- `Kombats.Chat.Infrastructure/Workers/MessageRetentionWorker.cs` — ретенция сообщений.
- `Kombats.Chat.Infrastructure/Workers/PresenceSweepWorker.cs` — очистка presence.
- `Kombats.Battle.Bootstrap/Workers/TurnDeadlineWorker.cs` — дедлайны ходов.
- `Kombats.Battle.Bootstrap/Workers/BattleRecoveryWorker.cs` — recovery боёв после рестарта.

### Обработка сообщений из очередей (MassTransit consumers)

- `Kombats.Battle.Infrastructure/Messaging/Consumers/CreateBattleConsumer.cs` — Matchmaking → Battle.
- `Kombats.Battle.Infrastructure/Messaging/Projections/BattleCompletedProjectionConsumer.cs` — projection.
- `Kombats.Matchmaking.Infrastructure/Messaging/Consumers/BattleCreatedConsumer.cs`, `BattleCompletedConsumer.cs`, `PlayerCombatProfileChangedConsumer.cs`.
- `Kombats.Players.Infrastructure/Messaging/Consumers/BattleCompletedConsumer.cs`.
- `Kombats.Chat.Infrastructure/Messaging/Consumers/PlayerCombatProfileChangedConsumer.cs`.
- Общая конфигурация: `Kombats.Common/Kombats.Messaging/DependencyInjection/MessagingServiceCollectionExtensions.cs`, `Filters/ConsumeLoggingFilter.cs`, `Naming/{CombatsEndpointNameFormatter,EntityNameConvention}.cs`.

### Нетривиальная async-логика

- `Kombats.Battle.Application/UseCases/Turns/BattleTurnAppService.cs` — самый большой файл (668 строк); ядро обработки хода + публикация событий + история.
- `Kombats.Battle.Application/UseCases/Recovery/BattleRecoveryService.cs` — recovery-логика.
- `Kombats.Bff.Application/Narration/NarrationPipeline.cs` (325 строк) + `DefaultCommentatorPolicy.cs` — пайплайн комментатора.
- `Kombats.Bff.Application/Composition/GameStateComposer.cs` — параллельные вызовы downstream-сервисов через HTTP.
- `Kombats.Bff.Application/Relay/BattleHubRelay.cs` (443 строки) — мост между серверным Battle.SignalR-нотификациями и клиентом BFF.

### Outbox / Inbox / Idempotency / Retry / Lease

- **Outbox**: `Kombats.Players.Infrastructure/Persistence/EF/Migrations/20260407054312_AddOutboxEntities.cs` — добавление outbox-таблиц. У Matchmaking была своя — удалена в `20260408062301_RemoveLegacyCustomOutboxTable.cs` (вероятно, перешли на MassTransit Outbox).
- **Inbox** (dedup на стороне consumer): `Kombats.Players.Application/Abstractions/IInboxRepository.cs`, `Kombats.Players.Infrastructure/Messaging/Inbox/InboxMessage.cs`, `Kombats.Players.Infrastructure/Persistence/Repository/InboxRepository.cs`, конфиг EF `InboxMessageConfig.cs`. Использование в `HandleBattleCompletedCommand.cs`, `EnsureCharacterExistsHandler.cs`.
- **Lease**: `MatchmakingLeaseService.cs`, `RedisLeaseLock.cs`.
- **Retry / Resilience**: `Kombats.Bff.Application/Clients/ResilienceOptions.cs` (конфиг для HTTP-клиентов BFF). Клиентский TanStack Query retry: `Kombats.Client/src/app/query-retry.test.ts`, `query-client.ts`.

### Boundary между сервисами

- **HTTP-клиенты BFF → downstream** (`Kombats.Bff.Application/Clients/`): `BattleClient.cs` (+ `IBattleClient`), `ChatClient.cs` (+ `IChatClient`), `MatchmakingClient.cs` (+ `IMatchmakingClient`), `PlayersClient.cs` (+ `IPlayersClient`), общий `HttpClientHelper.cs`, JWT-проброс `JwtForwardingHandler.cs`, конфиги `ResilienceOptions.cs`/`ServiceOptions.cs`.
- **Inter-service HTTP вне BFF**: `Kombats.Chat.Bootstrap/Http/PlayersAuthForwardingHandler.cs` (Chat → Players?).
- **Сервис-внутренние HTTP**: `Kombats.Chat.Infrastructure/Services/{EligibilityChecker,DisplayNameResolver}.cs` тоже используют HttpClient.
- **Контракты** (message-контракты между сервисами): `*.Contracts` проекты у каждого сервиса (Battle/Chat/Matchmaking/Players), плюс отдельный `Kombats.Battle.Realtime.Contracts` для SignalR-сообщений к фронту. Сериализационные тесты — `tests/Kombats.Integration/Kombats.Integration.Tests/ContractSerializationTests.cs`.
- **gRPC**: не обнаружен (grep не дал результатов в выборках — см. секцию «не смог определить»).

---

## 5. Размер

### Количество .cs файлов на сервис (без bin/obj, включая Migrations Designer)

| Сервис | .cs файлов |
|---|---|
| Kombats.Battle | **117** |
| Kombats.Bff | **100** |
| Kombats.Players | **87** |
| Kombats.Chat | **82** |
| Kombats.Matchmaking | **78** |
| Kombats.Common | **19** |
| **Всего src/ (.NET)** | **~483** |
| tests/ (все тест-проекты) | **167** |

### Топ-10 самых крупных .cs файлов (по размеру, без Migrations)

| # | Файл | Строки |
|---|---|---|
| 1 | `src/Kombats.Battle/Kombats.Battle.Application/UseCases/Turns/BattleTurnAppService.cs` | **668** |
| 2 | `src/Kombats.Battle/Kombats.Battle.Domain/Engine/BattleEngine.cs` | **622** |
| 3 | `src/Kombats.Battle/Kombats.Battle.Infrastructure/State/Redis/RedisBattleStateStore.cs` | **577** |
| 4 | `src/Kombats.Bff/Kombats.Bff.Application/Relay/BattleHubRelay.cs` | **443** |
| 5 | `src/Kombats.Bff/Kombats.Bff.Bootstrap/Program.cs` | **354** |
| 6 | `src/Kombats.Bff/Kombats.Bff.Application/Narration/NarrationPipeline.cs` | **325** |
| 7 | `src/Kombats.Battle/Kombats.Battle.Infrastructure/State/Redis/RedisScripts.cs` | **323** |
| 8 | `src/Kombats.Bff/Kombats.Bff.Application/Relay/ChatHubRelay.cs` | **317** |
| 9 | `src/Kombats.Bff/Kombats.Bff.Application/Narration/DefaultCommentatorPolicy.cs` | **278** |
| 10 | `src/Kombats.Chat/Kombats.Chat.Bootstrap/Program.cs` | **270** |

Концентрация массы: ядро **Battle** (#1, #2, #3, #7) и слой **BFF Relay/Narration** (#4, #5, #6, #8, #9). Chat/Matchmaking/Players в топ-10 крупнейших файлов почти не представлены — у них масса распределена ровнее.

---

## 6. Что я НЕ смог определить

Честный список — ниже только то, что было бы видно лишь при более глубоком чтении кода:

- **Точные обязанности каждого сервиса** — выведены из имён папок/проектов и имён consumer-ов; нет ни одного открытого файла бизнес-логики. Например: что именно делает `Narration` в BFF (комментатор боя?), как именно Players связан с прогрессией — только догадки по именам.
- **Граф взаимодействий сервис↔сервис** — видно, что Matchmaking публикует `CreateBattle`, Battle потребляет; Players и Matchmaking слушают `BattleCompleted`. Полный список топиков/маршрутов RabbitMQ не выводил — для этого нужно открыть `MessagingServiceCollectionExtensions.cs` и конфиги Bootstrap.
- **Точная схема outbox** — в Players есть outbox-миграция, но какой механизм (MassTransit EFCore-Outbox vs. кастом) не проверял внутри `Program.cs`/конфигов.
- **gRPC** — никаких `.proto`, `Grpc.AspNetCore` или `MagicOnion` среди явно увиденного. Маловероятно, что используется, но проверкой `grep` по `Grpc` я не подтвердил отсутствие.
- **Использует ли проект gRPC-style контракты или сугубо REST + Async messaging** — судя по `Bff.Application/Clients/*.cs`, всё HTTP/REST.
- **Какая именно версия .NET** — `global.json` есть, но не открывал; CLAUDE.md упоминает .NET 10.0.
- **Какие именно auth-flows используются Keycloak-ом** (resource-owner password? authorization-code? client-credentials?) — нужно открыть `KEYCLOAK_BOOTSTRAP.md` и `Kombats.Client/src/modules/auth/`.
- **Какие именно метрики/трейсы собираются** — есть `KombatsMetrics.cs` и `KombatsObservabilityExtensions.cs`, но содержимое не открывал.
- **SignalR backplane (Redis)** — из коммита `3d30c71 feat(battle): SignalR Redis backplane + skip-negotiation — Chapter 3` известно, что backplane включён, но соответствующую регистрацию в `Bootstrap` не подтверждал.
- **Реальная архитектура `Kombats.Client`** — структура `transport/signalr/`, `modules/auth/`, `ui/theme/`, `app/` видна, но детали не разбирал (frontend lane активен — см. CLAUDE.md, отдельные планы в `docs/frontend/`).
- **Размер фронта в строках/файлах** — посчитан только backend; TS-файлы не считал.
- **`docker-compose.multi-replica.yml`** — есть, но содержимое не смотрел: непонятно, сколько реплик и каких сервисов поднимается.
- **Состав observability-стека** — `observability/grafana`, `prometheus`, `otel-collector` присутствуют как папки, конкретные дашборды/метрики не разобраны.
- **Назначение пустого файла `project` в корне репозитория** (0 байт).
