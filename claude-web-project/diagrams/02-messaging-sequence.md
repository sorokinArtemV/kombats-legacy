# Kombats — Messaging & Gameplay-loop (sequence + каталог событий)

Сквозной путь от входа игрока до завершения боя. `->>` = синхронный HTTP/вызов,
`-)` = публикация/доставка через RabbitMQ (async, at-least-once).

```mermaid
sequenceDiagram
  autonumber
  actor U as Player
  participant C as Client
  participant B as BFF
  participant P as Players
  participant M as Matchmaking
  participant Bt as Battle
  participant Q as RabbitMQ
  participant R as Redis

  U->>C: вход (OIDC / Keycloak)
  C->>B: Onboard → SetCharacterName → AllocateStats
  B->>P: HTTP (+JWT)
  P-)Q: PlayerCombatProfileChanged
  Q-)M: обновить combat-profile
  Q-)Bt: (Chat также consume)

  C->>B: JoinQueue + Heartbeat (presence)
  B->>M: HTTP
  M->>R: enqueue + presence (DB1)

  Note over M,R: PairingWorker tick под lease-lock<br/>(только одна реплика подбирает)
  M->>R: acquire lease (SET NX PX / Lua)
  M-)Q: CreateBattle (command)
  Q-)Bt: CreateBattle

  Bt->>R: init battle state (DB0, Lua)
  Bt-)Q: BattleCreated
  Q-)M: match → BattleCreated
  Bt-->>B: BattleReady (SignalR)
  B-->>C: BattleReady (relay + narration)

  loop каждый ход (BattlePhase: TurnOpen → Resolving)
    C->>B: PlayTurn (зона атаки / блок)
    B->>Bt: HTTP
    Bt->>R: apply turn (atomic Lua) + turn-history
    Bt-->>B: TurnResolved · PlayerDamaged · BattleStateUpdated
    B-->>C: realtime + реплики «комментатора»
  end

  Note over Bt: HP<=0 / timeout / forfeit → BattlePhase.Ended
  Bt-)Q: BattleCompleted
  Q-)P: начислить XP/Win/Loss (Inbox dedup)
  Q-)M: освободить игроков (Match → Completed)
  Bt-->>B: BattleEnded (reason)
  B-->>C: BattleEnded
```

## Каталог сообщений

| Сообщение | Тип | Producer | Consumer(s) | Назначение |
|---|---|---|---|---|
| `PlayerCombatProfileChanged` | event | Players | Matchmaking, Chat | Боевой профиль/имя изменились (после AllocateStats и т.п.) |
| `CreateBattle` | command | Matchmaking | Battle | Создать бой для подобранной пары |
| `BattleCreated` | event | Battle | Matchmaking | Бой создан → матч переходит в `BattleCreated` |
| `BattleCompleted` | event | Battle | Players, Matchmaking | Итог боя → начисление прогресса + освобождение игроков |
| `TurnOpenedRealtime`, `TurnResolvedRealtime`, `AttackResolutionRealtime`, `PlayerDamagedRealtime`, `BattleStateUpdatedRealtime`, `BattleReadyRealtime`, `BattleEndedRealtime`, `BattleSnapshotRealtime` | realtime | Battle | Battle internal SignalR → **BFF Relay** → Client | Покадровое состояние боя для обоих клиентов |

**Ключевые заметки**
- **Idempotency:** Players дедуплицирует входящие события через **Inbox** (`InboxMessage`,
  PK `MessageId`) — защита от повторной доставки.
- **Lease:** в момент подбора только одна реплика Matchmaking держит lease — нет двойного матча.
- Realtime-события не идут в шину игрокам напрямую: Battle → internal hub → **BFF Relay** → клиент.
