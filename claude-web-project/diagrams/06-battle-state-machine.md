# Kombats — State machine (матч + бой)

Жизненный цикл от подбора до завершения. Состояния и причины — из реальных enum-ов эталона
(`MatchState`, `BattlePhase`, `BattleEndReason`).

## Match (Matchmaking) — `MatchState`

```mermaid
stateDiagram-v2
  [*] --> Queued: JoinQueue
  Queued --> BattleCreateRequested: PairingWorker (под lease) → CreateBattle
  BattleCreateRequested --> BattleCreated: BattleCreated event
  BattleCreated --> Completed: BattleCompleted event
  Queued --> TimedOut: MatchTimeoutWorker
  BattleCreateRequested --> TimedOut: timeout
  Queued --> Cancelled: LeaveQueue
  Completed --> [*]
  TimedOut --> [*]
  Cancelled --> [*]
```

## Battle — `BattlePhase` + `BattleEndReason`

```mermaid
stateDiagram-v2
  [*] --> ArenaOpen: CreateBattle (state в Redis)
  ArenaOpen --> TurnOpen: оба готовы / BattleReady
  TurnOpen --> Resolving: оба сделали ход
  TurnOpen --> Resolving: TurnDeadlineWorker (дедлайн хода)
  Resolving --> TurnOpen: раунд разрешён, HP > 0
  Resolving --> Ended: HP <= 0 / forfeit / timeout
  TurnOpen --> Ended: оба не ходят (DoubleForfeit)

  note right of Ended
    BattleEndReason:
    Normal · DoubleForfeit · Timeout
    Cancelled · AdminForced · SystemError
  end note

  state Recovery {
    [*] --> Restored: BattleRecoveryWorker
    note right of Restored
      после рестарта активный бой
      восстанавливается из Redis-состояния
    end note
  }

  Ended --> [*]: publish BattleCompleted
```

**Триггеры (легенда)**
- `CreateBattle` — команда от Matchmaking создаёт бой (состояние инициализируется в Redis).
- Ход игрока — выбор зоны атаки/блока через BFF → применяется движком.
- `TurnDeadlineWorker` — если игрок не походил вовремя, ход форсируется/таймаутится.
- HP `<= 0` (`PlayerState.IsDead`) → `Ended` с `Normal`.
- Оба не ходят → `DoubleForfeit`; общий таймаут → `Timeout`.
- `BattleRecoveryWorker` — восстановление активных боёв после рестарта реплики из Redis.
