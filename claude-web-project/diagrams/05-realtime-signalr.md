# Kombats — Realtime / SignalR Relay

Как событие боя доходит до обоих игроков, и зачем нужны Relay + Redis backplane.

```mermaid
flowchart LR
  classDef svc fill:#1e4d2b,stroke:#4ad97a,color:#fff
  classDef bff fill:#5f3a1e,stroke:#d99a4a,color:#fff
  classDef infra fill:#3a1e5f,stroke:#9a4ad9,color:#fff
  classDef client fill:#1e3a5f,stroke:#4a90d9,color:#fff

  subgraph BattleReplicas["Battle service (N реплик)"]
    B1["battle-1<br/>BattleHub + SignalRBattleRealtimeNotifier"]:::svc
    B2["battle-2<br/>BattleHub"]:::svc
  end

  BP[("Redis backplane<br/>SignalR.StackExchangeRedis")]:::infra

  subgraph BFFn["BFF"]
    Relay["BattleHubRelay<br/>internal→external мост<br/>ConcurrentDictionary конн."]:::bff
    Narr["NarrationPipeline<br/>комментатор боя"]:::bff
    ExtHub["BattleHub (external)"]:::bff
  end

  P1["Client — Player A<br/>@microsoft/signalr"]:::client
  P2["Client — Player B"]:::client

  B1 <-->|fan-out между репликами| BP
  B2 <-->|fan-out между репликами| BP
  B1 -->|"realtime events"| Relay
  B2 -->|"realtime events"| Relay
  Relay --> Narr --> ExtHub
  ExtHub -->|"TurnResolved · PlayerDamaged · BattleEnded"| P1
  ExtHub -->|"тот же поток"| P2
```

### Один ход — sequence

```mermaid
sequenceDiagram
  participant Bt as Battle (реплика)
  participant BP as Redis backplane
  participant Re as BFF Relay
  participant C1 as Client A
  participant C2 as Client B
  Bt->>BP: publish TurnResolved (group=battleId)
  BP-->>Re: доставка подписчикам группы
  Re->>Re: Narration: добавить реплику комментатора
  Re-->>C1: TurnResolved + narration
  Re-->>C2: TurnResolved + narration
```

**Зачем так**
- **Redis backplane** — клиенты одного боя могут висеть на разных репликах Battle; backplane
  рассылает событие всем репликам группы (+ skip-negotiation для прямого WS-коннекта).
- **BFF Relay** — клиент не коннектится к Battle напрямую: BFF держит единый внешний хаб,
  пробрасывает auth, управляет состоянием коннекций и обогащает поток нарративом. Это и
  изоляция (фронт знает только BFF), и точка для комментатора/агрегации.
