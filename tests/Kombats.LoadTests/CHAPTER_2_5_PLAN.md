# Chapter 2.5 Plan — Redis hygiene + Observability hardening

## 0. Header

- **Chapter type:** Hardening pass between Chapter 3 (multi-replica proof) and Chapter 4 (capacity test).
- **Scope budget:** 3 deliverables, 1 measurement run, ~2 days work. Deliberately smaller than Chapter 3.
- **Branch:** `feat/redis-ttl-hardening` off `development` @ `3d30c71` (Chapter 3 squash merge HEAD).
- **Merge target:** `development`, squash merge at chapter close (same as Chapter 3).
- **Author role:** architect/mentor. Code agent does the implementation. Architect commits manually after review.
- **Pre-conditions confirmed (Session 1 recon):**
  - `StateTtlAfterEnd` still unwired (2 definition sites, 0 call sites in `src/Kombats.*`).
  - `KombatsObservabilityExtensions.cs` still silently skips OTLP exporter attachment at lines 61 and 77 when endpoint is empty.
  - `BattleRecoveryWorker` scope unchanged since Run 0 (Postgres-only, 30s/600s config still in place).

## 1. Why this chapter exists

Chapter 3 closed with two known issues that were deliberately deferred:

1. **TTL wiring.** `BattleRedisOptions.StateTtlAfterEnd` is defined (`BattleRedisOptions.cs:20`) and configured (`appsettings.json:42`) but never read by any code. Redis `battle:state:*` keys accumulate without TTL. At ~1000 battles/day production rate, this leaks ~365k keys/year uncapped. The current measurement discipline (`docker compose down -v` between runs) is a dev workaround for this production bug, not a production-style operation.

2. **OTLP silent skip.** `KombatsObservabilityExtensions.cs:61,77` silently skip attaching the OTLP exporter when `OpenTelemetry:OtlpEndpoint` is empty. This silent behavior was the root cause of broken observability through all of Chapters 1–3, discovered only in Run 4 setup phase (RUN_4_SETUP_LOG.md §11). The original design intent (line 14 comment) was "optional exporter for deploys without a collector," but the absence of a startup warning meant the misconfiguration was invisible.

Plus one operational deliverable:

3. **Compose chain documentation.** The Chapter 3 D6 mistake (forgetting to include `observability/docker-compose.observability.override.yml` in the up-chain) was an *operational* error, not a code error. Documenting the canonical compose chain in `README.md` prevents recurrence.

Chapter 2.5 closes all three so Chapter 4 (sustained capacity test at production concurrency) can run without these confounds.

## 2. Pre-flight sanity checks

Before starting Phase 1 (implementation), confirm what *must remain unchanged* after this chapter:

- Run 0 baseline numbers (`RUN_0_BASELINE.md` §3) must be reproducible after Chapter 2.5 ends. TTL wiring affects key lifetime, not request latency or throughput. Any p50/p95 regression on `queue_wait_ms`, `total_ms`, or pairing throughput vs Run 0 is a red flag.
- `BattleRecoveryWorker` boot line must still appear unchanged: `BattleRecoveryWorker started. ScanInterval=30000ms, StaleBattleThreshold=600s` — confirms recovery worker is not touched.
- `ActionTtl` behavior must remain unchanged. Action keys (`battle:action:*`, `battle:turn:*`) get 12h TTL via existing Lua-script `EXPIRE` (`RedisScripts.cs:307`). This pathway is not touched.
- All 5 services (Battle, Chat, Matchmaking, BFF, Players) must still start and pass smoke checks after D2 change. WARN log is additive — no exporter behavior changes.
- Existing `docker compose up` flows must keep working — D3 is documentation-only, no compose file edits.

## 3. D1 — TTL wiring for `battle:state:*` keys

### 3.1 Scope

Wire `BattleRedisOptions.StateTtlAfterEnd` through `RedisBattleStateStore.EndBattleAndMarkResolvedAsync` so that battles transitioning to `Phase = Ended` get a TTL applied to their `battle:state:{id}` Redis key.

### 3.2 Architectural decision (already settled — do not re-litigate)

**Approach: C# `KeyExpireAsync` after Lua script returns `EndedNow`, not Lua-script extension.**

Rationale (full version goes into `CHAPTER_2_5_REPORT.md`):

- Ended battles are terminal state — no reader depends on TTL being present the instant SET completes. TTL here is housekeeping (Redis-side cleanup), not consistency.
- The brief non-atomic window between Lua SET and C# `KeyExpireAsync` (< 1 RTT) is acceptable because no consumer reads ended battles expecting a TTL to be set.
- Lua-script extension would require a sentinel value for nullable `TimeSpan` (Lua doesn't understand .NET `null`). This adds a conditional inside a hot path (`EndBattleAndMarkResolvedScript` runs on every battle end).
- C# wrapper change is ~3 LOC. Lua extension is ~3 LOC Lua + ~5 LOC C# + sentinel handling. Lower-risk shim wins under "minimal surgical fix" discipline.

This decision is recorded in three places: (1) this plan §3.2, (2) inline comment in code (§3.4), (3) `CHAPTER_2_5_REPORT.md` architectural decision section (§3.6).

### 3.3 Implementation target

**File:** `src/Kombats.Battle/Kombats.Battle.Infrastructure/State/Redis/RedisBattleStateStore.cs`

**Method:** `EndBattleAndMarkResolvedAsync` (line 219)

**Insertion point:** after the Lua script returns `EndBattleCommitResult`, gated on `commitResult == EndedNow && _options.StateTtlAfterEnd.HasValue`.

**Why `EndedNow` only (not `AlreadyEnded`):** the TTL should be set exactly once per battle-end transition. `AlreadyEnded` means we're seeing a re-call for an already-terminal battle — TTL was already applied (or intentionally not applied if config was null at the time). Re-applying TTL on `AlreadyEnded` is functionally harmless (idempotent) but semantically wrong — it would extend TTL on every retry, defeating the cleanup purpose.

### 3.4 Code shape (illustrative — agent decides exact form)

```csharp
// Inside EndBattleAndMarkResolvedAsync, after Lua script returns:
if (commitResult == EndBattleCommitResult.EndedNow && _options.StateTtlAfterEnd.HasValue)
{
    var db = _redis.GetDatabase();
    // State TTL applied as a separate call after Lua SET, not atomically inside the script.
    // Rationale: Ended battles are terminal — no reader depends on TTL being present
    // the instant SET completes. The brief non-atomic window (< 1 RTT) is acceptable
    // because TTL here is housekeeping (Redis-side cleanup), not consistency.
    // Atomic alternative would require extending EndBattleAndMarkResolvedScript with
    // a sentinel for nullable TTL — deferred as unnecessary complexity.
    // See CHAPTER_2_5_REPORT.md "Architectural decision — TTL via C# wrapper".
    await db.KeyExpireAsync(GetStateKey(battleId), _options.StateTtlAfterEnd.Value);
}
```

The comment block is **mandatory**, not optional — it's a load-bearing piece of D1. Without it, future-Artem (or future-agent) will read this code, see "non-atomic TTL after Lua SET", and propose to "fix" it by moving into Lua. The comment is the documented `// do not refactor` signal.

### 3.5 Config change

**File:** `src/Kombats.Battle/Kombats.Battle.Bootstrap/appsettings.json:42`

Change:
```json
"StateTtlAfterEnd": null
```
to:
```json
"StateTtlAfterEnd": "01:00:00"
```

This sets the production default to 1 hour. The handoff specifies 1h: "TTL короче не имеет смысла, длиннее накапливает state." This is the value that will run in Chapter 4 capacity test.

The Sustainable Run (§6) will *override* this to 60s via env var for measurement — the source-of-truth in `appsettings.json` stays at 1h.

### 3.6 Report section requirement

`CHAPTER_2_5_REPORT.md` must include section `## Architectural decision — TTL via C# wrapper, not Lua script` with this structure (copied from Run 0 §6 / CLEANUP_WORKER_DIAGNOSIS.md §4 style):

- **What I did:** wired `StateTtlAfterEnd` via C# `KeyExpireAsync` after Lua returns `EndedNow`
- **Alternatives considered:** (A) Lua-script extension with sentinel, (B) C# wrapper
- **Why B:** atomicity not needed for terminal state, smaller diff, doesn't touch hot path Lua
- **What I did not do:** extend `EndBattleAndMarkResolvedScript`. Reason: TTL is housekeeping not consistency.
- **Trigger to revisit:** if a future reader emerges that expects ended battles to have TTL set atomically with SET, switch to approach A.

This is portfolio material — it demonstrates judgment about *when atomicity matters* and *when minimum diff wins*.

## 4. D2 — OTLP defensive WARN log

### 4.1 Scope

Add a startup `Console.Error.WriteLine` WARN message in `KombatsObservabilityExtensions.AddKombatsObservability` when `OpenTelemetry:OtlpEndpoint` is empty or missing.

### 4.2 Architectural decision (already settled)

**Approach: `Console.Error.WriteLine` with `[WARN]` prefix, not `LoggerFactory.Create` or startup filter.**

Rationale:

- `AddKombatsObservability` runs during DI composition — before `BuildServiceProvider`. `ILogger<T>` is not available.
- `LoggerFactory.Create(...)` would work but adds ~5 LOC for a single startup warning. At this lifecycle stage (pre-Serilog setup), `LoggerFactory` would write to stdout/stderr anyway — functionally identical to `Console.Error.WriteLine` but with more ceremony.
- `Console.Error.WriteLine` is a recognized .NET startup pattern (used in `Microsoft.AspNetCore.*` source for pre-DI diagnostics).
- One WARN line for an operational concern — heavier mechanisms are overkill.

### 4.3 Implementation target

**File:** `src/Kombats.Common/Kombats.Observability/KombatsObservabilityExtensions.cs`

**Insertion point:** immediately after line 37 (`string? otlpEndpoint = config[OtlpEndpointKey];`), before either guard at line 61 (tracing) or line 77 (metrics) fires.

### 4.4 Code shape (illustrative)

```csharp
string? otlpEndpoint = config[OtlpEndpointKey];

if (string.IsNullOrEmpty(otlpEndpoint))
{
    Console.Error.WriteLine(
        $"[WARN] Kombats.Observability ({serviceName}): OpenTelemetry:OtlpEndpoint is empty or missing. " +
        $"OTLP exporters will NOT be attached — traces and metrics will be discarded. " +
        $"If this is intentional (deploys without a collector), ignore. " +
        $"If unintentional, ensure observability override file is in the compose chain.");
}
```

Message anatomy:
- `[WARN]` prefix — machine-greppable, conventional severity tag
- `Kombats.Observability ({serviceName})` — identifies origin without needing structured logging context
- States *what won't happen* (exporters not attached, data discarded) — actionable
- Acknowledges legitimate cases (collector-less deploys) — doesn't cry wolf
- Points at *the specific Chapter 3 D6 failure mode* (compose chain missing override file) — turns this WARN into a debugging breadcrumb

### 4.5 Sanity check for D2

After D2 is implemented, restart any service with empty `OpenTelemetry:OtlpEndpoint` and `grep` startup logs for `[WARN] Kombats.Observability`. Should appear exactly once per service.

Then restart with a populated endpoint — WARN must not appear. This is the *defensive logging* contract: noisy when misconfigured, silent when correct.

### 4.6 Inline comment required

Above the new `if (string.IsNullOrEmpty(...))` block, add:

```csharp
// Defensive logging: empty endpoint is a supported case (deploys without a collector),
// but historically this silent skip caused observability to be broken for 3 chapters
// before being noticed (RUN_4_SETUP_LOG.md §11). The WARN ensures the next misconfiguration
// surfaces at the first service startup, not after weeks of empty Grafana dashboards.
```

Same load-bearing role as D1 comment — protects against well-intentioned refactor.

## 5. D3 — Compose chain documentation

### 5.1 Scope

Add a "Running the stack" section to `README.md` documenting the canonical `docker compose -f ... up` chains for the three modes the project actually uses.

### 5.2 Why this is a deliverable, not "I'll just remember"

The Chapter 3 D6 mistake was operational: forgot to include `observability/docker-compose.observability.override.yml`. The result was 4 chapters of broken observability. Documentation is the durable fix — code-level defensive logging (D2) catches the failure after-the-fact, but README catches it *before* the wrong command is typed.

### 5.3 Implementation target

**File:** `README.md`

**Insertion point:** new section after the existing intro, before any environment-specific notes. README is currently 30 lines of disconnected snippets (per Session 1 recon); the new section becomes the structural anchor.

### 5.4 Content shape

The section should document exactly three chains, copied from compose file headers (which already contain the canonical invocations):

**Chain A — Single-replica baseline (Chapters 0–3, future Chapter 2.5 Sustainable Run):**
```bash
docker compose \
  -f docker-compose.yml \
  -f observability/docker-compose.observability.yml \
  -f observability/docker-compose.observability.override.yml \
  -f docker-compose.override.yml \
  up -d --build
```

**Chain B — Multi-replica (Chapter 3 Run 2/3, future Chapter 4):**
Add `-f docker-compose.multi-replica.yml` to Chain A.

**Chain C — IDE mode (infrastructure only, app services on host):**
```bash
docker compose -f docker-compose.local.yml up -d
```

Each chain entry should include a one-sentence "when to use this" prefix.

### 5.5 What NOT to document

- `docker-compose.override.yml` — auto-loaded by Compose, mentioning it once is enough, no need to explain auto-load semantics
- Compose file headers — they already describe themselves, README points at them
- Production deployment — out of scope, not part of Chapter 2.5

### 5.6 Sanity check for D3

After D3 is written, a fresh reader (or future-Artem after 6 months) should be able to start the right stack for the right purpose without reading the compose files. The check is "can someone start the correct stack for Chapter 4 capacity test by reading README alone?" — if no, D3 is incomplete.

## 6. Measurement — Sustainable Run

### 6.1 Why one run, not five

Chapter 3 ran 5 measurement runs because each tested a distinct hypothesis (Run 1 — backplane fix, Run 2 — multi-replica without backplane, Run 3 — multi-replica with backplane, Run 4 — observability verification, Run 5 — sustained). Chapter 2.5 has one hypothesis to test: *the system is sustainable across multiple runs without between-run teardown*. One measurement design covers it.

### 6.2 Methodology (Approach E from Session 1 architect decision)

**Two parts, single run sequence:**

**Part 1 — Production-config smoke (30 seconds):**
- Stack started with **production** `StateTtlAfterEnd = "01:00:00"` (from `appsettings.json`)
- Bring up one bot pair, let one battle complete
- `redis-cli TTL battle:state:{id}` — expect value in range [3500, 3600] seconds (allowing for round-trip latency between SET and EXPIRE)
- Also check: `redis-cli --scan --pattern 'battle:state:*' | xargs -I{} redis-cli TTL {}` — no key should return `-1` (no expiry)
- This validates *config binding* — the appsettings value reaches `RedisBattleStateStore._options.StateTtlAfterEnd` correctly

**Part 2 — Sustainable Run proper (15 minutes):**
- Restart Battle service with `Battle__Redis__StateTtlAfterEnd=00:01:00` env-var override (60 seconds TTL)
- Run sequence: 4× sequential 25-bot × 120s load runs, **no `docker compose down -v` between them**
- Between runs: `sleep 90` (allows TTL to fire on previous run's keys)
- Measure after each run: `redis-cli DBSIZE`, `redis-cli --scan --pattern 'battle:state:*' | wc -l`
- After all 4 runs: `docker compose down -v` (final teardown for next session)

### 6.3 Predicted curve (math model)

Run duration = 120s. Inter-run sleep = 90s. TTL = 60s.

Per-run battle count ≈ 1053 (from Run 0 baseline, `RUN_0_BASELINE.md` §3 — pairing throughput 9.08 matches/s × 116s active window).

Expected `battle:state:*` count over time, assuming TTL works:

| Time (s, cumulative) | Event | Expected count |
|---|---|---|
| 0 | Start run 1 | 0 |
| ~120 | End run 1 | ~1053 (just-finished battles, TTL set but not yet expired) |
| ~210 | After sleep 90 (TTL=60s elapsed since each Ended) | ~0 (all TTL'd out) |
| ~210 | Start run 2 | ~0 |
| ~330 | End run 2 | ~1053 |
| ~420 | After sleep 90 | ~0 |
| ... | (repeat) | ... |
| ~840 | End run 4 | ~1053 |
| ~930 | After sleep 90 | ~0 |

Plateau behavior: count oscillates between ~0 and ~1053. Does NOT monotonically grow.

### 6.4 Pass criteria

Sustainable Run **passes** if all four conditions hold:

1. `battle:state:*` count returns to ≤ ~50 between runs (allowing for in-flight battles that haven't ended yet at sample time). The exact threshold is "not monotonically growing."
2. Pairing throughput per run is within ±5% of Run 0 baseline (9.08 matches/s). No deterioration across runs.
3. `total_ms` p95 per run is within ±5% of Run 0 baseline (1593ms). No deterioration across runs.
4. Part 1 smoke shows TTL ≈ 3600s on production config — confirms config binding works.

If any of 1–4 fails: **negative result, document honestly, investigate before closing chapter.**

### 6.5 Pass criteria for negative results

A negative result is acceptable data. Three plausible negative outcomes and what each means:

- **(N1) Count plateau but throughput degrades** — TTL works but something else accumulates (Postgres rows, RabbitMQ messages, etc.). Investigate via system metrics.
- **(N2) Count keeps growing** — TTL mechanism broken. Likely a code bug in D1 (key shape mismatch, wrong condition gate, etc.).
- **(N3) Part 1 smoke fails (TTL = -1 on production config)** — config binding broken. D1 implemented but production appsettings value not reaching the code. Investigate config section path.

Each gets a dedicated subsection in `RUN_5_RESULTS.md` if it occurs.

### 6.6 Operational discipline

- Stack must be brought up with the **full canonical chain** (Chain A from §5.4, including observability override file). No repeat of Chapter 3 D6.
- Before starting Part 1: confirm Grafana shows live telemetry. If empty — D6 failed again, fix compose chain before measuring.
- Part 2 env var override applied via `docker-compose.override.yml` extension, not by editing `appsettings.json`. Source of truth stays at 1h for production.

## 7. Phasing

### Phase A — Plan (Session 1, this document)

Status: this file is Phase A. Review checkpoint with architect before Phase B.

### Phase B — Implementation (1 agent prompt, ~1 hour)

Single agent prompt covering D1 + D2 + D3 + appsettings change. Branch is `feat/redis-ttl-hardening` off `development`.

After implementation:
- Architect reviews diff
- Architect runs unit tests locally
- Architect commits manually (not agent)

### Phase C — Sustainable Run setup (agent autonomous)

Agent does setup phase 1–9 (same as Chapter 3 Run pattern):
1. Verify branch / HEAD / clean tree
2. Teardown via canonical compose chain
3. Rebuild via canonical compose chain (Chain A)
4. Migrations
5. Keycloak bootstrap
6. Seed users
7. Smoke checks (HTTP + Redis + Grafana confirms telemetry alive)
8. **TTL verification** (Part 1 of §6.2 — single-battle production-config smoke)
9. Setup log → `RUN_5_SETUP_LOG.md`

### Phase D — STOP for architect manual measurement

Architect runs the 4× sequential load (Part 2 of §6.2). Agent does NOT trigger NBomber. Architect watches Grafana, takes screenshots for STORY material, captures `redis-cli` snapshots between runs.

### Phase E — Analysis (agent after architect)

- Aggregate phases for all 4 runs
- Compare each run vs Run 0 baseline AND vs run N-1 (the key Sustainable Run check)
- TTL behavior verification (count curve)
- Sanity checks (recovery worker untouched, ActionTtl untouched)
- Write `RUN_5_RESULTS.md`
- STOP — no commit

### Phase F — Chapter close (architect)

- Architect writes `CHAPTER_2_5_REPORT.md` (~150 lines, format mirrors `CHAPTER_3_REPORT.md`)
- PR `feat/redis-ttl-hardening` → `development`, squash merge
- STORY Part 3 written separately (out of Chapter 2.5 technical scope)

## 8. Things I will not do

- **Will not modify `BattleRecoveryWorker`.** Its scope (Postgres-only recovery, 600s threshold) is correct as-is. Architectural decision in handoff.
- **Will not extend `EndBattleAndMarkResolvedScript` (Lua).** Per §3.2 — atomicity not needed, hot path stays untouched.
- **Will not lower the worker's 600s threshold for tests.** It's reasonable in production; lowering for tests would create a different code path in prod vs test.
- **Will not add active scan worker for Redis cleanup.** Approach B (active scan) was deliberately rejected in favor of Approach A (TTL via store) in handoff.
- **Will not create `OPERATIONS.md`.** D3 lives in `README.md` per handoff (single entry point, not yet-another-file).
- **Will not address SERIALIZABLE conflicts on `matchmaking.player_combat_profiles`** (Run 0 §6 bullet 2). Lower priority than TTL+OTLP, not on Chapter 2.5 path. Deferred.
- **Will not address BFF queue-polling p95 ≈ 1.80s** (Run 0 §6 bullet 3). Push-based notifications = Chapter 7. Deferred.
- **Will not change `ActionTtl` default (12h).** Working as intended, not in scope.
- **Will not push to GitHub Packages.** Per handoff constraint.

## 9. Open questions for architect

These should be resolved before Phase B starts:

1. **`appsettings.Production.json` (if it exists) — does it override `Battle:Redis:StateTtlAfterEnd`?** If yes, also update. If no, base appsettings change is sufficient. (Recon didn't enumerate environment-specific appsettings — check during Phase B.)
2. **Branch name `feat/redis-ttl-hardening`** — acceptable, or prefer `feat/chapter-2.5-hardening` or other?
3. **WARN log prefix `[WARN]` vs `WARN:` vs `[warn]`** — any house style preference? Default to `[WARN]` if no preference.

## 10. Roadmap — what Chapter 2.5 enables

Chapter 2.5 is the direct enabler for Chapter 4 (capacity test at production concurrency):

- Capacity test = sustained load over hours, not minutes. **Impossible without TTL fix** — Redis would accumulate uncapped.
- Capacity test = trust observability data. **Impossible without D2** — silent OTLP skip would mask data collection failures.
- Capacity test = consistent compose chain across multiple sessions. **Impossible without D3** — every team member could accidentally use a different stack.

After Chapter 2.5 closes, Chapter 4 has all preconditions met.

## 11. Plain-language summary

Я (Артём) в Главе 3 доказал что multi-replica работает. Но обнаружил два долга:

1. **Полгода назад я добавил настройку `StateTtlAfterEnd` для очистки Redis после завершения battle, но забыл её подключить к коду.** Ключи накапливались бесконечно. Unit-тесты этого не видели — нашёл только под нагрузочным тестированием. В этой главе подключаю.

2. **Год назад я сделал экспорт телеметрии опциональным — если endpoint не задан, не экспортируем.** Но не добавил предупреждение когда endpoint отсутствует. Это привело к тому что 3 главы я думал что observability работает, а он молчал. Добавляю startup WARN — теперь следующая такая ошибка увидится сразу.

3. **Документирую как правильно поднимать стек** — чтобы я (или будущий разработчик) не повторил оперативную ошибку Главы 3 (забыл подключить файл наблюдаемости).

Плюс делаю новый тип измерения — **Sustainable Run**. Это 4 нагрузочных теста подряд без очистки между ними. До этой главы между тестами я делал `docker compose down -v` — workaround для бага накопления Redis ключей. После TTL fix этот workaround больше не нужен. Sustainable Run доказывает что система **может жить во времени**, а не только проходить отдельный тест "из чистого состояния".

Это новая capability перед Главой 4 (capacity test), где система должна выдерживать нагрузку часами.
