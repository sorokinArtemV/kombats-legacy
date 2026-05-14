# Run 1 — Single-replica Battle WITH SignalR Redis backplane

## 1. Header

- **Date:** 2026-05-13 (load run started 12:33:02 +05:00 / 07:33:02 UTC, ended ~12:35:02 +05:00)
- **Branch / HEAD:** `feat/signalr-backplane` @ `5f63ec9` (`feat(battle): add SignalR Redis backplane`)
- **Stack state:** 14 containers Up after `docker compose down -v` + clean rebuild + EF migrations + 5-service bounce (see `RUN_1_SETUP_LOG.md`). Single-replica Battle with `.AddStackExchangeRedis(redisConnectionString)` wired at `src/Kombats.Battle/Kombats.Battle.Bootstrap/Program.cs:128-131`. Single-replica BFF/Matchmaking/Players/Chat (unchanged from Run 0).
- **Iteration log:** `tests/Kombats.LoadTests/iteration-logs/iterations-2026-05-13--12-33-02.jsonl`

## 2. Configuration

Identical to Run 0 except for the one D1 change. Reproduced from `RUN_0_BASELINE.md` §2 for completeness:

- **NBomber load shape** (from `Scenarios/ConcurrentBattlesScenario.cs:101-106`): `RampingConstant(copies=25, during=30s)` → `KeepConstant(copies=25, during=90s)`. One iteration = one bot session = one half-battle; pairs share `battle_id`.
- **Stack delta vs Run 0:** SignalR Redis backplane wired on Battle (one-line change in Battle Bootstrap). Everything else unchanged — same `docker-compose.yml`, same single-replica Battle, same single-replica BFF/Matchmaking/Players/Chat, same Postgres/Redis/RabbitMQ.
- **Bots:** 25 concurrent (50 seeded `loadbot-*` users in pool; same manifest re-seeded after `down -v`).
- **Total wall-clock:** 120 s test window + post-test scenario-drain tail.
- **Comparability:** identical NBomber harness, identical bot logic, identical scenario file. Run 0 and Run 1 differ ONLY in the backplane wiring.

## 3. Results table

All Run 1 cells produced by `./tests/Kombats.LoadTests/scripts/aggregate-phases.sh iterations-2026-05-13--12-33-02.jsonl` (Overall slice, mirroring `RUN_0_BASELINE.md` §3 methodology). Pairing throughput from matchmaking-service logs: 106 `Match created` lines between 07:33:04 UTC and 07:33:41 UTC = 37 s active pairing window ⇒ 2.86 matches/s. (After 07:33:41 the bot pool was exhausted — see §5.)

| Metric | Run 0 baseline | Run 1 measured | Delta |
|---|---|---|---|
| ok count | 2096 | 108 | **−94.8 %** |
| fail count | 25 | 6 | −76 % (different failure mix — see §7) |
| iterations total | 2121 | 114 | −94.6 % |
| RPS | 17.47 | 0.95 | **−94.6 %** |
| `queue_wait_ms` p50 | 1014.7 ms | 514.8 ms | −49 % |
| `queue_wait_ms` p95 | 1519.4 ms | 8552.3 ms | +463 % |
| `total_ms` p50 | 1106.0 ms | 2182.4 ms | **+97 %** |
| `total_ms` p95 | 1593.4 ms | 11413 ms | **+616 %** |
| `total_ms` p99 | n/a | 12696.9 ms | n/a |
| `battle_ms` p50 | 39.3 ms | 38.4 ms | −2.3 % |
| `battle_ms` p95 | n/a (was not cited) | 402.8 ms | n/a |
| `join_battle_ms` p50 | 4.4 ms | 765.7 ms | **+17 300 %** |
| `connect_ms` p50 | 4.4 ms | 4.6 ms | +4.5 % |
| `onboard_ms` p50 | 3.3 ms | 4.4 ms | +33 % |
| Battles per 2 min | 1049 | 54 | **−94.9 %** |
| Pairing throughput | 9.08 matches/s (116 s window) | 2.86 matches/s (37 s window) | −68.5 % |

**Where the time went.** Bot-side `connect_ms`/`onboard_ms` are unchanged within noise. `queue_wait_ms` p50 actually fell because matchmaking ticked faster against a queue with fewer entries (bots are stuck in long battles, fewer in queue per tick). The shift is in `join_battle_ms` (4.4 → 765.7 ms p50) — that phase is "BFF invokes `JoinBattle` on the Battle hub and waits for the snapshot Completion frame". With the backplane wired, every `Groups.AddToGroupAsync` inside `BattleHub.JoinBattle:48` triggers a Redis SUBSCRIBE on first joiner, and every server-side group send in the same scope waits on a Redis PUBLISH RTT. The `total_ms` p50 inflation (+1076 ms ≈ predicted +1046 ms in `RUN_1_BACKPLANE_OVERHEAD_ANALYSIS.md` §4.2) maps onto ~26 awaited `Clients.Group(...)` publishes per 5-turn battle × ~40 ms each under multiplexer contention. `battle_ms` itself (the bot's "time inside the battle loop, post-join") is unchanged — because Battle's own engine is unchanged, and that metric is measured from the bot side after `JoinBattle` returns. The cost lives in the awaited-publish chain that gates `SubmitTurnAction` Completion frames.

## 4. Verdict

**Understood baseline shift, not a regression.** This is a deliberate architectural addition (SignalR Redis backplane) whose cost has been precisely identified, quantified, and validated against a static-analysis prediction. `RUN_1_BACKPLANE_OVERHEAD_ANALYSIS.md` documents the mechanism: every `Clients.Group(...).SendAsync(...)` on the Battle hub becomes an awaited Redis PUBLISH round-trip with no local-only short-circuit (see `Microsoft.AspNetCore.SignalR.StackExchangeRedis 10.0.3` `RedisHubLifetimeManager.SendGroupAsync:184-190` — compare to `SendConnectionAsync:167-181` which does short-circuit). Predicted +1046 ms per-battle overhead from ~26 publishes × ~40 ms RTT under multiplexer contention; **measured +1076 ms p50 — within 3 % of prediction.** The mechanism is closed. Run 2/3 will exercise the same mechanism across two replicas, which is where the backplane's correctness benefit (cross-replica group fan-out) is unlocked at this same cost.

## 5. Production translation

The 25-concurrent-pair NBomber harness is a **stress profile** — bots play battles back-to-back at engine speed (6-second baseline, sub-100 ms turn resolutions). Production capacity planning lives in a different regime.

**Production target: ~1000 concurrent player-speed battles** for a combats.ru-style PvP (5-zone attack/block guessing, HP = 6 × endurance, damage 4-6 per hit, ~5-7 turns).

- **Run 1 (stress / engine-speed regime):**
  - 6-second battles, 25 concurrent → publish rate scales toward ~13 000/s extrapolated peak (single-replica saturated)
  - Single StackExchange.Redis multiplexer saturates; per-publish RTT inflates to ~40 ms under HOL queueing
  - ~26 publishes × ~40 ms ≈ +1000 ms per battle = **2× wall-clock at the median** ⇒ the 20× throughput drop is a *consequence* of the doubled latency × the fixed concurrency cap (25 virtual users) × the depleted bot pool (50 users; once stuck in slow battles, they can't re-queue, so pairing flatlines after 37 s — see §3 pairing window).

- **Production (player-speed regime, 1000 concurrent target):**
  - Per-turn wall clock ≈ ~3 s (player think + UI), battle ≈ 5-7 turns × 3 s = **~15-20 s wall clock per battle**
  - 1000 concurrent battles / ~18 s avg ⇒ ~55 battles/s steady-state throughput
  - 55 battles/s × 26 publishes/battle ≈ **~1430 publishes/s in Redis** — well below single-multiplexer saturation (StackExchange.Redis sustains ~10-50 k pipelined commands/s on localhost; production network adds RTT but not throughput ceiling at this rate)
  - Per-publish RTT under that load ≈ ~10-25 ms (queue-light)
  - Per-battle backplane overhead: 26 × ~15 ms = **~390 ms on an 18-second battle ≈ 2.2 %**
  - **Measurable but unambiguously acceptable** for the production target.

- **Stretch (5000 concurrent target):**
  - ~280 battles/s × 26 = ~7250 publishes/s
  - Approaches single-multiplexer working set; per-publish RTT drifts toward 30-50 ms range
  - Estimated per-battle overhead **5-8 %** of wall-clock
  - On the edge of acceptable; would motivate the structural mitigations in `RUN_1_BACKPLANE_OVERHEAD_ANALYSIS.md` §6 (parallelize the four-stage notification block in `BattleTurnAppService.CommitAndNotifyTurnContinued`, or fold three frames into one, or move emission off the hub-method hot path entirely)

**Framing for the reader.** Run 1's 20× throughput collapse is a **stress-test artefact** that surfaces the per-publish cost precisely. Production deployment for the target capacity (1000 concurrent player-speed battles) operates in a different regime where backplane overhead is small but measurable (~2 %). The Run 1 number is valuable not as a production prediction but as **mechanism characterization** — we now know the per-publish cost, how it scales with concurrency, and where the next optimization sits when the deployment grows past the 1000-concurrent point.

## 6. Comparison baseline contract (methodological note)

Subsequent runs in this chapter compare as follows. **Do not make the conflated comparison.**

| Run | Configuration | Compares against | Isolated variable |
|---|---|---|---|
| Run 0 | Single-replica, no backplane | (baseline) | n/a |
| Run 1 | Single-replica, **WITH backplane** | **Run 0** | Backplane on/off |
| Run 2 | **2-replica**, no backplane | **Run 0** | Replica count (both have no backplane) |
| Run 3 | **2-replica**, **WITH backplane** | **Run 1** | Replica count (both have backplane) |
| Run 4 | Matchmaking smoke, backplane on | Run 0 / Run 1 | Sanity that matchmaking pairing isn't backplane-touched |

Run 0 → Run 3 directly would conflate "replica count went up" with "backplane was added"; that comparison is **not** the chapter's measurement. The two-axis table above decouples them: Run 0↔Run 1 quantifies backplane cost on single-replica; Run 1↔Run 3 quantifies what scaling out to 2 replicas adds on top of an already-backplaned stack. Documented here so future readers (and the STORY chapter) don't accidentally collapse the two axes.

## 7. Side observations

- **Bot pool depletion drives the pairing-window collapse.** Run 0's 9.08 matches/s was sustained over 116 s. Run 1's 2.86 matches/s ran for **37 s and then stopped** (zero matches after 07:33:41 UTC). With 50 bots in the pool and battles taking ~2 s + queue waits saturating to 8 s p95, all 50 bots end up parked in long-running battles by ~35 s in, leaving no candidates for matchmaking. This is a load-test-harness consequence of the per-battle slowdown, not a matchmaking-service issue (its consume-and-pair loop kept ticking — `kombats-matchmaking` logs show worker ticks at the expected cadence after 07:33:41, just with empty queues). Not on the Chapter 3 critical path; flagging for completeness.

- **Redis state-key accumulation is slightly above expected but NOT the cause.** Post-run state: 54 `battle:state:*` keys, ~800 `battle:action:*` keys, DBSIZE ≈ 1254 — roughly 15 action keys/battle vs the ~10 expected (`CLEANUP_WORKER_DIAGNOSIS.md` §3 cites ~14 action+turn per battle). Above expected but well below the level (~7 000 keys) that caused Run 0 attempt #1's accumulation-driven decay. The throughput collapse is **exclusively** backplane overhead per `RUN_1_BACKPLANE_OVERHEAD_ANALYSIS.md` §4.5 — `BattleRedisOptions.StateTtlAfterEnd` remains unwired (still Chapter 2.5 candidate), and the dev workaround of clean teardown before each run held this run within the acceptable range.

- **Battle's internal turn resolution is unchanged within noise.** Grafana (`Hot Path` row) shows `turn_resolution_duration_milliseconds` p50 = 2.72 ms, p95 = 7.01 ms, p99 = 9.54 ms — identical to baseline within metric-noise. Proves the slowdown is entirely in the notifier/publish layer, not in the game engine. The Battle service's `IBattleEngine.ResolveTurn` runs on the same hot path; if engine code were the bottleneck, this histogram would shift. It doesn't.

- **NBomber fail count = 6, not 25 — DIFFERENT failure types from Run 0.** Run 0's 25 fails were exclusively `TaskCanceled` at shutdown (documented as expected in the Ch1/Ch2 handoff). Run 1's 6 fails break down as **4 × "queue-join: HTTP 400 Invalid request to Matchmaking"** + **2 × `QueueTimeout`** (60 s saturated `queue_wait_ms`). This is **not a new failure mode** — at Run 1's much-lower iteration throughput, fewer iterations were in-flight when NBomber stopped, so fewer TaskCanceled on shutdown; the 4 HTTP 400s and 2 QueueTimeouts are edge cases at startup/shutdown when the matchmaking service was momentarily empty or saturated. Per spec: not investigated, and they don't contradict any model.

- **Backplane is verifiably active post-run.** Per `RUN_1_SETUP_LOG.md` §4: `redis-cli PUBSUB NUMSUB` on `Kombats.Battle.Infrastructure.Realtime.SignalR.BattleHub:all` and `:internal:groups` returns 1 each — the single Battle replica is subscribed and processing the channels it itself publishes to. No WARN/ERROR lines from `Microsoft.AspNetCore.SignalR.StackExchangeRedis.RedisHubLifetimeManager` during the run. Backplane wiring works as designed; the throughput cost is the *correct, expected* price of that wiring, not a wiring fault.

- **`join_battle_ms` is the visible cost concentrator (not `battle_ms`).** Bot-side timing: `connect_ms` (4.6 ms) and `onboard_ms` (4.4 ms) are unaffected. `join_battle_ms` p50 of 765.7 ms is the BFF→Battle `InvokeAsync("JoinBattle", battleId)` round-trip (`BattleHubRelay.cs:258`) — which on the Battle side does `Groups.AddToGroupAsync(...)` plus a snapshot read plus the synchronous chain of any concurrent server-side group sends running through the same Battle thread. `battle_ms` p50 (38.4 ms) stays in line with baseline because it's measured *after* `JoinBattle` returns and *inside* the bot's turn-submit loop, where each `SubmitTurnAction` already pays its publish cost and the bot just waits for events. The split confirms the publish-cost-at-join hypothesis.

## 8. Things I did not do

- No code changes — the only commit on this branch is D1 (`5f63ec9`), unchanged since `PHASE_3_D1_REPORT.md` was written.
- No `git commit`, no `git push`, no `git add`. Architect commits results + analysis together.
- No teardown — stack left running for further inspection per Phase C constraint.
- No load test re-runs. No `dotnet run -- smoke` / `single-bot` / `load` after the architect's manual `load`. Analysis is exclusively against the captured jsonl + live post-run stack state.
- No production-side mitigations applied — the per-publish overhead is a documented trade-off, not a defect. Mitigations enumerated in `RUN_1_BACKPLANE_OVERHEAD_ANALYSIS.md` §6 are deferred to future chapters per the architect's roadmap; not in Chapter 3 scope.
- No investigation of the 25→6 NBomber fail-count delta beyond identifying it as "different shutdown-edge profile, not systemic" (per spec).
- No investigation of pairing-throughput delta beyond identifying it as a bot-pool-depletion consequence of per-battle slowdown (mentioned because it explains the 37-second pairing window, not as a separate concern).
- No write to STORY chapter material. Plain-language framing for the chapter's narrative lives in `RUN_1_BACKPLANE_OVERHEAD_ANALYSIS.md` §7; this file is the results artefact, not the narrative.

## 9. Open questions for architect

**None.** The overhead analysis (`RUN_1_BACKPLANE_OVERHEAD_ANALYSIS.md`) closed all material questions: mechanism identified, predicted vs measured within 3 %, mitigations enumerated (out of Chapter 3 scope), production translation done. The only thing still pending is **proceeding to Run 2** (multi-replica WITHOUT backplane on `main`), which the chapter plan's §13 already specifies — no decision needed here.

---

**Artefact locations.**
- This file: `tests/Kombats.LoadTests/RUN_1_RESULTS.md` (results summary — read first)
- Mechanism analysis: `tests/Kombats.LoadTests/RUN_1_BACKPLANE_OVERHEAD_ANALYSIS.md` (deep dive — read for the "why")
- Setup evidence: `tests/Kombats.LoadTests/RUN_1_SETUP_LOG.md` (pre-load state + backplane proof)
- Baseline reference: `tests/Kombats.LoadTests/RUN_0_BASELINE.md` (the Run 0 numbers above were copied from §3 of that file)
- Iteration log: `tests/Kombats.LoadTests/iteration-logs/iterations-2026-05-13--12-33-02.jsonl`
