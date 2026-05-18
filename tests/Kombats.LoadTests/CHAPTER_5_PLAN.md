# Chapter 5 — Lift the Matchmaking Pairing-Rate Ceiling — Plan

## 1. Goal & scope

**Primary goal:** lift the matchmaking pairing-rate ceiling found in Chapter 4
(~9.5 pairs/s, ~19 RPS — a code-level cap, not a capacity cap), and measure
the new ceiling and the next wall.

**Concrete deliverable for the portfolio:**
> "Chapter 4 found pairing pinned at ~9.5 pairs/s by a fixed 100 ms `Task.Delay`
> plus one-pair-per-tick handler. Chapter 5 replaces both with a bounded
> inner pairing loop and a conditional idle backoff. New ceiling: N pairs/s.
> Next wall: Y, named with file:line / metric."

**In scope:** changes to `src/Kombats.Matchmaking/*` only —
`MatchmakingPairingWorker.cs`, `ExecuteMatchmakingTickHandler.cs`,
`MatchmakingWorkerOptions.cs`, plus tests in the matchmaking test project.

**Out of scope:**
- Any other service (`src/Kombats.Battle`, `Bff`, `Players`, `Chat`).
- The secondary walls — 5 sequential awaits per pair, SERIALIZABLE conflicts
  on `player_combat_profiles`, outbox/MassTransit per-pair cost. Chapter 6+.
- Full-batch transactionality (see §6 — explicitly accepted limitation).
- NBomber harness — not touched.

## 2. The bottleneck (confirmed)

From Chapter 4 capacity scan + `CHAPTER_5_RECON.md`. Three reinforcing limiters:

1. **Unconditional `Task.Delay(TickDelayMs)`** at `MatchmakingPairingWorker.cs:72`
   — fires after every tick whether or not a pair was created. `TickDelayMs = 100`.
2. **One-pair-per-tick handler** — `ExecuteMatchmakingTickHandler.HandleAsync`
   pops exactly one pair (`ExecuteMatchmakingTickHandler.cs:52`), no inner loop.
3. **Single Redis lease** `mm:lease:matchmaking:default` — one active pairer.

Arithmetic: `1 / (100 ms + ~7 ms work) ≈ 9.35 pairs/s × 2 = 18.7 RPS`.
Confirmed on 25/50/100 simul, 0.8 % error on Stage 3 latency.

Chapter 5 targets limiters (1) and (2). Limiter (3) is left intact — it is
correctness machinery (see §3), not a tuning knob.

## 3. Correctness — what recon established (the load-bearing section)

`CHAPTER_5_RECON.md` settled four facts the fix depends on:

- **Q1 — pair atomicity is in the Lua script, not the lease.** `TryPopPairScript`
  does `LPOP … LPOP … SREM` in one Lua body; Redis runs scripts single-threaded.
  **A player GUID cannot be popped into two pairs** — true regardless of tick
  rate, regardless of how many workers run. The fix cannot break this because
  the fix does not touch the Lua script.
- **Q2 — the lease guarantees "one active pairer", not pair correctness.**
  Removing/losing the lease does not cause double-pairing (Q1 prevents that);
  it causes two replicas to run *post-pop* work (`PublishAsync`,
  `SaveChangesAsync`, `SetMatchedAsync`) concurrently on **disjoint** pairs.
- **Q3 — an *unbounded* inner loop opens new races.** Lease TTL is 5 s, renewed
  every ~1.67 s. A loop that drains a deep queue can outrun the TTL: the lease
  expires mid-batch, another replica acquires it, and two replicas run the
  handler concurrently for up to one renewal interval before cancellation fires.
- **Q4 — `Task.Delay` is the *only* pacing mechanism.** Neither the handler nor
  the lease service backs off on an empty queue. Remove the delay with no
  replacement → busy-loop hammering Redis lease acquire/release at RTT rates.

**How the fix preserves correctness:**

- Pair atomicity (Q1) — untouched. The Lua script is not modified.
- Lease "one active pairer" (Q2/Q3) — preserved by **bounding the inner loop**
  so one lease window stays well under the 5 s TTL. The loop exits on the
  *first* of: queue empty, `MaxPairsPerTick` reached, or soft deadline
  (`LockTtlMs / 2` ≈ 2.5 s) elapsed. The queue drains across several ticks,
  not one — but with no 100 ms gap between them.
- Cancellation (Q3) — every `await` inside the loop threads the linked
  cancellation token. A lost lease cancels the loop cleanly at the next await;
  it does not silently continue.
- Busy-loop (Q4) — prevented by the **conditional** idle backoff (§4 step 2).

## 4. The fix — three parts, all in `src/Kombats.Matchmaking/*`

**Part 1 — bounded inner pairing loop in the handler.**
`ExecuteMatchmakingTickHandler.HandleAsync` loops over `TryPopPairAsync` instead
of calling it once. Each iteration creates one match (existing per-pair logic
unchanged). The loop exits on the first of:
- `TryPopPairAsync` returns null (queue empty *or* one player left — recon
  confirmed both return null; the lone player is requeued to the tail by the
  Lua script's existing fairness branch),
- pairs created this tick reaches `MaxPairsPerTick`,
- elapsed time reaches the soft deadline.
Return value reports **how many pairs were created** this tick (not just a bool).

**Part 2 — conditional idle backoff in the worker.**
`MatchmakingPairingWorker` replaces the unconditional `Task.Delay` at line 72
with: if the tick created **zero pairs**, sleep `TickDelayMs`; if it created
≥1 pair, continue immediately to the next iteration. Zero pairs covers both
empty queue and lone-player-left — in both cases there is nothing to pair, so
backing off is correct.

**Part 3 — config + constants.**
- `MaxPairsPerTick` — new key in `MatchmakingWorkerOptions`, default ~64.
  Configurable: it will be tuned during the capacity scan (§5).
- Soft deadline — a code constant derived from the lease TTL
  (`LockTtlMs / 2` ≈ 2.5 s), **not** a config key. Keeping it tied to the TTL
  in code prevents it drifting independently of the lease lifetime.
- `TickDelayMs` — unchanged at 100 ms; now an idle-only backoff.

Nothing else changes. The Lua script, the lease service, `RedisMatchQueueStore`,
the per-pair handler body — all untouched.

## 5. Methodology — measure → fix → measure

Reuse Chapter 4 infrastructure entirely (Locust, capacity overlay, RUNBOOK,
methodology in `CHAPTER_4_PLAN.md` §3). Chapter 5 builds no new tooling.

- **Measure before** — reuse Run 6 Stage 1/2/3 (25/50/100 simul) as the
  baseline. Already on Mac, same platform. No re-measurement needed.
- **Fix** — Part 1–3 above, via code agent. Tests per §7.
- **Measure after (Run 7)** — same capacity scan on the changed code, on Mac
  (same-platform with the baseline — non-negotiable). Stop on first break.
  `MaxPairsPerTick` may be tuned between stages if it becomes the visible cap.
- **Compare** — predicted vs measured, new ceiling, new wall.

## 6. Prediction (predict-then-measure)

Current ceiling: `1 / (100 ms + ~7 ms) ≈ 9.5 pairs/s`.

After the fix the 100 ms gap between pairs is gone; only ~7 ms of per-pair
work remains. Arithmetic ceiling: `1 / 7 ms ≈ 140 pairs/s ≈ 280 RPS`.

**But** recon Q5 (in `RUN_6_RESULTS.md`) warns contention compresses this well
before 140. Honest predicted range: **new ceiling somewhere 30–140 pairs/s**,
limited by either the 5 sequential Redis/Postgres awaits per pair or — more
likely — SERIALIZABLE conflicts on `matchmaking.player_combat_profiles`, which
recon expects to reappear at 30–50 battles/s.

**Success = "ceiling lifted from 9.5 to N pairs/s, new wall is Y".** Not a
specific number. If N turns out to be 30 because a secondary wall is close,
that is a valid Chapter 5 result.

## 7. Tests (mandatory — this chapter touches business logic)

In the matchmaking test project:

- **Inner loop drains the queue.** Seed an even queue of M players; one tick
  produces M/2 matches (M/2 ≤ `MaxPairsPerTick`), all distinct, no GUID reused.
- **`MaxPairsPerTick` bounds the loop.** Seed a queue larger than the cap; one
  tick produces exactly `MaxPairsPerTick` matches, the rest stay queued.
- **Odd queue / lone player.** Seed an odd queue; the last player is requeued,
  not lost, not self-paired; the tick reports its pair count correctly.
- **Idle backoff.** Empty queue → tick creates zero pairs → worker takes the
  `TickDelayMs` sleep path. Lone-player queue → same (zero pairs).
- **Cancellation mid-loop.** A cancelled token partway through the loop stops
  it at the next await without throwing past the handler.

## 8. Risk register

| ID | Risk | Mitigation |
|----|------|-----------|
| R1 | Unbounded loop outruns lease TTL (recon Q3) | Loop bounded by `MaxPairsPerTick` + soft deadline `LockTtlMs/2`; window stays ≪ 5 s TTL |
| R2 | Busy-loop on empty queue if delay removed naively (recon Q4) | Idle backoff is *conditional* — sleep `TickDelayMs` whenever zero pairs created |
| R3 | Fix does not lift ceiling as far as predicted | Valid outcome (§6). Report the real N and the wall; do not chase 140 |
| R4 | Scope creep into secondary walls | §1 out-of-scope is explicit. One bottleneck per chapter (Chapter 2.5 lesson) |
| R5 | Partial-batch commit on mid-loop lease loss | Accepted, not fixed — see §9. Bounded window makes it low-probability |

## 9. Things this chapter will NOT do

- **Full-batch transactionality.** Recon Q3 #3: with an inner loop, pairs
  committed earlier in the loop are persisted before a mid-batch lease loss;
  they cannot be rolled back. The bounded window (≈2.5 s vs 5 s TTL) makes
  mid-loop lease loss low-probability, but the partial-commit *property*
  remains. Making the whole batch one transaction is a larger refactor —
  Chapter 6+, not here.
- **Secondary walls** — 5 sequential awaits, SERIALIZABLE conflicts, outbox
  per-pair cost. Named in `RUN_6_RESULTS.md`; next chapters.
- **Matchmaking horizontal scaling** — hardcoded variant + single lease.
  Follows this fix, not part of it.
- **Re-measuring the baseline** — Run 6 Stage 1/2/3 is reused as "before".

## 10. Phases

- **A — Recon + plan** (this document). Done.
- **B — Implement.** Code agent: Part 1–3 in `src/Kombats.Matchmaking/*` + §7
  tests. Review checkpoint before merge.
- **C — Measure after (Run 7).** Capacity scan on the changed code, on Mac,
  reusing Chapter 4 Locust + methodology. → `RUN_7_RESULTS.md`.
- **D — Analysis.** Before/after table, new ceiling, new wall.
- **E — Close.** `CHAPTER_5_REPORT.md` (≤80 lines), PR, squash merge into
  `development`.

## 11. Ceremony budget

- This plan: ≤ 200 lines.
- `RUN_7_RESULTS.md`: ≤ 150 lines.
- `CHAPTER_5_REPORT.md`: ≤ 80 lines.
- One code agent for Phase B. Architect drives Phase C scan.
- If any deliverable exceeds budget — stop, ask "necessary or overkill?"
