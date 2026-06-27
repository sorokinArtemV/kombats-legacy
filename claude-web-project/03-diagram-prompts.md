# Kombats — Промпты на схемы (copy-paste)

Готовые промпты для агента в Claude Project. Все схемы — в **Mermaid** (рендерится прямо
в claude.ai, переносится в GitHub / Notion / Obsidian / VS Code). Перед запуском убедись, что
в knowledge проекта загружен `01-kombats-project-context.md` — промпты на него опираются.

**Какие схемы стоит иметь** (то, что реально любят в проектах):
1. **C4 / Container diagram** — сервисы и связи (главная «карта»).
2. **Sequence + Event catalog** — messaging и доменный поток.
3. **ER-схемы БД** (schema-per-service).
4. **Deployment diagram** — как это лежит в Azure.
5. **Realtime / SignalR Relay** — как идёт realtime до клиента.
6. **State machine боя** — жизненный цикл матча/боя.
7. **Frontend layers** — 4 слоя клиента.
8. **Matchmaking concurrency** — lease-lock и гонки (изюминка проекта).

Совет: проси по одной схеме за раз — так агент даёт чище и подробнее. Ниже каждый промпт —
самостоятельный блок.

---

## 1. C4 Container Diagram — карта сервисов и связей

```
Нарисуй профессиональную C4-style Container-диаграмму системы Kombats в Mermaid.

Требования:
- Используй `flowchart LR` (или `graph LR`) с subgraph-группировкой по зонам:
  «Client», «Edge/BFF», «Backend Services», «Infrastructure (stateful)», «Identity», «Observability».
- Контейнеры: React SPA (Client); BFF; 5 backend-сервисов — Players, Matchmaking, Battle, Chat;
  Migrator (job); Postgres, Redis, RabbitMQ, Keycloak; OTel Collector → Prometheus + Jaeger + Grafana.
- Покажи характер связей разными стилями линий и подписями:
  • сплошные стрелки с подписью протокола для синхронных вызовов (HTTPS/REST, SignalR/WebSocket);
  • пунктирные стрелки для асинхронного обмена через RabbitMQ (с названием события/команды);
  • отдельные линии к Redis (state/queue/cache) и Postgres (schema-per-service).
- Клиент общается ТОЛЬКО с BFF (HTTP + SignalR). Backend-сервисы между собой — асинхронно
  через RabbitMQ; синхронный HTTP только BFF→downstream и Chat→Players.
- Подпиши на узлах ключевую технологию (например: «Battle\n.NET 10 · Redis state · SignalR»).
- Добавь classDef-стили: разные цвета для client / bff / services / infra / identity / observability,
  читаемые контрастные подписи. Линии-подписи краткие.

Опирайся строго на knowledge-файл «Kombats — Project Context» (§1–§3, §7). Не выдумывай
сервисов и связей, которых там нет. После диаграммы дай короткую легенду (3–6 строк):
что означает каждый стиль линии и цвет.
```

---

## 2. Messaging — Sequence diagram сквозного gameplay-loop + каталог событий

```
Построй для Kombats две вещи по knowledge-файлу (§3 «Доменный поток» и §2):

A) Mermaid `sequenceDiagram` сквозного gameplay-loop end-to-end. Участники (participant):
   Client, BFF, Players, Matchmaking, Battle, Chat, RabbitMQ, Redis.
   Покажи шаги: Onboard → AllocateStats → JoinQueue (+Heartbeat presence в Redis) →
   PairingWorker находит пару → CreateBattle → Battle создаёт бой (state в Redis) →
   BattleCreated/BattleReady → ходы игроков через BFF → realtime-события через BFF Relay
   на обоих клиентов → BattleCompleted → начисление в Players + освобождение в Matchmaking.
   Различай синхронные вызовы (сплошная стрелка `->>`) и публикацию/доставку через
   RabbitMQ (помечай как async, через `--)` или с заметкой `Note over RabbitMQ`).
   Используй `Note` для пояснения ключевых моментов (idempotency через Inbox, lease при подборе).

B) Под диаграммой — таблицу-каталог сообщений (Markdown):
   столбцы: Сообщение | Тип (event/command/realtime) | Producer | Consumer(s) | Назначение.
   Включи: PlayerCombatProfileChanged, CreateBattle, BattleCreated, BattleCompleted,
   и группу realtime (TurnOpened/TurnResolved/AttackResolution/PlayerDamaged/
   BattleStateUpdated/BattleReady/BattleEnded/BattleSnapshot) одной строкой с пометкой
   «Battle → internal SignalR → BFF Relay → Client».

Строго по knowledge-файлу. Где точный маршрут RabbitMQ не зафиксирован в knowledge — помечай
как «нужно свериться с MessagingServiceCollectionExtensions.cs / Bootstrap», не выдумывай.
```

---

## 3. ER-схемы баз данных (schema-per-service)

```
Спроектируй ER-диаграммы баз данных Kombats в Mermaid (`erDiagram`), исходя из knowledge-файла
(Postgres со schema-per-service: players, matchmaking, chat, battle; Battle также держит
авторитетное состояние боя в Redis, не в Postgres).

Сделай ОТДЕЛЬНУЮ erDiagram на каждую схему-сервис:
- players: профиль/персонаж, характеристики/прогрессия, + таблицы Outbox и Inbox
  (transactional messaging — покажи их явно как отдельные сущности).
- matchmaking: (что хранится в Postgres vs Redis — очередь/lease живут в Redis, отметь это
  заметкой над диаграммой; в Postgres — устойчивые сущности матчей, если есть).
- chat: conversations, messages (+ связи), отметь presence/rate-limit как Redis (не Postgres).
- battle: устойчивые данные боя/истории ходов в Postgres vs состояние в Redis — раздели заметкой.

Для каждой сущности укажи правдоподобные ключевые поля (PK/FK, типы), связи (1:N, N:M) с
подписями кардинальности. Где модель не зафиксирована в knowledge явно — помечай поля как
«предположительно, свериться с *DbContext + EF Migrations соответствующего сервиса» и не
выдавай догадку за факт.

После диаграмм — короткий блок «Что в Redis, а что в Postgres» (буллеты): какие данные
сознательно НЕ в реляционной БД и почему (латентность, atomic-операции, TTL/presence).
```

---

## 4. Deployment diagram — Azure Container Apps

```
Нарисуй deployment-диаграмму Kombats для Azure в Mermaid (`flowchart TB` с subgraph по
границам инфраструктуры), по knowledge-файлу §9 (Bicep / Container Apps).

Покажи:
- Azure Subscription → Resource Group → Container Apps Environment.
- Внутри Environment: 5 backend Container Apps (Players/Matchmaking/Battle/Chat/BFF),
  Keycloak app, stateful apps (Postgres/Redis/RabbitMQ как Container Apps), Migrator как Job.
- Static Web App для фронта (React SPA).
- Azure Files storage (для stateful), Log Analytics workspace.
- GHCR (ghcr.io/sorokinartemv/kombats-*) как источник образов — стрелки pull в Container Apps.
- Входящий трафик: пользователь → Static Web App (SPA) и → BFF (ingress) → backend.
- Пометь, что Migrator-job выполняется до/при деплое (EF migrations).

Сгруппируй визуально, добавь classDef-цвета для compute / stateful / identity / registry / edge.
Под диаграммой — буллеты «Что разворачивает какой Bicep-модуль» (main → workload → modules:
storage, env-storage, stateful-app, backend-app, keycloak-app, migrator-job, static-web-app).
Строго по knowledge-файлу §9; чего там нет — не добавляй.
```

---

## 5. Realtime / SignalR Relay — как realtime доходит до клиента

```
Построй Mermaid-диаграмму (комбинируй: `flowchart LR` для топологии + при желании короткий
`sequenceDiagram` для одного хода), показывающую realtime-путь Kombats по knowledge-файлу
(§2.3 Battle realtime, §2.5 BFF Relay, §6 backplane).

Покажи:
- Внутренний BattleHub в сервисе Battle (+ SignalRBattleRealtimeNotifier) — публикует
  realtime-события боя.
- Redis backplane (SignalR.StackExchangeRedis) между несколькими репликами Battle
  (battle-1, battle-2) — объясни заметкой, зачем (масштаб реплик + skip-negotiation).
- BFF Relay (BattleHubRelay) как мост internal-hub → external-hub; держит состояние
  коннекций (ConcurrentDictionary/SemaphoreSlim).
- Внешний BattleHub в BFF, к которому подключается React-клиент (@microsoft/signalr,
  transport/signalr).
- Narration pipeline в BFF, который обогащает поток репликами «комментатора боя».
- Двух игроков-клиентов, получающих один и тот же поток событий боя.

Покажи направление событий стрелками и подпиши на стрелках имена realtime-событий
(TurnOpened, TurnResolved, PlayerDamaged, BattleEnded). Под диаграммой — 3–5 строк:
зачем нужен Relay (а не прямой коннект клиента к Battle) и зачем backplane.
```

---

## 6. State machine боя (и матча)

```
Нарисуй в Mermaid (`stateDiagram-v2`) жизненный цикл боя Kombats по knowledge-файлу (§2.2–2.3, §3).

Покрой состояния от подбора до завершения, например:
- Matchmaking: Queued → Matched (pairing worker, под lease) → (timeout → back to Queued/Left).
- Battle: Created → Ready → TurnOpen → TurnResolving → (повтор по раундам) →
  Ended(reason). Учитывай ветки: дедлайн хода (TurnDeadlineWorker → авто-резолюция/таймаут),
  recovery после рестарта (BattleRecoveryWorker восстанавливает активный бой из Redis).
- Переходы подпиши событиями/триггерами (CreateBattle, ход игрока, дедлайн, оба готовы,
  HP<=0, дисконнект). Для финала покажи разные BattleEndReason (нормальная победа / таймаут /
  сдача-дисконнект) если они зафиксированы.

Где конкретные состояния/причины не зафиксированы в knowledge — помечай «свериться с
BattleEngine.cs / BattleEndReason.cs», не выдумывай точные имена. Под диаграммой — легенда
триггеров (3–6 строк).
```

---

## 7. Frontend layers — 4-слойная архитектура клиента

```
Нарисуй в Mermaid (`flowchart TB`) 4-слойную архитектуру React-клиента Kombats
(knowledge §4): app / modules / transport / ui / types.

Покажи:
- Слой app (shell, routing, guards) сверху; modules (auth, onboarding, player, matchmaking,
  battle, chat) — фичи; transport (http / signalr / polling); ui (stateless primitives);
  types (общие типы).
- Разрешённые направления зависимостей стрелками И запрещённые — отдельно (красным, перечёркнуто
  или в заметке): transport БЕЗ React/Zustand/Query; ui stateless (без сторов/транспорта);
  компоненты НЕ делают fetch()/new HubConnection() напрямую; модуль не пишет в чужой стор;
  токены только в памяти (не localStorage).
- На transport покажи, что только он ходит в BFF (HTTP + SignalR).

Под диаграммой — таблица «правило → почему» по forbidden patterns из knowledge §4.
classDef-цвета по слоям. Строго по knowledge-файлу.
```

---

## 8. Matchmaking concurrency — lease-lock и гонки (изюминка)

```
Построй Mermaid `sequenceDiagram`, объясняющий распределённый подбор пар в Kombats при
НЕСКОЛЬКИХ репликах Matchmaking (knowledge §2.2, §6 — RedisLeaseLock / MatchmakingLeaseService /
MatchmakingPairingWorker).

Участники: PairingWorker@replica-1, PairingWorker@replica-2, Redis (lease + queue), RabbitMQ.
Покажи:
- Оба воркера тикают одновременно и пытаются взять lease в Redis (SET NX PX / Lua).
- Только один получает lease → читает очередь → формирует пару → публикует CreateBattle →
  обновляет статусы игроков → отпускает lease. Второй получает отказ и уходит в idle backoff.
- Заметками (`Note`) объясни: зачем lease (иначе двойной матч одной пары / гонка),
  bounded loop + idle backoff (не жечь CPU), atomic-операции на очереди через Lua.
- Покажи защиту от «пары из одного игрока» и от повторной выдачи уже сматченного.

Под диаграммой — 4–6 строк: какие race conditions это закрывает и что было бы без lease.
Где детали алгоритма не зафиксированы в knowledge — помечай «свериться с RedisScripts.cs /
MatchmakingLeaseService.cs», не выдумывай точную реализацию Lua.
```

---

## Подсказки по стилю (можно добавлять к любому промпту)

```
Дополнительно по оформлению:
- Делай диаграмму читаемой: короткие подписи на узлах, группировка subgraph, не более ~25 узлов.
- Добавляй classDef со спокойной палитрой и контрастным текстом; разные роли — разные цвета.
- После КАЖДОЙ диаграммы давай краткую легенду и 2–4 строки «как читать».
- Если данных в knowledge не хватает — явно перечисли допущения отдельным блоком
  «Assumptions (свериться с эталоном: <файлы>)». Не выдавай догадки за факты.
- Выдавай корректный Mermaid, проверь синтаксис (стрелки, кавычки в подписях, отсутствие
  зарезервированных символов в id узлов).
```
