# ⚔️ Kombats

> A turn-based browser fighting game with an East-Asian aesthetic — built as a **production-grade microservice playground**.

Players create a fighter, allocate stats, queue for matchmaking, and fight 1-on-1 in real time (turns + a live battle commentator + chat). Under the hood it is **not a CRUD app** — the interesting part is distributed coordination, concurrency, and realtime: Clean Architecture, async messaging, Outbox/Inbox, distributed locks, a SignalR backplane, and a full observability stack.

<p>
  <img alt=".NET 10" src="https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white">
  <img alt="React 19" src="https://img.shields.io/badge/React-19-61DAFB?logo=react&logoColor=black">
  <img alt="PostgreSQL" src="https://img.shields.io/badge/PostgreSQL-16-4169E1?logo=postgresql&logoColor=white">
  <img alt="Redis" src="https://img.shields.io/badge/Redis-state%20%26%20locks-DC382D?logo=redis&logoColor=white">
  <img alt="RabbitMQ" src="https://img.shields.io/badge/RabbitMQ-MassTransit-FF6600?logo=rabbitmq&logoColor=white">
  <img alt="SignalR" src="https://img.shields.io/badge/SignalR-realtime-5C2D91">
  <img alt="Keycloak" src="https://img.shields.io/badge/Keycloak-OIDC-4D4D4D">
  <img alt="OpenTelemetry" src="https://img.shields.io/badge/OpenTelemetry-traces%20%26%20metrics-F5A800?logo=opentelemetry&logoColor=white">
</p>

---

## Architecture

The client talks **only** to the BFF (HTTP + SignalR). Backend services communicate **asynchronously over RabbitMQ** — the single synchronous cross-service exception is `Chat → Players`.

```mermaid
flowchart LR
  classDef client fill:#1e3a5f,stroke:#4a90d9,color:#fff
  classDef bff fill:#5f3a1e,stroke:#d99a4a,color:#fff
  classDef svc fill:#1e4d2b,stroke:#4ad97a,color:#fff
  classDef infra fill:#3a1e5f,stroke:#9a4ad9,color:#fff
  classDef ident fill:#5f1e3a,stroke:#d94a90,color:#fff
  classDef obs fill:#444,stroke:#aaa,color:#fff

  Client["React 19 SPA<br/>Vite · Zustand · TanStack Query · SignalR"]:::client

  subgraph Edge["Edge / BFF"]
    BFF["BFF (.NET 10)<br/>HTTP aggregator · SignalR relay · Narration"]:::bff
  end

  subgraph Services["Backend Services (.NET 10, layered DDD)"]
    Players["Players<br/>profiles · progression · Outbox/Inbox"]:::svc
    MM["Matchmaking<br/>queue · lease-lock · pairing worker"]:::svc
    Battle["Battle<br/>engine · turn state · realtime"]:::svc
    Chat["Chat<br/>messages · presence · rate-limit"]:::svc
  end

  subgraph Infra["Infrastructure (stateful)"]
    PG[("PostgreSQL 16<br/>schema-per-service")]:::infra
    Redis[("Redis<br/>DB0 Battle state · DB1 MM queue")]:::infra
    MQ[["RabbitMQ<br/>MassTransit"]]:::infra
  end

  KC["Keycloak<br/>OIDC / JWT"]:::ident

  subgraph Obs["Observability"]
    OTel["OTel Collector"]:::obs
    Prom["Prometheus"]:::obs
    Jaeger["Jaeger"]:::obs
    Graf["Grafana"]:::obs
  end

  Client -->|"HTTPS REST"| BFF
  Client -->|"SignalR / WS"| BFF
  Client -.->|"OIDC login"| KC

  BFF -->|"HTTP + JWT forward"| Players
  BFF -->|"HTTP + JWT forward"| MM
  BFF -->|"HTTP + JWT forward"| Battle
  BFF -->|"HTTP + JWT forward"| Chat
  BFF <-->|"SignalR relay"| Battle
  BFF <-->|"SignalR relay"| Chat

  Chat -->|"HTTP (eligibility / display-name)"| Players

  Players -.->|"PlayerCombatProfileChanged"| MQ
  MM -.->|"CreateBattle"| MQ
  Battle -.->|"BattleCreated · BattleCompleted"| MQ
  MQ -.-> Players
  MQ -.-> MM
  MQ -.-> Battle
  MQ -.-> Chat

  Players --- PG
  MM --- PG
  Chat --- PG
  Battle --- PG
  Battle --- Redis
  MM --- Redis

  Players & MM & Battle & Chat & BFF -.->|"validate JWT"| KC
  Players & MM & Battle & Chat & BFF -.->|"traces / metrics"| OTel
  OTel --> Prom --> Graf
  OTel --> Jaeger
```

**Solid line** = synchronous (REST / SignalR). **Dashed line** = async message over RabbitMQ (label = event/command name). Battle and Matchmaking keep *hot* state in Redis; durable state lives in Postgres.

---

## Services

Each live service follows **layered DDD / Clean Architecture** (`Api/Bootstrap → Application → Domain`, `Infrastructure → Application/Domain`; Domain depends on nothing).

| Service | Responsibility | Notable engineering |
|---|---|---|
| **Players** | Profiles, character, stat progression | **Outbox + Inbox** for idempotent, transactional messaging |
| **Matchmaking** | Redis queue, presence, pairing | **Distributed lease-lock** so one replica pairs per tick; bounded pairing loop + idle backoff |
| **Battle** | Deterministic combat engine, turn state, realtime | Authoritative **state in Redis (+ Lua)**; **SignalR Redis backplane** for multi-replica realtime; recovery worker |
| **Chat** | Conversations, presence, retention | **Distributed rate-limit** over replicas |
| **BFF** | Single facade for the frontend | HTTP aggregation, JWT forwarding (Polly resilience), **SignalR relay**, battle-commentator narration pipeline |

### End-to-end gameplay loop

1. Login (Keycloak OIDC) → `Onboard` → character created (Players)
2. `AllocateStats` → Players publishes `PlayerCombatProfileChanged`
3. `JoinQueue` → Matchmaking enqueues (Redis); heartbeat keeps presence
4. Pairing worker finds a pair → publishes `CreateBattle`
5. Battle creates the fight (state in Redis), publishes `BattleCreated`, sends `BattleReady` realtime
6. Players take turns via BFF → engine resolves the turn → realtime events fan out through the BFF relay to both clients; the narration pipeline adds commentator lines
7. Battle ends → `BattleCompleted` → Players awards results, Matchmaking releases players

> Integration tests assert exactly this: `I01_PlayersToMatchmaking`, `I02_MatchmakingToBattle`, `I03_BattleCompletion`, `I04_EndToEndGameplayLoop`.

---

## Key patterns

| Pattern | Where | Why |
|---|---|---|
| Clean Architecture / layered DDD | every service | Domain isolated from infrastructure; testable |
| Backend-for-Frontend | `Kombats.Bff` | One facade: aggregation + auth forwarding + SignalR bridge |
| Outbox | Players | Atomic "write to DB + publish event" |
| Inbox / Idempotency | Players | Dedup of at-least-once deliveries |
| Distributed lease-lock | Matchmaking (`RedisLeaseLock`) | One pairer per tick across N replicas |
| State in Redis + Lua | Battle (`RedisBattleStateStore`) | Authoritative low-latency battle state, atomic ops |
| SignalR Redis backplane | Battle | Realtime across multiple replicas |
| Distributed rate-limit | Chat (`RedisRateLimiter`) | Flood protection across replicas |
| Result/Error railway | `Common.Abstractions` | Errors as values — no exceptions for control flow |
| Central Package Management | `Directory.Packages.props` | Single source of truth for package versions |
| Schema-per-service | PostgreSQL | Data isolation within one database |

---

## Tech stack

**Backend** — .NET 10 · EF Core + Npgsql · PostgreSQL 16 (schema-per-service) · MassTransit + RabbitMQ · StackExchange.Redis · ASP.NET Core SignalR (+ Redis backplane) · Keycloak (OIDC/JWT) · FluentValidation · Polly resilience · Serilog · OpenTelemetry · xUnit · NBomber + Locust (load tests)

**Frontend** — React 19 · Vite · React Router 7 · Zustand 5 · TanStack Query 5 · `@microsoft/signalr` 8 · oidc-client-ts + react-oidc-context · Tailwind CSS 4 · Radix UI · Motion (Framer) · Vitest · TypeScript

The frontend follows a strict 4-layer architecture (`app / modules / transport / ui`) with hard isolation rules — auth tokens are kept **in memory only** (no `localStorage`), and all network access goes through `transport/`.

---

## Repository structure

```
Kombats/
├── src/
│   ├── Kombats.Battle/        # combat engine, turn state, recovery, realtime
│   ├── Kombats.Matchmaking/   # queue, lease-locks, pairing ticks
│   ├── Kombats.Players/       # profiles, progression, Outbox/Inbox
│   ├── Kombats.Chat/          # chat, presence, rate-limit, retention
│   ├── Kombats.Bff/           # Backend-for-Frontend (API + SignalR relay)
│   ├── Kombats.Common/        # Abstractions / Messaging / Observability
│   ├── Kombats.Migrator/      # EF migrations runner image
│   └── Kombats.Client/        # React 19 + Vite SPA
├── tests/                     # unit/component per service + Integration + LoadTests
├── infra/                     # Bicep IaC + Keycloak bootstrap (realm/clients/themes)
├── observability/             # Grafana / Prometheus / OTel Collector configs
├── pipelines/                 # Azure DevOps CI/CD
├── scripts/                   # deploy-stack, run-migrations, show-tree
└── docker-compose.*.yml       # local / full-stack / multi-replica / capacity
```

---

## Getting started

> Prerequisites: Docker + Docker Compose, .NET 10 SDK (for IDE mode).
> Add `127.0.0.1 keycloak` to your `/etc/hosts` so OIDC redirects resolve.

### Mode A — Full stack (default)

All backend services + infrastructure + observability:

```bash
docker compose \
  -f docker-compose.yml \
  -f observability/docker-compose.observability.yml \
  -f observability/docker-compose.observability.override.yml \
  -f docker-compose.override.yml \
  up -d --build
```

> ⚠️ The `observability.override.yml` file is **required** — without it services start with an empty `OtlpEndpoint` and silently drop telemetry. A defensive `WARN Kombats.Observability` log surfaces this if your compose chain is wrong.

### Mode B — Multi-replica

Mode A plus a second Battle replica (tests the SignalR backplane / sustained capacity):

```bash
docker compose \
  -f docker-compose.yml \
  -f observability/docker-compose.observability.yml \
  -f observability/docker-compose.observability.override.yml \
  -f docker-compose.override.yml \
  -f docker-compose.multi-replica.yml \
  up -d --build
```

### Mode C — IDE mode

Only the stateful infrastructure (Postgres / Redis / RabbitMQ / Keycloak); run the .NET services from your IDE or `dotnet run`:

```bash
docker compose -f docker-compose.local.yml up -d
```

---

## Testing

```bash
dotnet test                      # unit, component & integration tests
```

Load tests live in `tests/Kombats.LoadTests/` (NBomber + a Locust migration) — see its `README.md`.

---

## Deployment

Infrastructure as Code targets **Azure Container Apps** via Bicep (`infra/main.bicep` → `workload.bicep`): Container Apps Environment, stateful apps (Postgres/Redis/RabbitMQ), the five backend apps, Keycloak, a migration job, and a static web app for the frontend. Container images are published to GHCR. CI/CD is defined under `pipelines/` (Azure DevOps).

<img width="2552" height="1381" alt="bat" src="https://github.com/user-attachments/assets/1ebc17c6-77cb-47ec-b6cd-d61ff4c6431f" />
