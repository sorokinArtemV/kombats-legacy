# Kombats — ER-схемы баз данных (schema-per-service)

PostgreSQL 16, **схема на сервис** (`players`, `matchmaking`, `chat`, `battle`).
Поля взяты из реальных доменных/EF-сущностей эталона. «Горячее» состояние (очередь,
presence, состояние активного боя) живёт в **Redis**, не в Postgres — отмечено в конце.

## players (schema: `players`)

```mermaid
erDiagram
  CHARACTERS {
    guid Id PK
    guid IdentityId "Keycloak subject"
    int Strength
    int Agility
    int Intuition
    int Vitality
    int UnspentPoints
    int Revision "optimistic concurrency"
    int OnboardingState "Draft|Named|Ready"
    string AvatarId
    long TotalXp
    int Level
    int LevelingVersion
    int Wins
    int Losses
    timestamptz Created
    timestamptz Updated
  }
  INBOX_MESSAGES {
    guid MessageId PK "dedup ключ"
    timestamptz ProcessedAt
  }
  CHARACTERS ||..|| INBOX_MESSAGES : "idempotent BattleCompleted handling"
```
> Плюс таблицы **Outbox** (MassTransit, миграция `AddOutboxEntities`) — транзакционная публикация.

## matchmaking (schema: `matchmaking`)

```mermaid
erDiagram
  MATCHES {
    guid MatchId PK
    guid BattleId
    guid PlayerAId
    guid PlayerBId
    string Variant
    int State "Queued|BattleCreateRequested|BattleCreated|Completed|TimedOut|Cancelled"
    timestamptz CreatedAtUtc
    timestamptz UpdatedAtUtc
  }
  PLAYER_COMBAT_PROFILES {
    guid IdentityId PK "проекция из PlayerCombatProfileChanged"
  }
```
> Очередь подбора, presence и lease-lock — **в Redis (DB1)**, не в этой схеме.

## chat (schema: `chat`)

```mermaid
erDiagram
  CONVERSATIONS ||--o{ MESSAGES : contains
  CONVERSATIONS {
    guid Id PK
    int Type "Global|Direct"
    guid ParticipantAIdentityId "null для Global"
    guid ParticipantBIdentityId "sorted-pair invariant"
    timestamptz CreatedAt
    timestamptz LastMessageAt
  }
  MESSAGES {
    guid Id PK "GUID v7 (time-ordered)"
    guid ConversationId FK
    guid SenderIdentityId
    string SenderDisplayName
    string Content
    timestamptz SentAt
  }
```
> Presence и rate-limit — **в Redis**. Ретенция старых сообщений — `MessageRetentionWorker`.

## battle (schema: `battle`)

```mermaid
erDiagram
  BATTLES ||--o{ BATTLE_TURNS : has
  BATTLES {
    guid BattleId PK
    guid MatchId
    guid PlayerAId
    guid PlayerBId
    string State
    timestamptz CreatedAt
    timestamptz EndedAt
    string EndReason "Normal|DoubleForfeit|Timeout|Cancelled|AdminForced|SystemError"
    guid WinnerPlayerId
    string PlayerAName
    string PlayerBName
    int PlayerAMaxHp
    int PlayerBMaxHp
  }
  BATTLE_TURNS {
    guid BattleId FK
    int TurnIndex PK
    string AtoBAttackZone
    string AtoBDefenderBlockPrimary
    string AtoBDefenderBlockSecondary
    bool AtoBWasBlocked
    bool AtoBWasCrit
    string AtoBOutcome
    int AtoBDamage
    string BtoAAttackZone
    bool BtoAWasBlocked
    bool BtoAWasCrit
    string BtoAOutcome
    int BtoADamage
    int PlayerAHpAfter
    int PlayerBHpAfter
    timestamptz ResolvedAt
  }
```
> Postgres хранит **историю завершённых боёв** (для feed/recovery). **Авторитетное состояние
> активного боя — в Redis (DB0)** (`RedisBattleStateStore` + Lua), оттуда же `BattleRecoveryWorker`
> восстанавливает бои после рестарта.

## Что в Redis, а что в Postgres

- **Redis (эфемерное, low-latency, atomic):** очередь подбора и presence (MM, DB1),
  lease-lock, состояние активного боя + ходы в полёте (Battle, DB0), rate-limit и presence чата,
  кэш профилей.
- **Postgres (устойчивое, реляционное):** профили/прогрессия игроков, история матчей,
  история завершённых боёв и ходов, сообщения чата, Outbox/Inbox.
- Почему так: для боя важны латентность и атомарные операции (Lua), а presence/rate-limit
  естественно живут с TTL — это плохо ложится в реляционную модель.

> Поля `players`/`matchmaking` отчасти усечены — для полного списка колонок сверяться с
> `*DbContext` + EF-миграциями соответствующего сервиса.
