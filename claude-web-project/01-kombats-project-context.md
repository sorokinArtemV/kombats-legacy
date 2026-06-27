# Kombats — Project Context (Knowledge File)

---

## 0. TL;DR — что это за проект

**Kombats** — пошаговый браузерный файтинг (turn-based browser fighting game) в
восточноазиатской эстетике. Игрок создаёт персонажа, прокачивает характеристики, встаёт
в очередь подбора, дерётся 1-на-1 в реальном времени (ходы + комментатор боя + чат),
по завершении боя получает награды/прогресс.

Технически это **production-grade учебный монорепозиторий**: микросервисный .NET 10
backend + React 19 SPA + асинхронный обмен сообщениями + realtime + полный observability-стек
+ IaC для Azure Container Apps. Проект намеренно демонстрирует "взрослые" паттерны
(Clean Architecture, Outbox/Inbox, distributed locks, BFF, SignalR backplane).

**Главная ценность для воссоздания:** это не CRUD. Сложность здесь — в распределённых
взаимодействиях, конкурентности и realtime. Именно их стоит проектировать осознанно.

---

## 1. Структура монорепозитория

Один git-репозиторий, один `Kombats.sln` в корне.

```
Kombats/
├── src/
│   ├── Kombats.Battle/        ← бой: движок, ходы, recovery, realtime  (7 .csproj, ~117 .cs)
│   ├── Kombats.Matchmaking/   ← подбор: очередь, lease-локи, тики      (6 .csproj, ~78 .cs)
│   ├── Kombats.Players/       ← профили, прогрессия, характеристики    (6 .csproj, ~87 .cs)
│   ├── Kombats.Chat/          ← чат, presence, rate-limit, ретенция    (6 .csproj, ~82 .cs)
│   ├── Kombats.Bff/           ← Backend-for-Frontend (API + SignalR)   (3 .csproj, ~100 .cs)
│   ├── Kombats.Common/        ← Abstractions / Messaging / Observability (3 .csproj, ~19 .cs)
│   ├── Kombats.Migrator/      ← образ-раннер EF-миграций (только Dockerfile)
│   └── Kombats.Client/        ← React 19 + Vite SPA (TypeScript)
├── tests/                     ← unit/component на каждый сервис + Integration + LoadTests (~167 .cs)
├── infra/                     ← Bicep IaC (main/workload + 8 модулей) + Keycloak bootstrap
├── observability/             ← Grafana / Prometheus / OTel Collector конфиги
├── pipelines/ + azure-pipelines.yml  ← Azure DevOps CI/CD
├── scripts/                   ← deploy-stack, run-migrations, show-tree
├── docker-compose.*.yml       ← 6 compose-файлов (см. §8)
├── Directory.Build.props / Directory.Packages.props / global.json  ← Central Package Management
└── Markdown в корне: README, LOAD_TEST_*, OBSERVABILITY_*, RECON_REPORT, KEYCLOAK_BOOTSTRAP
```

**Управление зависимостями:** Central Package Management (`Directory.Packages.props`) —
версии пакетов заданы в одном месте, проекты ссылаются без версий. SDK pinned в `global.json`.

---

## 2. Пять backend-сервисов

Каждый "живой" сервис (Battle / Matchmaking / Players / Chat) построен по **layered DDD /
Clean Architecture** и состоит из стандартного набора проектов:

| Проект | Роль |
|---|---|
| `*.Domain` | Сущности, value-objects, доменные события, бизнес-правила. Без внешних зависимостей. |
| `*.Application` | Use-cases / app-services, порты (интерфейсы), read-models. Оркестрация без инфраструктуры. |
| `*.Infrastructure` | Реализация портов: EF Core, Redis, MassTransit consumers/publishers, HTTP-клиенты, Workers. |
| `*.Api` | HTTP endpoints (minimal API), SignalR hubs, mapping, validation, middleware. |
| `*.Bootstrap` | Host-композиция (`Program.cs`), DI-сборка, hosted Workers, Dockerfile. |
| `*.Contracts` | Message-контракты (события/команды), публикуемые в шину. Разделяемы между сервисами. |

**Зависимости слоёв:** `Api/Bootstrap → Application → Domain`; `Infrastructure → Application/Domain`.
Domain ни от чего не зависит. Это правило — основа всей архитектуры.

### 2.1 Kombats.Players — профили и прогрессия

- Профиль игрока, персонаж, прокачка: `AllocateStats`, прогресс по итогам боя.
- Слушает `BattleCompleted` → начисляет результат (`HandleBattleCompletedCommand`).
- **Outbox** (EF-миграция `AddOutboxEntities`) + **Inbox**-репозиторий для идемпотентной
  обработки входящих событий (dedup по message-id). Это эталон transactional messaging.
- Публикует `PlayerCombatProfileChanged` при изменении боевого профиля.

### 2.2 Kombats.Matchmaking — подбор противников

- Очередь подбора на Redis (DB 1): `RedisMatchQueueStore`, `RedisQueuePresenceStore`,
  `RedisPlayerMatchStatusStore`.
- **Distributed lease-lock** (`RedisLeaseLock` + `MatchmakingLeaseService`) — чтобы только
  один воркер/реплика подбирал пару в данный тик. Тонкий Domain (`Match`, `MatchState`),
  богатая Infrastructure (Lua-скрипты в Redis).
- Workers: `MatchmakingPairingWorker` (тик подбора, bounded loop + idle backoff),
  `MatchTimeoutWorker`, `QueuePresenceSweepWorker`.
- Слушает `PlayerCombatProfileChanged`, `BattleCreated`, `BattleCompleted`.
- Публикует `CreateBattle` (команда) → Battle.

### 2.3 Kombats.Battle — ядро боя

Самый сложный сервис. Концентрация массы кода здесь.

- **Domain/Engine** (`BattleEngine.cs`, ~622 стр) + `Rules` — детерминированный движок:
  применение хода, расчёт урона, переходы фаз, доменные события.
- **Application** (`BattleTurnAppService.cs`, ~668 стр) — обработка хода: валидация,
  применение к движку, запись turn-history, публикация realtime-событий и `BattleCompleted`.
- **State** в Redis (DB 0): `RedisBattleStateStore` (+ Lua `RedisScripts`) — авторитетное
  состояние боя живёт в Redis, не в БД (низкая латентность, atomic-операции).
- **Realtime:** внутренний `BattleHub` (SignalR) + `SignalRBattleRealtimeNotifier`.
  Включён **Redis backplane** (`SignalR.StackExchangeRedis`) + skip-negotiation — чтобы
  realtime работал при нескольких репликах Battle (Chapter 3).
- Workers: `TurnDeadlineWorker` (дедлайны ходов), `BattleRecoveryWorker` (восстановление
  активных боёв после рестарта из Redis-состояния).
- Слушает `CreateBattle`. Публикует `BattleCreated`, `BattleCompleted` + realtime-события
  (`Battle.Realtime.Contracts`).

### 2.4 Kombats.Chat — чат и presence

- Conversations + Messages, presence-трекинг, **distributed rate-limit** (`RedisRateLimiter`),
  ретенция сообщений.
- Workers: `MessageRetentionWorker`, `PresenceSweepWorker`. `HeartbeatScheduler` (таймерная конкурентность).
- Внутренний `InternalChatHub` (SignalR) + `SignalRChatNotifier`.
- Слушает `PlayerCombatProfileChanged` (актуализация display-name/профиля).
- HTTP к Players (`PlayersAuthForwardingHandler`) для eligibility/display-name.

### 2.5 Kombats.Bff — Backend-for-Frontend

Единственная точка входа для фронта. **Нет Domain и Infrastructure** — это агрегатор поверх
четырёх сервисов через HTTP + мост SignalR.

- **Clients** (`Bff.Application/Clients/`): `PlayersClient`, `MatchmakingClient`,
  `BattleClient`, `ChatClient` + `JwtForwardingHandler` (проброс токена пользователя вниз)
  + `ResilienceOptions` (Polly через `Microsoft.Extensions.Http.Resilience`).
- **Composition** (`GameStateComposer`): параллельные вызовы downstream-сервисов, сборка
  единого game-state для экрана.
- **Relay** (`BattleHubRelay` ~443 стр, `ChatHubRelay`): мост между внутренними SignalR-хабами
  сервисов и внешними хабами BFF. Держит состояние коннекций (`ConcurrentDictionary`/`SemaphoreSlim`).
- **Narration** (`NarrationPipeline`, `DefaultCommentatorPolicy`, `InMemoryTemplateCatalog`):
  генерация "комментатора боя" / feed по realtime-событиям.
- **Внешние Hubs** (`Bff.Api/Hubs/`): `BattleHub`, `ChatHub` — то, к чему подключается фронт.

**Публичный API BFF (HTTP endpoints — это вся поверхность для фронта):**
`Onboard`, `SetCharacterName`, `ChangeAvatar`, `GetPlayerCard`, `AllocateStats`,
`GetGameState`, `GetOnlinePlayers`, `JoinQueue`, `LeaveQueue`, `GetQueueStatus`,
`Heartbeat`, `GetBattleFeed`, `GetConversations`, `GetConversationMessages`,
`GetDirectMessages`, `Health`.

### 2.6 Kombats.Common — разделяемые библиотеки

- `Kombats.Abstractions`: `Result`/`Error` (railway-style, без исключений для control-flow),
  `ICommand`/`IQuery`, auth-хелперы (`IdentityIdExtensions`, `KombatsAuthExtensions` — JwtBearer).
- `Kombats.Messaging`: DI-расширения MassTransit, naming conventions (`EntityNameConvention`,
  `CombatsEndpointNameFormatter`), `ConsumeLoggingFilter`.
- `Kombats.Observability`: `KombatsMetrics`, OTel-расширения.

---

## 3. Доменный поток (как сервисы общаются)

Связь между сервисами — **асинхронная через RabbitMQ/MassTransit** (события + команды).
Синхронно (HTTP) ходит только BFF вниз и Chat→Players.

```
Players ──PlayerCombatProfileChanged──▶ Matchmaking, Chat
Matchmaking ──CreateBattle (command)──▶ Battle
Battle ──BattleCreated──▶ Matchmaking
Battle ──BattleCompleted──▶ Players (начисление), Matchmaking (освобождение)
Battle ──realtime events──▶ (internal SignalR) ──▶ BFF Relay ──▶ Client
```

**Realtime-контракты** (`Battle.Realtime.Contracts`, отдельный проект): `TurnOpenedRealtime`,
`TurnResolvedRealtime`, `AttackResolutionRealtime`, `PlayerDamagedRealtime`,
`BattleStateUpdatedRealtime`, `BattleReadyRealtime`, `BattleEndedRealtime`,
`BattleSnapshotRealtime` и т.д. (имена событий — в `RealtimeEventNames`).

### Сквозной gameplay-loop (end-to-end)

1. Игрок логинится (Keycloak OIDC) → `Onboard` → создаётся персонаж (Players).
2. Прокачка характеристик (`AllocateStats`) → Players публикует `PlayerCombatProfileChanged`.
3. `JoinQueue` → Matchmaking ставит в очередь (Redis), Heartbeat держит presence.
4. `MatchmakingPairingWorker` находит пару → публикует `CreateBattle`.
5. Battle создаёт бой (состояние в Redis), публикует `BattleCreated`, шлёт `BattleReady` realtime.
6. Игроки ходят через BFF → Battle.Engine применяет ход → realtime-события идут через
   BFF Relay на оба клиента; `NarrationPipeline` добавляет реплики комментатора.
7. Бой завершается → Battle публикует `BattleCompleted` → Players начисляет результат,
   Matchmaking освобождает игроков.

Интеграционные тесты эталона ровно это и проверяют: `I01_PlayersToMatchmaking`,
`I02_MatchmakingToBattle`, `I03_BattleCompletion`, `I04_EndToEndGameplayLoop`.

---

## 4. Frontend (Kombats.Client) — React 19 SPA

Строгая 4-слойная архитектура с изоляцией (правила **обязательны**):

```
app/        → shell, роутинг, guards, entry point
modules/    → фичи: auth, onboarding, player, matchmaking, battle, chat (стейт + экраны + компоненты)
transport/  → http / signalr / polling — НЕТ UI, НЕТ React, НЕТ сторов
ui/         → stateless примитивы (components/hooks/theme/assets) — НЕТ бизнес-логики
types/      → общие TypeScript-типы
```

**Жёсткие правила (forbidden patterns):**
- `transport/` без React/Zustand/TanStack Query; `ui/` без сторов и транспорта.
- Все сетевые вызовы — только через `transport/` (никаких `fetch()`/`new HubConnection()` в компонентах).
- Auth-токены **только в памяти**, не в `localStorage` (DEC-6: XSS-риск из-за чата).
- Роуты — проекция состояния (никаких `navigate()` в feature-компонентах).
- Только Tailwind + CSS-переменные (никаких inline-стилей/CSS-модулей/хардкод-цветов).
- Named exports (без default), без `React.FC`, без `any` без обоснования.
- Данные — через TanStack Query, не через `useEffect`.

---

## 5. Tech stack (точные версии из эталона)

### Backend (.NET)
| Что | Технология | Версия |
|---|---|---|
| Runtime / SDK | .NET | 10.0.100 (`global.json`, rollForward latestPatch) |
| ORM | EF Core + Npgsql | 10.0.3 / 10.0.0 (+ `EFCore.NamingConventions`) |
| БД | PostgreSQL | 16-alpine, **schema-per-service** |
| Messaging | MassTransit + RabbitMQ.Client | 8.5.9 / 7.2.1 |
| Cache/State | StackExchange.Redis | 2.8.16 (Battle=DB0, Matchmaking=DB1) |
| Realtime | ASP.NET Core SignalR + StackExchangeRedis backplane | 10.0.3 |
| Auth | Keycloak (OIDC) + JwtBearer | 10.0.3 |
| Validation | FluentValidation | 12.1.1 |
| Resilience | Microsoft.Extensions.Http.Resilience (Polly) | 10.0.0 |
| DI scanning | Scrutor | 7.0.0 |
| Logging | Serilog | 10.0.0 |
| Observability | OpenTelemetry (traces/metrics) | 1.15.0 |
| API docs | OpenAPI + Scalar | — |
| Health | AspNetCore.HealthChecks (NpgSql/Rabbit/Redis) | 9.0.0 |
| Tests | xUnit 2.9.3 + Microsoft.NET.Test.Sdk 17.12.0 | — |
| Load tests | NBomber 6.4.0 (C#) + Locust (Python) | — |

### Frontend (фиксированный стек — альтернативы не предлагать)
React 19.2 · Vite 8 · React Router 7 · Zustand 5 · TanStack Query 5 ·
@microsoft/signalr 8 · oidc-client-ts 3 + react-oidc-context 3 · Tailwind 4 ·
Radix UI · Motion (Framer) 12 · Vitest 4 · TypeScript 6 · lucide-react · date-fns · clsx.

---

## 6. Ключевые паттерны (что стоит понять и осознанно воссоздать)

| Паттерн | Где в эталоне | Зачем |
|---|---|---|
| **Clean Architecture / layered DDD** | каждый сервис | Domain не зависит от инфраструктуры; тестируемость. |
| **BFF** | `Kombats.Bff` | Один фасад для фронта; агрегация + проброс auth + SignalR-мост. |
| **Outbox** | Players (EF-миграция) | Атомарность "запись в БД + публикация события". |
| **Inbox / Idempotency** | Players (`IInboxRepository`) | Dedup входящих событий (at-least-once delivery). |
| **Distributed lease-lock** | Matchmaking (`RedisLeaseLock`) | Один подборщик пары на тик при N репликах. |
| **State в Redis + Lua** | Battle (`RedisBattleStateStore`) | Авторитетное состояние боя, atomic-операции, низкая латентность. |
| **SignalR Redis backplane** | Battle | Realtime поверх нескольких реплик. |
| **BFF SignalR Relay** | Bff (`BattleHubRelay`) | Мост internal-hub → external-hub, управление коннекциями. |
| **Distributed rate-limit** | Chat (`RedisRateLimiter`) | Защита чата от флуда поверх реплик. |
| **Result/Error railway** | Common.Abstractions | Ошибки как значения, без исключений в control-flow. |
| **Central Package Management** | `Directory.Packages.props` | Единые версии пакетов на монорепо. |
| **Schema-per-service** | Postgres | Изоляция данных сервисов в одной БД. |

---

## 7. Инфраструктура и observability

- **Postgres** (schema-per-service: players/matchmaking/chat/battle), **Redis** (DB0 Battle, DB1 Matchmaking),
  **RabbitMQ** (MassTransit), **Keycloak** + отдельная `keycloak-db` + `keycloak-bootstrap` (realm/clients/themes).
- **Миграции** прогоняются отдельным образом `Kombats.Migrator` (как Job — в Container Apps / k8s).
- **Observability-стек:** OTel Collector → Prometheus (метрики) + Jaeger (трейсы) + Grafana (дашборды).
  ⚠️ Подводный камень эталона: без файла `observability/docker-compose.observability.override.yml`
  в compose-цепочке сервисы стартуют с пустым `OtlpEndpoint` и **молча теряют телеметрию**
  (защитный WARN `Kombats.Observability` это ловит).

---

## 8. Локальный запуск (3 режима из эталона)

- **Mode A — полный стек** (для замеров): `docker-compose.yml` + оба observability-файла +
  `docker-compose.override.yml`. Все 5 сервисов + инфра + телеметрия.
- **Mode B — multi-replica** (тест backplane / capacity): Mode A + `docker-compose.multi-replica.yml`
  (вторая реплика Battle).
- **Mode C — IDE mode** (`docker-compose.local.yml`): только инфра (Postgres/Redis/RabbitMQ/Keycloak),
  сами .NET-сервисы запускаются с хоста из IDE/`dotnet run`. Отдельное имя проекта `kombats-local`.

Keycloak требует `127.0.0.1 keycloak` в `/etc/hosts`.

---

## 9. Деплой в Azure (IaC)

- **Bicep**, subscription-scope: `infra/main.bicep` создаёт Resource Group → модуль `workload.bicep`.
- `workload.bicep` поднимает: Log Analytics, **Container Apps Environment**, Azure Files storage,
  stateful-апсы (Postgres/Redis/RabbitMQ как Container Apps), backend-апсы (5 сервисов),
  Keycloak-апп, **migrator-job**, **static-web-app** (фронт).
- 8 модулей в `infra/modules/`. Образы — в GHCR (`ghcr.io/sorokinartemv/kombats-*`).
- CI/CD: Azure DevOps (`azure-pipelines.yml` + `pipelines/build-backend.yml`, `deploy-stack.yml`, `teardown.yml`).

---

## 10. Рекомендованный порядок воссоздания руками

Это предложение, не догма. Принцип: **вертикальный срез раньше горизонтальной полноты** —
сначала тонкая сквозная нить, потом наращивание.

1. **Каркас монорепо:** solution, `Directory.*.props`, `global.json`, один пустой сервис по
   слоям (Domain/Application/Infrastructure/Api/Bootstrap), Docker-compose с Postgres.
2. **Players** целиком (это "входной" сервис): профиль, onboarding, EF + миграции, JwtBearer/Keycloak.
3. **Common** по ходу: `Result/Error`, `Messaging` (MassTransit), `Observability`.
4. **Matchmaking:** очередь на Redis, lease-lock, pairing-worker, контракт `CreateBattle`.
5. **Battle:** движок (Domain, чистая логика — легко тестировать), state в Redis, обработка хода,
   `BattleCompleted`. Realtime — отдельным шагом.
6. **BFF:** HTTP-агрегатор + endpoints, потом SignalR Relay.
7. **Realtime:** SignalR в Battle → Relay в BFF → клиент. Backplane — когда дойдёшь до реплик.
8. **Chat:** presence, rate-limit, ретенция.
9. **Frontend:** transport → ui → modules, по фичам в порядке gameplay-loop.
10. **Observability → Load tests → Azure IaC** в конце.

Outbox/Inbox, backplane, multi-replica — это **усиления**, добавляемые после того, как
сквозной путь уже работает в один-реплику. В эталоне они и появлялись "главами" (Chapters 0–4).

---

## 11. Честные ограничения этого среза

- Обязанности сервисов и часть связей выведены из имён файлов/проектов и контрактов, а не
  прочитаны построчно во всей бизнес-логике. При сомнении — открыть конкретный файл в эталоне.
- Точный механизм Outbox (MassTransit EFCore-Outbox vs кастом) и точные RabbitMQ-маршруты
  стоит подтверждать в `MessagingServiceCollectionExtensions.cs` и `Bootstrap/Program.cs`.
- Папки `docs/frontend/*`, на которые ссылается корневой `CLAUDE.md`, в текущем срезе
  отсутствуют — ориентироваться на код и этот документ.
- Точные Keycloak auth-flows, состав дашбордов Grafana и список метрик — смотреть в
  `KEYCLOAK_BOOTSTRAP.md`, `observability/` и `KombatsMetrics.cs`.
- Числа (.cs-файлы, строки) — снимок на момент среза, могут дрейфовать.

