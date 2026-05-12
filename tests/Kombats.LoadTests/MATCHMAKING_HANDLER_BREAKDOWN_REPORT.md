# Matchmaking Handler Breakdown — Report

**Run:** 25 pairs × 120 s, NBomber session `2026-05-11_13-20-42_...`
**Iteration log:** `tests/Kombats.LoadTests/iteration-logs/iterations-2026-05-11--18-20-42.jsonl`
**NBomber p50 (ok):** 21 200.9 ms — same shape as the previous baseline (21 119 ms).
**Pair-forming traces collected from Jaeger:** **67** parent `matchmaking.pair.tick` spans that actually formed a pair.

## Headline finding (the surprise)

**The handler is not slow.** Parent `matchmaking.pair.tick` p50 = **5.055 ms**. The 1.25 s/pair derivation in `MATCHMAKING_PAIRING_NOTES.md` was wrong about *where* the time is. The per-pair work inside `HandleAsync` accounts for 0.4 % of the per-pair cycle time, not 100 %. The remaining ~99 % lives **outside** `HandleAsync`, in the worker's between-tick path (lease management, DI scope, `Task.Delay`, async scheduler).

This is a valid portfolio chapter on its own: *"the model said handler I/O was the bottleneck, the instrumentation said no — and now we know where to look next."*

## 1. Per-span breakdown (67 pair-forming traces)

| Span | count | p50 ms | p95 ms | p99 ms | max ms |
|---|---:|---:|---:|---:|---:|
| `matchmaking.pair.tick` *(parent)* | 67 | **5.055** | 10.173 | 132.303 | 132.303 |
| `matchmaking.pair.try-pop` | 67 | 0.415 | 0.939 | 2.112 | 2.112 |
| `matchmaking.pair.profile-lookup-1` | 67 | 0.854 | 2.065 | 2.220 | 2.220 |
| `matchmaking.pair.profile-lookup-2` | 67 | 0.539 | 1.058 | 1.497 | 1.497 |
| `matchmaking.pair.publish-create-battle` | 67 | 0.327 | 0.760 | 55.425 | 55.425 |
| `matchmaking.pair.save-changes` | 67 | 2.037 | 4.236 | 42.461 | 42.461 |
| `matchmaking.pair.set-matched-1` | 67 | 0.308 | 0.624 | 0.711 | 0.711 |
| `matchmaking.pair.set-matched-2` | 67 | 0.268 | 0.539 | 0.828 | 0.828 |

All-ticks parent (includes the 42 no-op ticks where the queue was empty):
n=109, p50 = 3.598 ms, p95 = 9.815 ms.

### Statistical caveat

n = 67 pair-forming samples in ~90 s of steady-state. **p50 is trustworthy.
p95 is marginal — one or two outliers move it noticeably. p99 reports a single
sample**, which is 132 ms — almost certainly a GC pause or a transient
Postgres blip and **not a structural signal**. No strong claims will be made
off the p99 / max columns; they're shown for completeness.

## 2. Sanity check #1 — sum of child p50 vs parent p50

Sum: `0.415 + 0.854 + 0.539 + 0.327 + 2.037 + 0.308 + 0.268 = 4.748 ms`.
Parent: `5.055 ms`. Gap = **0.31 ms** ≈ object allocation + `Guid.NewGuid()`
calls + logger formatting (line 94-96). Negligible. **Spans cover the handler
fully.** ✅

## 3. Sanity check #2 — parent p50 vs the 1.25 s figure from `MATCHMAKING_PAIRING_NOTES.md`

Predicted: 1.25 s.
Measured: **5 ms.**
Gap: **~250×.** ❌

The previous derivation went: *throughput = 0.74 pairs/s ⇒ cycle = 1.35 s
⇒ minus 100 ms sleep ⇒ ~1.25 s of handler work*. That last step assumed
the cycle time was all consumed by `HandleAsync` plus the configured
sleep. That assumption was wrong. **The cycle time is real, but most of
it lives outside the handler.**

The hypothesis in `MATCHMAKING_PAIRING_NOTES.md` ("Postgres + outbox flush
under transaction contention") is therefore not supported by this run. The
matching is fast, the commit is fast (2 ms), the outbox publish is fast
(0.3 ms). The bottleneck is elsewhere.

## 4. Sanity check #3 — wait model

Original formula: `parent_p50 × 12.5 + 100 ms × 12.5 + 250 ms ≈ queue_wait_ms p50`.
With `parent_p50 = 5 ms`: `5 × 12.5 + 100 × 12.5 + 250 = 1562 ms`. But measured
queue_wait_ms p50 = 19 800 ms. **The formula is broken** by the same
assumption that broke the 1.25 s derivation.

Corrected formula, using observed cycle time directly:

```
observed_cycle = 191 s window / 109 ticks  = 1.755 s/tick
predicted wait = 12.5 × observed_cycle + 250 ms poll lag
              = 12.5 × 1.755 + 0.25 = 22.2 s
```

Measured: **19.8 s.** Match within ~12 %. ✅

So the queue-wait model is sound; the worker is genuinely producing one
pair every ~1.75 s. We just had the wrong location for *why*.

## 5. Top-1 contributor — and the bigger contributor (outside the handler)

**Inside the handler:** `matchmaking.pair.save-changes` at p50 2.04 ms is
the biggest child span (40 % of parent budget). The Postgres auto-
instrumentation (see §6) confirms why: it's a single batched INSERT of the
match row + outbox state + outbox message rows. Already efficient — one
Postgres round-trip.

**The real top contributor — outside the handler:**

```
configured tick rate: 1 / 0.105 s = 9.5 ticks/s
measured tick rate:   1 / 1.755 s = 0.57 ticks/s
gap:                  ~17×
```

Per pair-cycle:
- HandleAsync wall clock: **5 ms** (instrumented)
- `await Task.Delay(100 ms)`: 100 ms (configured)
- **Unaccounted: ~1 650 ms.**

That 1.65 s lives between the parent-span close (`HandleAsync` return) and
the next parent-span open (next `HandleAsync` start). The candidates, in
order of suspicion:

| # | Candidate | Why suspect |
|---|---|---|
| 1 | `MatchmakingLeaseService.TryExecuteUnderLeaseAsync` — Redis lease acquire / release / renewal-task scheduling | Lease TTL = 5 s, renewal at TTL/3 = **1.667 s**. Measured cycle = **1.755 s**. The numbers are suspiciously close. |
| 2 | `_scopeFactory.CreateScope()` and `GetRequiredService<...>` — DI scope construction per tick | EF Core DbContext registration in DI is non-trivial, especially with MassTransit interceptors. |
| 3 | The `await Task.Delay` resumption and OS-level scheduler under load | Less likely at this concurrency, but non-zero. |

**Candidate 1 is the leading hypothesis.** A periodic renewal task is
created per lease, and if the worker is blocked on its scheduling somehow
(or if the lease release synchronously waits on the renewal-task
completion via `Task.Delay` cancellation), each tick could end up
anchored to the renewal interval.

## 6. Auto-instrumentation bonus content — the "free" insight

The plan flagged that `AddSource("Npgsql")` and `AddSource("MassTransit")`
are already registered in `KombatsObservabilityExtensions.cs`. That paid off:
**we got 30+ additional spans in every pair-forming trace without writing
any code**, including the SQL text inside `save-changes`.

Sample pair-forming trace (`trace_id 87f22ba5c836d27028356c33da328ddd`):

**Inside `matchmaking.pair.save-changes` (parent 3.14 ms):**

One Postgres span at 1.535 ms with `db.query.text`:

```sql
INSERT INTO matchmaking.matches
  (match_id, battle_id, created_at_utc, player_a_id, player_b_id, state, updated_at_utc, variant)
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7);

INSERT INTO matchmaking.outbox_state
  (outbox_id, created, delivered, last_sequence_number, lock_id)
VALUES (@p8, @p9, @p10, @p11, @p12)
RETURNING row_version;

INSERT INTO matchmaking.outbox_message
  (body, content_type, conversation_id, correlation_id, destination_address, ...)
VALUES (@p13, @p14, ..., @p32)
RETURNING sequence_number;
```

— EF Core has batched all three INSERTs into a single round-trip. The
transactional outbox pattern is working exactly as designed: the match row
and the MassTransit outbox row are committed atomically in one statement.
**This is healthy.** No contention, no slow query, no N+1 — just one fast
multi-statement command. The 1.5 ms cost is essentially the network +
fsync latency for the commit, not anything we could optimize from
application code.

**Inside `matchmaking.pair.publish-create-battle` (parent 0.69 ms):**

The MassTransit `outbox send` span at 0.47 ms. This is a *staging* operation
that writes the outbox row in-memory; the actual durable write happens
inside `save-changes`. So the publish call is genuinely tiny — it's the
commit that's measurable, and even the commit is fast.

These two findings together say something concrete: **inside the pair-forming
code path, every operation is fast, every Postgres write is batched, the
outbox pattern is doing exactly what it should**. There's nothing on this
code path to fix.

## 7. Hypothesis for the missing 1.65 s

Plain language:

> The worker runs an infinite loop. Each iteration: acquire a Redis lease,
> run the handler under the lease, release the lease, sleep 100 ms,
> repeat. We instrumented the handler. The handler is fast (5 ms). The
> sleep is 100 ms. So a pair-forming cycle should take ~105 ms — but
> we measure 1 750 ms.
>
> The leftover 1 650 ms has to be in: lease acquire, lease release, the
> background lease-renewal task that fires every 1.667 s, or the DI
> scope construction per iteration. The lease-renewal interval (1.667 s)
> and the observed cycle time (1.755 s) match within 5 %, so the leading
> guess is that the lease-management code is the bottleneck — possibly
> because the renewal task is being awaited synchronously somewhere it
> shouldn't be, or because lease release waits for a renewal to complete.
>
> This wasn't visible from the original `MATCHMAKING_PAIRING_NOTES.md`
> source walk because the lease code (`MatchmakingLeaseService` +
> `RedisLeaseLock`) wasn't on the per-pair work list — it was treated as
> infrastructure. Turns out infrastructure is the per-pair work.

## 8. Proposed next steps (not implemented)

Three candidate next moves, sorted by expected information-per-effort.

### Lever A — Instrument the worker loop itself (~20 min, low risk)

Same shape as this PR but in `MatchmakingPairingWorker.cs`. Add spans
around:
- `_scopeFactory.CreateScope()` + `GetRequiredService<...>` (DI scope)
- `_leaseService.TryExecuteUnderLeaseAsync` (whole call)
- `_leaseLock.TryAcquireLockAsync` (just the acquire) — requires changes to `MatchmakingLeaseService` or making its internals visible
- `await Task.Delay` (sanity check it really is 100 ms)

This is the same playbook that just worked. It should resolve the 1.65 s
mystery in one run. Recommended **next** move regardless of whether we
ship a fix.

### Lever B — Read the lease service / lease lock source (~30 min, no risk)

Specifically look for: where the renewal task is started, whether `TryExecuteUnderLeaseAsync`
awaits anything that could be coupled to the renewal cadence, whether
lease release synchronously waits for the renewal task to finish. Files
named in `MATCHMAKING_PAIRING_NOTES.md` §4:
- `src/Kombats.Matchmaking/Kombats.Matchmaking.Infrastructure/Redis/MatchmakingLeaseService.cs:33-96`
- `src/Kombats.Matchmaking/Kombats.Matchmaking.Infrastructure/Redis/RedisLeaseLock.cs:34-69`

If the source reading makes the bug obvious (lease release `await`-ing
the renewal cancellation, for example), we can fix it directly without
needing Lever A first.

### Lever C — Bypass the lease for the local single-replica case (~1 hr, moderate risk)

A short-term win for portfolio storytelling: in single-replica mode, the
lease is doing nothing useful but is the suspected bottleneck. A config
flag `Matchmaking:Worker:LeaseEnabled` defaulting to `true` could be
turned off for development/load-test runs, bypassing the lease entirely.
If that recovers the expected 9.5 ticks/s, we have **proof** that the
lease management is responsible — and we have a clean before/after
number for the portfolio story.

**Recommendation:** **Lever B first** (source reading is cheap), then
**Lever A** (instrumentation) if the source isn't conclusive. **Lever C**
becomes the fix story once we know what we're fixing — bypassing without
knowing why feels like fixing the symptom.

## 9. Out of scope

- Implementing any fix (Lever A / B / C).
- Multi-replica / backplane work.
- Changes to anything outside `ExecuteMatchmakingTickHandler.cs` and the
  Phase-2 docs.

## 10. Files modified in this commit

| | Path | Change |
|---|---|---|
| MOD | `src/Kombats.Matchmaking/.../ExecuteMatchmakingTickHandler.cs` | +25 lines, 8 spans added, no logic changes |
| NEW | `tests/Kombats.LoadTests/MATCHMAKING_HANDLER_BREAKDOWN_PLAN.md` | the approved plan |
| NEW | `tests/Kombats.LoadTests/MATCHMAKING_HANDLER_BREAKDOWN_REPORT.md` | this file |

Branch: `feat/matchmaking-handler-instrumentation` (off `feat/iteration-phase-breakdown`).
First touch of `src/Kombats.Matchmaking/`.
