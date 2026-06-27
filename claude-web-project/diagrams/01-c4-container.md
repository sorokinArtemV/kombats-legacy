# Kombats — C4 Container Diagram (карта сервисов)

Высокоуровневая карта системы: контейнеры и связи. Источник — реальный код эталона
(`§1–§3, §7` knowledge-файла). Сплошные линии = синхронно (REST/SignalR),
пунктир = async через RabbitMQ.

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
    BFF["BFF (.NET 10)<br/>HTTP aggregator · SignalR Relay · Narration"]:::bff
  end

  subgraph Services["Backend Services (.NET 10, layered DDD)"]
    Players["Players<br/>profiles · progression · Outbox/Inbox"]:::svc
    MM["Matchmaking<br/>queue · lease-lock · pairing worker"]:::svc
    Battle["Battle<br/>engine · turn state · realtime"]:::svc
    Chat["Chat<br/>messages · presence · rate-limit"]:::svc
  end

  subgraph Infra["Infrastructure (stateful)"]
    PG[("PostgreSQL 16<br/>schema-per-service")]:::infra
    Redis[("Redis<br/>DB0 Battle state · DB1 MM queue · cache")]:::infra
    MQ[["RabbitMQ<br/>MassTransit 8.5"]]:::infra
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
  Chat --- Redis

  Players & MM & Battle & Chat & BFF -.->|"validate JWT"| KC
  Players & MM & Battle & Chat & BFF -.->|"traces / metrics"| OTel
  OTel --> Prom --> Graf
  OTel --> Jaeger
```

**Легенда**
- **Сплошная** стрелка с подписью протокола — синхронный вызов (REST / SignalR).
- **Пунктир** — асинхронное сообщение через RabbitMQ (на стрелке — имя события/команды).
- Клиент общается **только** с BFF. Сервисы между собой — через шину; единственное синхронное
  межсервисное исключение — Chat → Players (HTTP).
- Battle и Matchmaking держат «горячее» состояние в Redis, устойчивое — в Postgres.
