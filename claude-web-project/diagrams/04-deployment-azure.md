# Kombats — Deployment Diagram (Azure Container Apps)

Как система разворачивается в Azure через Bicep (`infra/main.bicep` → `workload.bicep` → modules).
Образы тянутся из GHCR.

```mermaid
flowchart TB
  classDef edge fill:#1e3a5f,stroke:#4a90d9,color:#fff
  classDef compute fill:#1e4d2b,stroke:#4ad97a,color:#fff
  classDef stateful fill:#3a1e5f,stroke:#9a4ad9,color:#fff
  classDef ident fill:#5f1e3a,stroke:#d94a90,color:#fff
  classDef reg fill:#5f3a1e,stroke:#d99a4a,color:#fff
  classDef ops fill:#444,stroke:#aaa,color:#fff

  User(["Browser"]):::edge
  GHCR[["GHCR<br/>ghcr.io/sorokinartemv/kombats-*"]]:::reg

  subgraph Sub["Azure Subscription"]
    subgraph RG["Resource Group (rg-kombats-*)"]
      SWA["Static Web App<br/>React SPA"]:::edge
      LA["Log Analytics<br/>workspace"]:::ops

      subgraph CAE["Container Apps Environment"]
        BFFc["BFF<br/>(ingress)"]:::compute
        Pc["Players"]:::compute
        Mc["Matchmaking"]:::compute
        Btc["Battle"]:::compute
        Cc["Chat"]:::compute
        KCc["Keycloak<br/>app"]:::ident

        PGc[("Postgres<br/>container app")]:::stateful
        Rc[("Redis<br/>container app")]:::stateful
        MQc[["RabbitMQ<br/>container app"]]:::stateful

        Job["Migrator Job<br/>EF migrations"]:::ops
      end

      Files[("Azure Files<br/>share (stateful volumes)")]:::stateful
    end
  end

  User -->|HTTPS| SWA
  User -->|HTTPS / SignalR| BFFc
  SWA -. API calls .-> BFFc

  BFFc --> Pc & Mc & Btc & Cc
  Pc & Mc & Cc & Btc --- PGc
  Btc & Mc & Cc --- Rc
  Pc & Mc & Btc & Cc --- MQc
  Pc & Mc & Btc & Cc & BFFc -. validate JWT .-> KCc

  GHCR -. pull images .-> BFFc & Pc & Mc & Btc & Cc & KCc & Job
  Job -->|migrate before serve| PGc
  PGc & Rc & MQc -. persist .-> Files
  CAE -. logs/metrics .-> LA
```

## Какой Bicep-модуль что разворачивает

- `main.bicep` (subscription scope) → создаёт **Resource Group** → вызывает `workload.bicep`.
- `workload.bicep` → Log Analytics, **Container Apps Environment**, и модули:
  - `storage.bicep` + `env-storage.bicep` → Azure Files share + монтирование в Environment.
  - `stateful-app.bicep` → Postgres / Redis / RabbitMQ как Container Apps.
  - `backend-app.bicep` → 5 backend-сервисов (Players/Matchmaking/Battle/Chat/BFF).
  - `keycloak-app.bicep` → Keycloak (+ отдельная БД/bootstrap).
  - `migrator-job.bicep` → Job, прогоняющий EF-миграции до старта сервисов.
  - `static-web-app.bicep` → хостинг React SPA.

> Образы публикуются в GHCR пайплайнами Azure DevOps (`azure-pipelines.yml`,
> `pipelines/build-backend.yml`, `deploy-stack.yml`). Детали ingress/секретов — в самих модулях.
