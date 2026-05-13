# Run 0 Baseline Drift Investigation

**Run analyzed:** `iterations-2026-05-12--17-26-49.jsonl` (629 lines, 17:26:49 → 17:27:52 +05:00)
**Compared against:** `iterations-2026-05-11--18-38-56.jsonl` (2127 lines, the Chapter 2 handoff baseline)
**Repo state:** `HEAD = 2842b7f` (the lease-renewal cancellation fix)

## 1. Summary

The load harness has **not** changed since the Chapter 2 baseline was recorded, and `src/Kombats.*` has **not** changed either — `HEAD` and the lease fix commit (`cb1aa22`, merged as `2842b7f`) are identical, and no source diffs exist between them and the working tree. The drift is not in the harness and not in the source.

**Run 0 reached baseline throughput for the first ~30 seconds, then degraded monotonically until iterations stopped completing around t≈63 s.** Per-5-second buckets in the iteration log show ramp → plateau at ~95/5 s (matching baseline's plateau exactly) → linear decay to zero. Match-creation logs from the matchmaking service follow the same envelope (peak ~10 matches/s at t≈20 s, decaying to 1 match/s by t≈63 s, then nothing). The `active_battles` gauge peaked at **5** (vs ~25 expected) and was at 0 for the entire second half of the test.

The shape is consistent with a runtime resource issue that **accumulates under sustained load and does not appear in the harness or in static source diffs** — most likely environmental (warm system + database/Redis state on 2026-05-11 vs the same containers after an hour of idle on 2026-05-12 plus three days of accumulated battle state). The most actionable single signal is **32 `40001 could not serialize access due to concurrent update` errors on `matchmaking.player_combat_profiles`** during the run, which baseline matchmaking logs are no longer available to confirm against. **Confidence: medium.** Without a clean-state rerun, I can't rule out a latent bug that the original baseline happened to dodge.

## 2. Evidence per investigation area

### 2.1 Load harness configuration drift — NOT THE CAUSE

`git log --oneline -- tests/Kombats.LoadTests/Scenarios/ tests/Kombats.LoadTests/VirtualPlayer.cs tests/Kombats.LoadTests/Program.cs` shows the last touch to harness code was `080405d` (per-iteration phase breakdown), which is the same commit referenced in the handoff. No edits since.

`tests/Kombats.LoadTests/Scenarios/ConcurrentBattlesScenario.cs:93-107` is the load shape:
- `RampingConstant(copies: pairs, during: rampUpSeconds)` then `KeepConstant(copies: pairs, during: steadyDurationSeconds)`
- `pairs = opts.Load.PairCount = 25` (from `appsettings.json:11`)
- `RampUpSeconds = 30`, `TestDurationSeconds = 120`, so steady = 90 s

Important — and counterintuitive given the variable name `pairs`: one NBomber iteration is **one virtual user / one bot**, not one pair. Two iterations (matched bots) share a `BattleId`. The 25 copies mean 25 concurrent bots, ~12 concurrent battles when paired up; the harness gets to 50 bot-sessions / 25 battles by letting `KeepConstant` recycle finished iterations.

Per-iteration delays: `PollUntilMatchedAsync` polls queue status every `Task.Delay(500, ct)` (`VirtualPlayer.cs:294`). Heartbeat every 10 s (`VirtualPlayer.cs:253`). These are unchanged and match what the handoff documented.

**No edits to scenarios, virtual-player code, options, or NBomber simulation parameters between the baseline run and Run 0.**

### 2.2 Iteration log content vs handoff numbers

`scripts/aggregate-phases.sh` overall, 628 successful iterations each:

| Phase | Baseline p50/p95 | Run 0 p50/p95 |
|---|---|---|
| `auth_ms` | 0 / 0 | 0 / 31.6 |
| `onboard_ms` | 3.7 / 6.5 | 3.6 / 15.9 |
| `connect_ms` | 4.9 / 9.3 | 4.8 / 9.0 |
| `queue_wait_ms` | **1018 / 1525** | **512 / 1020** |
| `join_battle_ms` | 4.4 / 10.0 | 5.2 / 13.9 |
| `battle_ms` | 49 / 132 | 43 / 146 |
| `total_ms` | **1115 / 1609** | **582 / 1127** |

`queue_wait_ms` halved (~one 500 ms poll cycle, instead of two), which is what dragged `total_ms` down. Battle/join phases are unchanged. This rules out a per-step slowdown — every component is fine in isolation. What's missing is **iterations**.

**Iteration completions per 5-second wall-clock bucket** (seconds since run start):

| t (s) | Baseline | Run 0 |
|---:|---:|---:|
| 5  | 2   | 6   |
| 10 | 26  | 30  |
| 15 | 68  | 28  |
| 20 | 94  | 98  |
| 25 | 94  | 96  |
| 30 | 98  | 88  |
| 35 | 94  | 90  |
| 40 | 92  | 66  |
| 45 | 90  | 46  |
| 50 | 100 | 34  |
| 55 | 92  | 18  |
| 60 | 90  | 16  |
| 65 | 98  | 12  |
| 70–115 | 92–100 | **0** |
| 120 (tail) | 54 → 3 | 1 |

**Baseline holds a flat ~95/5s plateau for the entire 90-second steady period. Run 0 hits the same peak (~98/5s at t=20–25), then decays monotonically and flat-lines for the last ~55 seconds of the test.** Only 1 of the expected ~25 in-flight bots at test-end recorded a result (the QueueTimeout at `total_ms=56945`, meaning one bot sat in queue from t≈63s to test-end without ever being paired).

### 2.3 Environment state

`docker compose ps`: all 14 containers Up. Matchmaking, BFF, Battle, Players, Postgres, Redis, RabbitMQ, Keycloak, OTel collector all running. Containers were started ~1 hour before Run 0 (status "Up About an hour"). All baseline runs from 2026-05-11 were against a different container instance — matchmaking logs from 5-11 are no longer accessible (`docker logs kombats-matchmaking` earliest entry is `[10:33:11 INF]` on 5-12).

Prometheus `active_battles` for the Run 0 window (5s step, `2026-05-12T12:25:00Z`–`12:30:00Z`):
- Peak: **5** (at t=12:27:20 UTC ≈ run+31 s)
- After 12:28:00 UTC (run+71 s): **0** for the remainder of the test
- This is far below the ~12–13 concurrent battles the harness expects given 25 paired bots.

Matchmaking pairing rate (counted from "Match created … MatchmakingPairingWorker" log lines per second): peaks at ~10 matches/s during t=20–35 s, declines to 1 match/s by t=60 s. **Same envelope as iteration completion.**

Postgres state, post-run:
- `matchmaking.player_combat_profiles` row count: **50** (matches seeded pool — clean).
- `battle.battles` row count: **315** (Run 0 added ~314 to leftover state).
- Redis DB 0 has **7089 `battle:*` keys** — accumulated across all 5-11 + 5-12 runs; this is leftover state, not active.
- Redis DB 1 (matchmaking): no keys at the time of inspection (post-run, queue empty).
- RabbitMQ queues: 0 messages on all 7 queues, all have a consumer attached.

**Matchmaking error spike during Run 0:** 32 `Npgsql.PostgresException: 40001 could not serialize access due to concurrent update` against `UPDATE matchmaking.player_combat_profiles` (the projection of `PlayerCombatProfileChanged` events). These are SERIALIZABLE-isolation contention errors. The 5-11 baseline cannot be checked because the matchmaking container has been restarted since.

### 2.4 Source code differences in `src/Kombats.*` since Chapter 2 final commit

`git log --oneline 2842b7f..HEAD` returns nothing — `HEAD` *is* the Chapter 2 final commit. `git diff HEAD -- src/Kombats.` returns nothing. `git diff 2842b7f cb1aa22 -- src/Kombats. tests/Kombats.LoadTests/` returns nothing — the on-development merge commit `2842b7f` and the original feature-branch commit `cb1aa22` are content-identical.

**Working-tree changes outside the source/harness:**
- `docker-compose.yml` adds a `keycloak-bootstrap` one-shot init container (lines +75…+105) that disables `sslRequired` on the master realm via `kcadm.sh`. It is `restart: no` and has no runtime cost. Not a regression suspect.
- `.claude/settings.local.json` modified (no functional impact).
- Many new `.md` files (docs only).

**No code change can explain the drift.**

## 3. Ranked hypotheses

### H1 — Environmental / accumulated state difference *(most likely; medium-high confidence)*

Run 0 hit baseline peak throughput. The system can do it. Sustained throughput then decayed to zero. The two run environments differ in:
- 5-11 runs were preceded by smaller runs (159, 157, 2127 iters) that warmed caches and exercised the system in succession; Run 0 was the first run after ~1 h idle on a freshly restarted container.
- Postgres has 315 stale battles plus 7089 Redis keys carried over from 5-11. Not enormous, but not zero.
- 32 SERIALIZABLE conflicts on `matchmaking.player_combat_profiles` — these may have been present in baseline too (we can't see the 5-11 logs), or they may be new because of accumulated state interacting with concurrent projection writes. Each conflict means a transaction retry, and retries under sustained 25-way concurrency cascade.

**For:** explains why peak matches baseline (system fine cold) but sustain doesn't (system degrades under continued load). Explains why no code diff. Explains the monotonic decay.
**Against:** 32 errors over the run is not obviously catastrophic — they should retry and recover; doesn't immediately explain why pairing falls to zero. Could be a symptom rather than the cause.

### H2 — Lease-renewal bug is back (or only partially fixed) *(possible; low-medium confidence)*

The shape of Run 0 (initial spike → monotonic decay → flat-line) is exactly the "lease overhead" symptom that Chapter 2 chased. The fix is in `HEAD` (the `cb1aa22`/`2842b7f` pair), but the original investigation may have only fixed one of multiple lease-related bugs, or fixed it for a narrower scenario than the load test exercises.

**For:** matches the symptom pattern Chapter 2 documented. The lease-tick cancellation path is a known pain point.
**Against:** no source diff between baseline (which ran successfully with this fix in working tree per the handoff timeline) and Run 0. If the fix is in, behavior should match. Unless the baseline measurement itself wasn't made against this exact code, in which case the handoff numbers are suspect.

### H3 — Handoff baseline was measured under unusually favorable conditions *(possible; low confidence)*

The 5-11 18:38 baseline was the third run that day on the same containers. Runs 1 and 2 (17:48 and 18:20) had only 159 and 157 iterations respectively — far short of 2127. Either they ran for a short duration on purpose, or they too degraded and the team kept retrying until they got a "good" run. The handoff cites the third run as "the baseline" without noting the earlier runs.

**For:** explains the gap without needing a real regression. Day-of-test runs land where they land.
**Against:** the team probably knows the earlier two were short on purpose; without their notes I can't confirm.

### H4 — Real regression in source code *(unlikely)*

Ruled out by `git diff 2842b7f HEAD = ∅` and `git diff HEAD -- src/Kombats. = ∅`. The only way this is the cause is if the **runtime image** in Docker is built from a different commit than the working tree. Worth checking the running image SHA, but unless the team has been doing local image edits, this should be false.

## 4. Recommended next action

**Inconclusive — need additional data.** Specifically, before changing anything:

1. **Rerun Run 0 from a clean state.** `docker compose down -v && docker compose up -d`, reseed users, run `dotnet run -- load`. If the baseline returns, H1 (accumulated state) is confirmed and the action is to add a teardown step before each measurement run. If the drift persists, we are looking at a real environmental or latent code issue.
2. **In parallel with that rerun**, capture matchmaking-service logs for the full duration so we can compare serialization-error count and pairing-tick pacing against the warm-image rerun.
3. Only after step 1, decide whether to (a) update `LOAD_TEST_PLAN.md` to record the clean-state number as the canonical baseline (probably the right move regardless), or (b) open a Chapter 2.5 mini-fix.

I'm explicitly **not** recommending a code change. The evidence does not support one yet.

## 5. Questions for the architect

1. Were the two short 5-11 runs (17:48, 18:20) deliberately short, or did they also degrade like Run 0? If the latter, the "baseline" was the lucky third roll of the dice and Run 0 is the more representative number.
2. Is the baseline expected to be reproducible across container restarts, or is it implicitly "after a warm-up phase"? The handoff doesn't say.
3. Are the 32 `40001 serialize access` errors on `matchmaking.player_combat_profiles` expected during a 25-way concurrent run? They look like contention on the consumer-side projection (Players → Matchmaking) — if SERIALIZABLE is the right isolation level for that projection at this concurrency, retry behavior may need a look, but that's Chapter 3 territory at earliest.
4. Confirm: is the running matchmaking container built from `HEAD = 2842b7f`? If the local image is stale (built before the lease fix), the symptom is fully explained without any deeper investigation. I didn't check image SHAs because the constraint forbids container actions, but a `docker inspect kombats-matchmaking | grep -i image` plus `git log` would settle it.
5. Should the load test enforce a clean DB/Redis state between runs as part of the harness contract, rather than relying on the operator to remember?
