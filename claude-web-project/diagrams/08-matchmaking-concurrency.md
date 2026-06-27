# Kombats — Matchmaking concurrency (lease-lock)

Как несколько реплик Matchmaking подбирают пары, не создавая дублей. Изюминка проекта:
`RedisLeaseLock` + `MatchmakingLeaseService` + `MatchmakingPairingWorker`.

```mermaid
sequenceDiagram
  autonumber
  participant W1 as PairingWorker @replica-1
  participant W2 as PairingWorker @replica-2
  participant R as Redis (lease + queue, DB1)
  participant Q as RabbitMQ

  par Оба воркера тикают одновременно
    W1->>R: acquire lease (SET key NX PX=ttl)
  and
    W2->>R: acquire lease (SET key NX PX=ttl)
  end
  R-->>W1: OK (lease получен)
  R-->>W2: nil (занято) → idle backoff

  Note over W1,R: только держатель lease подбирает
  W1->>R: прочитать очередь, выбрать пару (Lua, atomic)
  R-->>W1: PlayerA, PlayerB (разные игроки)
  W1->>R: пометить обоих matched + убрать из очереди
  W1-)Q: CreateBattle (command)
  W1->>R: release lease

  Note over W2: следующий тик — lease свободен,<br/>но эти игроки уже не в очереди
```

**Какие race conditions это закрывает**
- **Двойной матч одной пары** — без lease две реплики могли бы одновременно вытащить тех же
  игроков и создать два боя. Lease гарантирует одного подборщика в момент тика.
- **Игрок в двух матчах** — атомарная Lua-операция «выбрать пару + пометить matched + убрать
  из очереди» исключает гонку чтения-записи очереди.
- **Залипший lease** — `PX=ttl` (lease с истечением): если держатель упал, lock сам отпустится.
- **Холостое сжигание CPU** — `MatchmakingPairingWorker` использует bounded loop + idle backoff:
  не крутится вхолостую, когда очередь пуста.

> Точная реализация Lua и параметры lease — в `RedisScripts.cs`, `RedisLeaseLock.cs`,
> `MatchmakingLeaseService.cs`, `MatchmakingPairingWorker.cs`.
