# Run 4 — Single-replica Battle WITH backplane + skip-negotiation (matchmaking regression + Goal-2 control)

## 1. Header

- **Date:** 2026-05-14 (load run 10:08:13 → 10:10:13 +05:00 / 05:08:13 → 05:10:13 UTC, NBomber 120 s + drain tail).
- **Branch / HEAD:** `feat/signalr-backplane` post-D1.5 (uncommitted working tree state — same as end of Run 3 + D1.5 + Run 3 docs + Run 4 setup-log + D6 §11 amendment).
- **Stack state:** 15 long-running containers + 1 one-shot bootstrap after the **D6-corrected** compose chain (`docker-compose.yml` + `observability/docker-compose.observability.yml` + **`observability/docker-compose.observability.override.yml`** + `docker-compose.override.yml`). Single-replica Battle, single-replica BFF/Matchmaking/Players/Chat. **D1 (backplane) + D1.5 (skip-negotiation) both active.** OTel pipeline end-to-end green for the first time in Chapter 3 — see `RUN_4_SETUP_LOG.md` §11.
- **Iteration log:** `tests/Kombats.LoadTests/iteration-logs/iterations-2026-05-14--10-08-13.jsonl` (2103 rows).
- **Cumulative Chapter 3 source changes:** still exactly two — D1 (commit `5f63ec9`) and D1.5 (uncommitted in `BattleHubRelay.cs`). No further `src/Kombats.*` changes for Run 4. Per chapter scope, this remains the final source change set.
- **Last load run of Chapter 3.** Next artefact is `CHAPTER_3_REPORT.md` (the chapter closer).

## 2. Configuration

Identical NBomber harness across all four runs — `RampingConstant(copies=25, during=30s) → KeepConstant(copies=25, during=90s)` (`Scenarios/ConcurrentBattlesScenario.cs:101-106`). 25 concurrent VUs, 50 seeded `loadbot-*` users. The structural variable across runs is stack shape:

| Run | Replicas | Backplane (D1) | Skip-negotiation (D1.5) | Compose-chain OTel override |
|---|---|---|---|---|
| Run 0 | 1 | ❌ | ❌ | (irrelevant — no backplane) |
| Run 1 | 1 | ✅ | ❌ | broken (silent: see §9.3.b in Run 3) |
| Run 2 | 2 | ❌ | ❌ | broken (silent; `battle-2` had OTel env inlined) |
| Run 3 | 2 | ✅ | ✅ | broken (silent; `battle-2` had OTel env inlined) |
| **Run 4** | **1** | **✅** | **✅** | **fixed (D6 repair)** |

Run 4 is the first measurement in Chapter 3 with a fully-working observability pipeline. The architect's live Grafana observations (cited in §9) are therefore valid for the first time — prior runs' Grafana panel evidence was partial and replica-dependent.

**Comparison-baseline contract for Run 4** (per `RUN_4_SETUP_LOG.md` §7):
- **Goal 1 (primary):** matchmaking pairing throughput vs Run 0's 9.08 matches/s. ±5 % gate.
- **Goal 2 (bonus):** single-replica latency control. Compares against **both Run 0** (no-D1, no-D1.5 baseline) **and Run 1** (D1, no-D1.5). Run 3 (multi-replica) is a *reference* column, not the primary comparison.

## 3. Results table — Run 4 vs Run 0 / Run 1 / Run 3

All Run 4 cells from `./scripts/aggregate-phases.sh iteration-logs/iterations-2026-05-14--10-08-13.jsonl` (Overall slice unless noted). Pairing throughput from matchmaking-service log timestamps (`ExecuteMatchmakingTickHandler` source-of-truth): 1043 `Match created` lines between 05:08:15 UTC and 05:10:13 UTC = 118 s ⇒ **8.84 matches/s**.

| Metric | Run 0 (single, no-bp, no-skip-neg) | Run 1 (single, +bp, no-skip-neg) | Run 3 (multi, +bp, +skip-neg) | **Run 4 (single, +bp, +skip-neg)** | Δ vs Run 0 | Δ vs Run 1 |
|---|---|---|---|---|---|---|
| ok count | 2096 | 108 | 2088 | **2078** | −0.9 % | +1824 % |
| fail count | 25 | 6 | 25 | **25** (23 QueueTimeout + 2 Error — see §9.1) | 0 % | +316 % |
| iterations total | 2121 | 114 | 2113 | **2103** | −0.8 % | +1745 % |
| RPS | 17.47 | 0.95 | 17.40 | **17.32** | −0.9 % | +1723 % |
| `auth_ms` p50 | (not cited) | (not cited) | (≈0) | **0.0** | n/a | n/a |
| `onboard_ms` p50 | 3.3 ms | 4.4 ms | 3.6 ms | **3.7 ms** | +12 % | −16 % |
| `connect_ms` p50 | 4.4 ms | 4.6 ms | 4.9 ms | **5.0 ms** | +14 % | +9 % |
| `queue_wait_ms` p50 | 1014.7 ms | 514.8 ms (pool depleted) | 1016.8 ms | **1018.7 ms** | +0.4 % | +98 % (back to baseline) |
| `queue_wait_ms` p95 | 1519.4 ms | 8552.3 ms | 1522.4 ms | **1526.6 ms** | +0.5 % | −82 % |
| **`join_battle_ms` p50** | **4.4 ms** | **765.7 ms** | **4.2 ms** | **4.1 ms** | **−7 %** | **−99.5 %** |
| **`join_battle_ms` p95** | n/a | n/a | 9.7 ms | **10.0 ms** | n/a | n/a |
| **`battle_ms` p50** | **39.3 ms** | **38.4 ms** | **56.4 ms** | **60.4 ms** | **+54 %** | **+57 %** |
| **`battle_ms` p95** | n/a | 402.8 ms | 275 ms | **228.5 ms** | n/a | −43 % |
| `total_ms` p50 | 1106 ms | 2182.4 ms | 1124.7 ms | **1130.6 ms** | **+2.2 %** | **−48 %** |
| `total_ms` p95 | 1593.4 ms | 11 413 ms | 1618.5 ms | **1629.6 ms** | +2.3 % | −86 % |
| `total_ms` p99 | n/a | 12 696.9 ms | 1657.6 ms | **1670.2 ms** | n/a | −87 % |
| Battles per 2 min (NBomber iters / 2) | 1049 | 54 | 1057 | **1052** | +0.3 % | +1848 % |
| Battles created in Postgres | 1049 | n/a | 1052 | **1043** (this run) + 3 pre-load smokes = 1046 | n/a | n/a |
| Pairing throughput (matches/s) | 9.08 (116 s) | 2.86 (37 s — collapsed) | 8.87 (118 s) | **8.84 (118 s)** | **−2.6 %** | +209 % |

### 3a — Per-outcome breakdown (Run 4)

| Outcome | Count | Share | total_ms p50 | battle_ms p50 | What happened |
|---|---|---|---|---|---|
| Won | 926 | 44.0 % | 1128.3 ms | 58.3 ms | Normal battle, bot won |
| Lost | 926 | 44.0 % | 1132.4 ms | 60.9 ms | Normal battle, bot lost |
| Draw | 226 | 10.7 % | 1134.4 ms | 66.4 ms | Normal battle (typically longer — both sides at low HP) |
| QueueTimeout | 23 | 1.09 % | 733.8 ms | 0 ms | Never paired; NBomber shutdown tail |
| Error | 2 | 0.10 % | 1553.9 / 1577.3 ms | 0 ms | `A task was canceled.` — see §9.1 |

**Successful = Won + Lost + Draw = 2078 / 2103 = 98.81 %.** Within the predicted band (98–99 % per `RUN_3_RESULTS.md` model). Run 0 baseline = 98.82 %, Run 3 = 98.82 %, Run 4 = 98.81 % — three-decimal-place identical across the runs that aren't broken.

## 4. Goal 1 result — matchmaking pairing throughput unaffected by backplane

**Measured:** 1043 `Match created` lines (ExecuteMatchmakingTickHandler source-of-truth) between 05:08:15 UTC and 05:10:13 UTC = 118 s active pairing window ⇒ **8.84 matches/s**.

**vs Run 0's 9.08 matches/s baseline: −2.6 %.** Within the ±5 % gate. **Goal 1 confirmed.**

Run 0 → Run 4 comparison isolates the backplane addition because matchmaking has no `AddStackExchangeRedis` call, no `IHubContext`, no SignalR DI at all — the structural prediction was "matchmaking shouldn't be affected." The empirical answer matches the structural prediction within measurement noise. The architect's Grafana observation that matchmaking HTTP server p95 stayed at ~5 ms (cited in the Phase C inputs as "matchmaking peak ~5ms") corroborates this: matchmaking's pairing path is fast, unaffected, and uninstrumented-but-unsurprising.

This closes the last "did the backplane accidentally touch X?" question in `CHAPTER_3_PLAN.md` §8 sanity checks. **Matchmaking is clean.** No code interaction, no measurable load interaction, no observability anomaly.

## 5. Goal 2 result — Outcome A: D1.5 alone restores baseline on single-replica

**Per-phase decisive table:**

| Phase p50 | Run 0 (single, ❌bp, ❌skip) | Run 1 (single, ✅bp, ❌skip) | **Run 4 (single, ✅bp, ✅skip)** | Run 3 (multi, ✅bp, ✅skip) |
|---|---|---|---|---|
| `connect_ms` | 4.4 | 4.6 | **5.0** | 4.9 |
| `onboard_ms` | 3.3 | 4.4 | **3.7** | 3.6 |
| `queue_wait_ms` | 1014.7 | 514.8 (pool depleted) | **1018.7** | 1016.8 |
| **`join_battle_ms`** | **4.4** | **765.7** | **4.1** | **4.2** |
| `battle_ms` | 39.3 | 38.4 (starved) | 60.4 | 56.4 |
| `total_ms` | 1106 | 2182.4 | **1130.6** | 1124.7 |

**Run 4's `join_battle_ms` p50 = 4.1 ms — indistinguishable from Run 0's 4.4 ms** (and 0.1 ms lower than Run 3's 4.2 ms). The Run 1 → Run 4 collapse on this phase is **−761.6 ms** (−99.5 %) — i.e. effectively complete.

This is **Outcome A** from `RUN_4_SETUP_LOG.md` §7's prediction matrix:

> *Outcome A:* Run 4 total_ms p50 ≈ 1100 ms (Run 0 territory). → D1.5 alone restores baseline on single-replica. Multi-replica's contribution to Run 3's join_battle_ms collapse is small.

Measured: Run 4 `total_ms` p50 = 1130.6 ms (vs Run 0's 1106 — +2.2 %, within noise). Run 4 `join_battle_ms` p50 = 4.1 ms (vs Run 0's 4.4 — −7 %, within noise). Both metrics land in Run 0 territory on a single replica with backplane wired, when D1.5 is also applied.

**Causal implication.** The Run 1 → Run 4 Δ isolates the effect of D1.5 alone (with the second replica held constant at 1). The Run 1 → Run 3 Δ is the joint effect of D1.5 + multi-replica. The two deltas are nearly identical on `join_battle_ms` (−761.6 ms for Run 4, −761.5 ms for Run 3) and on `total_ms` (−1051.8 ms for Run 4, −1057.7 ms for Run 3). **D1.5 is the dominant contributor; the second replica's marginal effect on join-phase latency is on the order of ~−6 ms or less — under the noise floor of single-run sampling.**

This *partially closes* `RUN_3_RESULTS.md` §11.5 item 1 ("D1.5 vs second replica — relative contribution to the `join_battle_ms` collapse is not isolated"). It does not *fully* close item 1, because:
- Run 4 measures D1.5-alone vs Run 1, isolating D1.5's contribution.
- It does NOT measure multi-replica-alone (no Run 5 was run with multi-replica + backplane *without* D1.5). So the multi-replica-alone effect on `join_battle_ms` is still inferred (estimated ~−6 ms by subtraction) rather than directly measured.
- The architect's call (`RUN_3_RESULTS.md` §11.5 introduction): a Run 5 control is not on Chapter 3's critical path because the production target uses the joint configuration.

## 6. Causal isolation — before/after Run 4

| Claim | Pre-Run-4 status | Post-Run-4 status |
|---|---|---|
| Event A closed by D1.5 | Confirmed at smoke (5/5 in `RUN_3_SETUP_LOG.md` §14.6) + Run 3 load (0 of 2125 WebSocket upgrades 404'd) | Re-confirmed at single-replica load: 0 of 2103 upgrades 404'd, 0 negotiate POSTs |
| Event B closed by backplane | Confirmed at multi-replica diag-smoke #2 (`RUN_3_SETUP_LOG.md` §3) + Run 3 load (1052/1052 battles ended cleanly) | N/A this run (single-replica) — Run 4 does not stress the cross-replica path |
| Run 1's overhead lived in `join_battle_ms` (handshake setup), not in `battle_ms` (per-turn publishes) | Inferred from Run 3 per-phase data + code-level bracketing (`RUN_3_RESULTS.md` §3b) | **Confirmed and isolated:** D1.5 alone closes it; the handshake-setup cost on single-replica was wholly attributable to the negotiate-POST roundtrip + WebSocket-upgrade contention, not to multiplexer queueing during the battle |
| Relative contribution of D1.5 vs multi-replica to `join_battle_ms` collapse | Not isolated by Run 3 alone | **D1.5 is the primary contributor** (Run 1 → Run 4 delta on join_battle_ms = −761.6 ms; Run 1 → Run 3 delta = −761.5 ms; second replica's marginal contribution is ≤6 ms, indistinguishable from noise) |
| Backplane affects matchmaking | No known interaction; not measured at load | **No measurable effect.** Pairing throughput 8.84 m/s vs Run 0's 9.08 (−2.6 %, within noise) |
| §9.6 `battle_ms` cross-replica delivery attribution | Plausible but uninstrumented; flagged in `RUN_3_RESULTS.md` §11.5 item 3 as "alternative attribution would also fit" | **Refuted.** Run 4 single-replica shows the same ~+21 ms `battle_ms` inflation as Run 3. The actual mechanism is the **backplane PUBSUB hop per group send, local to each Battle process**, not cross-replica delivery. See §7 below and `RUN_3_RESULTS.md` §9.6.b for the corrected reading |
| OTel observability pipeline behavior | Believed scrape-interval-bound (per `RUN_3_RESULTS.md` §9.3) | **Refuted.** Was actually OTLP exporter silently skipped because `OpenTelemetry:OtlpEndpoint` was empty (compose-chain missed `observability/docker-compose.observability.override.yml`). D6 repair applied; Run 4 is the first chapter run with end-to-end-green OTel pipeline. See `RUN_4_SETUP_LOG.md` §11 and `RUN_3_RESULTS.md` §9.3.b |

## 7. The `battle_ms` finding — refining the §9.6 attribution

This was the surprise of Run 4. The pre-load prediction (per `RUN_4_SETUP_LOG.md` §7 and Phase C briefing) was *"Run 4 is single-replica → cross-replica delivery cost is N/A → battle_ms should be back to ~39 ms (Run 0 level)."* Measured: **60.4 ms** — even slightly higher than Run 3's 56.4 ms, and clearly +21 ms over Run 0's 39.3 ms.

The cross-replica attribution in `RUN_3_RESULTS.md` §9.6 (`a` body) is therefore **refuted by Run 4**. If cross-replica delivery were the cause, Run 4 (no cross-replica path) should have been at Run 0 levels. It wasn't.

### The actual mechanism

Per `RUN_1_BACKPLANE_OVERHEAD_ANALYSIS.md` §3.1 (source-verified at Microsoft v10.0.3): `RedisHubLifetimeManager.SendGroupAsync` has **no local-only short-circuit**. Every `Clients.Group(...).SendAsync(...)` unconditionally awaits a Redis PUBLISH RTT, then receives the message back via its own SUBSCRIBE callback and writes to local connections — even when the publisher and all subscribers are the same process. On loopback Redis at this load shape, that round-trip is ~1–2 ms per publish.

Per typical 5–7-turn battle, a bot perceives roughly **13 events** (per the breakdown in `RUN_1_BACKPLANE_OVERHEAD_ANALYSIS.md` §2.2: bot's share of `turnOpened`, `resolved`, `damaged`, `stateUpd`, `feed`). 13 × ~1–2 ms ≈ **+13–26 ms** in the bot's `battle_ms` wait time. That fits Run 4's measured +21 ms and Run 3's measured +18 ms — both within the predicted band, both consistent with each other.

### Why didn't Run 1 show this overhead in `battle_ms`?

Run 1 had **bot-pool starvation** (54 successful battles in 2 min vs ~1040 in Run 0/3/4). With the harness collapsed to 2.86 matches/s and battles taking 2+ seconds to start (because of `join_battle_ms` blow-up), bots that did reach the turn loop typically didn't run full-length play. Median Run 1 battle might have been 2–3 turns of partial play before the harness gave up on iteration timeout. Per-battle event count was much lower → per-battle backplane overhead was much lower → `battle_ms` p50 = 38.4 ms didn't reflect it.

**Run 4 is the first measurement that captures the steady-state backplane PUBSUB cost during the battle phase at full throughput.** At this load shape, it's +21 ms per typical-length battle. Small, stable, load-shape-bounded — the kind of cost that matters at production capacity-planning scale but not at smoke or test scale.

### Why is Run 4 marginally HIGHER than Run 3 (60.4 vs 56.4)?

Could be sampling noise at this measurement precision (n=2078 successful iterations in Run 4 vs 2088 in Run 3 → standard error per percentile is small but non-zero). Could be a slight per-replica processing asymmetry: with two replicas in Run 3, when both bots co-locate (P=0.5), only ONE replica is doing publishes for that battle and only that replica's loopback PUBSUB is exercised. The other replica is idle on that battle, so its multiplexer is less loaded; the publishing replica's per-publish RTT might be marginally lower than a single-replica running the full load. Single-replica Run 4 has one process doing all the PUBSUB → maybe slight queueing → slight extra ms per publish. Not chased — the effect is in the 4 ms range, well under any production-relevant threshold.

### Implications

- **The +21 ms is the actual per-battle backplane cost in steady state.** Production at 1000 concurrent player-speed battles (~18 s each, per `RUN_1_RESULTS.md` §5 framing): +21 ms / 18000 ms ≈ **0.1 % of wall-clock**. Even less than the §6 of `RUN_3_RESULTS.md` claimed (which had been 0.2 % under the multiplexer-queue-frees-itself-on-multi-replica assumption). Production-irrelevant.
- **The `RUN_1_BACKPLANE_OVERHEAD_ANALYSIS.md` §3.5 / §4.1 multiplexer-saturation framing for the battle phase was overstated.** Run 4 quantifies the steady-state per-publish-RTT contribution at ~1–2 ms, far below the 30–50 ms that analysis estimated under saturation. The saturation in Run 1 wasn't at battle-phase per-publish at all; it was at handshake-setup — already corrected in `RUN_3_RESULTS.md` §3b / §9.2 and now further refined.

## 8. Comparison baseline contract — no Run 5

Per `RUN_3_RESULTS.md` §7 + `RUN_4_SETUP_LOG.md` §7: Run 4 is the **last load run of Chapter 3**. No Run 5 is planned. The remaining gap from `RUN_3_RESULTS.md` §11.5 (item 1's "multi-replica-alone effect not directly measured") is acceptable because:

- Run 4 isolated D1.5's contribution at single-replica (the unknown that mattered for chapter framing).
- The production target uses the joint configuration (D1 + D1.5 + multi-replica), which Run 3 measured directly.
- The marginal effect of the second replica on join_battle_ms is ≤6 ms by subtraction — production-irrelevant.

Next artefact: `CHAPTER_3_REPORT.md`. The chapter closer should consolidate:
- Run 0 baseline
- Run 1 + Run 4 axis (backplane overhead at single-replica, with vs without D1.5)
- Run 2 failure proof (multi-replica without dual-fix)
- Run 3 fix proof (multi-replica with dual-fix)
- Run 4 D1.5 isolation (the structural finding from this run)
- D6 observability-pipeline repair (the operational finding)
- Chapter 2.5 candidates list (carried forward — TTL wiring, `active_battles` split-brain, OTLP silent-skip + compose-audit, BFF queue-polling latency, BFF-relay `_connections` map)

## 9. Sanity checks (all Run 0 invariants hold)

| Invariant | Run 0 baseline | Run 4 measured | Status |
|---|---|---|---|
| `total_ms` p50 within ±5 % of Run 0 | 1106 ms | 1130.6 ms (+2.2 %) | ✅ |
| `total_ms` p95 within ±5 % of Run 0 | 1593.4 ms | 1629.6 ms (+2.3 %) | ✅ |
| `queue_wait_ms` p50 within ±50 ms of Ch2 post-fix (1018 ms) | 1014.7 ms | 1018.7 ms | ✅ exactly on baseline |
| `connect_ms` p50 within ±5 ms of Run 0 | 4.4 ms | 5.0 ms (+0.6 ms) | ✅ |
| `onboard_ms` p50 within ±5 ms of Run 0 | 3.3 ms | 3.7 ms (+0.4 ms) | ✅ |
| Success rate ≥ 98 % | 98.82 % | 98.81 % | ✅ |
| Pairing throughput within ±5 % of Run 0 | 9.08 m/s | 8.84 m/s (−2.6 %) | ✅ |
| Battles created in DB ≈ ok iterations / 2 | 1049 / (2096/2 = 1048) | 1043 from this run (plus 3 pre-load smoke) / (2078/2 = 1039) | ✅ accounting clean |
| 0 stuck `mm:player:*` keys post-run | 0 | 0 | ✅ Ch2 lease fix intact |
| 0 SignalR 404s in Battle logs (D1.5 working) | n/a | 0 (only 3 `/metrics` 404s unrelated to SignalR) | ✅ |
| 0 negotiate POSTs in Battle logs (D1.5 working) | n/a (D1.5 didn't exist) | 0 | ✅ |
| Backplane channels live throughout, NUMSUB=1 | n/a | 1 on both cross-replica channels (single-replica self-subscribed) | ✅ |
| Battle engine turn-resolution latency unchanged | (varies, ~3 ms) | Grafana `turn_resolution_duration_milliseconds` p50 = 4.52 ms (architect's live observation) | ✅ engine identical |
| No new WARN+ log lines in Battle during clean run | (clean) | clean | ✅ |
| OTel pipeline end-to-end green | (was the same compose-chain bug present in all prior runs) | **all 5 services + battle UUID + bff UUID in Prometheus; collector :8889/metrics populated with `process_cpu_count`, `active_signalr_connections`, `http_server_request_duration_seconds`, `turn_resolution_duration_milliseconds`** | ✅ first chapter run with this state |

## 10. Side observations

### 9.1 (this is §10.1 — kept numbering simple) — The 2 Error outcomes are end-of-test shutdown tail

Iteration log inspection of the 2 `outcome:"Error"` rows:

```
ts="2026-05-14T10:10:13.3597580+05:00"  loadbot-0028  battle=75d87a56-...  error="A task was canceled."
ts="2026-05-14T10:10:13.3597700+05:00"  loadbot-0027  battle=75d87a56-...  error="A task was canceled."
```

Both timestamps are within 12 microseconds of each other AND **match the last iteration timestamp of the entire run** (`2026-05-14T10:10:13.3597700+05:00` is the latest record). Both bots completed auth + onboard + connect + queue_wait + JoinBattle (`join_battle_ms` = 3.8 / 4.7 ms — fine) and were paired into the same battle (`battle_id=75d87a56-...`). They never entered the turn loop — `battle_ms = 0, turns_played = 0`. NBomber's per-bot CancellationToken fired before the turn loop's first `SubmitTurnActionAsync` round trip completed.

This is the **classic NBomber TaskCanceled-on-shutdown artefact** documented in `RUN_0_BASELINE.md` §3 / handoff §7. Run 0 / Run 3 saw 25 fails (all QueueTimeout); Run 4 split the 25 differently because 2 bots happened to finish queue-wait + JoinBattle right at the cancellation boundary instead of timing out in queue.

**Not a Run 4 finding. Not a Chapter 3 failure mode.** Total fail count of 25 matches Run 0 baseline shape; the fail-mix differs by 23+2 vs 25+0 but the underlying mechanism is identical (NBomber shutdown cancellation). Rolling these into the same "shutdown tail" bucket as QueueTimeout for the §3 results table.

### 9.2 — Matchmaking HTTP p95 stayed at ~5 ms

The architect's Grafana observation cited 4.96 ms p95 for matchmaking HTTP. That's effectively unchanged from Run 0's matchmaking footprint (matchmaking has no SignalR, no backplane). Corroborates Goal 1's pairing-throughput conclusion.

### 9.3 — BFF HTTP p95 ≈ 1.87 s — same Run 0 polling pattern, not a backplane finding

Per architect: BFF HTTP server p95 peaked at 1.87 s during the run. This matches `RUN_0_BASELINE.md` §6 ("BFF queue-status endpoint p95 ≈ 1.80 s") — driven by the 500 ms client polling cadence in `VirtualPlayer.PollUntilMatchedAsync` × the `queue_wait_ms` p95 ≈ 1.53 s. Not a backplane interaction; pre-existing architectural choice (queue polling instead of push-based match notification). Already named as a Ch2.5 candidate in `RUN_0_BASELINE.md` §6.

### 9.4 — Backplane channels NUMSUB=1 stable single-replica

`docker exec kombats-redis redis-cli PUBSUB NUMSUB BattleHub:all BattleHub:internal:groups` returned `1` on both throughout the run. Single-replica self-subscribed. No drift, no flap. Confirms D1 is wired correctly on single-replica too.

### 9.5 — Zero 404s in Battle logs during load (D1.5 active)

`docker logs --since 15m kombats-battle 2>&1 | grep " - 404"` returned 3 entries — **all 3 are `/metrics` scrape probes** (Battle uses OTel push, no Prometheus pull, so any `/metrics` GET returns 404 by design). Filtered: 0 SignalR-related 404s, 0 negotiate POSTs, 2103 successful WebSocket 101 upgrades. D1.5 working as designed at sustained single-replica load, identical to its multi-replica behavior in Run 3.

### 9.6 — `active_battles` peak = 5 (architect's Grafana observation)

Substantially less than Run 0's typical peak of ~6 (`CHAPTER_3_PLAN.md` §9). Within noise; consistent with single-replica steady-state of 6-7-second battles × ~9 matches/s ÷ 25 concurrent VUs. No split-brain to track here (single replica — the multi-replica observability artefact in `RUN_2_RESULTS.md` §8 / `RUN_3_RESULTS.md` §9.4 doesn't manifest).

## 10. Chapter 2.5 candidates — carried forward to chapter closer

| Candidate | First named | Run 4 status |
|---|---|---|
| `BattleRedisOptions.StateTtlAfterEnd` unwired — `battle:state:*` keys accumulate without TTL | `CLEANUP_WORKER_DIAGNOSIS.md`, `RUN_0_BASELINE.md` §6 | Still unwired. Post-Run-4 Redis state: 23,819 keys, 1046 `battle:state:*` (matches battle count). Below saturating-decay threshold; clean-teardown discipline carries us through Chapter 3. Same as prior runs. |
| `matchmaking.player_combat_profiles` SERIALIZABLE conflicts under load | `RUN_0_BASELINE.md` §6 | Not re-observed; lower priority than TTL. |
| `active_battles` per-replica split-brain | `RUN_2_RESULTS.md` §8 | Not exercised in Run 4 (single replica). Still a Ch4 candidate. |
| BFF queue-polling p95 dominates `total_ms` | `RUN_0_BASELINE.md` §6 | Confirmed again at 1.87 s (§10.3 above). Push-based match-found notification remains the architectural answer; orthogonal to backplane. |
| BFF-relay `_connections` map process-local | `SIGNALR_SURFACE_MAP.md` J.3 | Not stressed in Run 4 (BFF single-replica, only one outbound per frontend). Separate "BFF scale-out" chapter. |
| ~~Per-replica OTel emission discipline~~ → **OTLP exporter silent skip when `OpenTelemetry:OtlpEndpoint` is empty** | `RUN_3_RESULTS.md` §9.3 (renamed and reattributed in §9.3.b after D6 diagnostic) | **NEW candidate identified, repair applied at compose-chain level.** Source-level Ch4 candidate: harden `Kombats.Common/Kombats.Observability/KombatsObservabilityExtensions.cs` to emit a startup WARN log line when `OpenTelemetry:OtlpEndpoint` is empty (currently silent skip — that silence is what kept this latent across Chapters 1–3). |
| SignalR distributed-trace propagation | Plan §11 | Unchanged. Separate chapter. |

The chapter closer (`CHAPTER_3_REPORT.md`) is the place to consolidate these candidates into a Chapter 2.5 / Chapter 4 backlog.

## 11. What I did NOT do (constraints honored)

- **No `src/Kombats.*` changes anywhere in Run 4.** D1.5 is the same uncommitted edit in `BattleHubRelay.cs` it was at end of Run 3. No new source change of any kind for Run 4.
- **No `git commit` / `git push` / `git add`.** Architect commits the full chapter set after `CHAPTER_3_REPORT.md` is written.
- **No load test re-runs.** Analysis is exclusively against `iterations-2026-05-14--10-08-13.jsonl` + post-run live stack state.
- **No investigation of the Run 4 `battle_ms` +4 ms vs Run 3 difference.** Sampling-noise + per-replica processing asymmetry plausible; not chased.
- **No mitigation of the `active_battles` per-replica split-brain.** Same as prior runs. Ch4 candidate.
- **No re-running of pre-load smokes.** Already done as part of D6 repair (Phase A redo at `RUN_4_SETUP_LOG.md` §11.5).
- **No teardown after the load run.** Stack left running for architect review.
- **No edits to `docker-compose.yml`, `docker-compose.multi-replica.yml`, observability files, NBomber scenarios, or virtual player code.** Run 4 used the D6-corrected compose chain that includes `observability/docker-compose.observability.override.yml`; that file already existed in the repo and was just included in the chain. No file content was edited.
- **No source-level fix to the `KombatsObservabilityExtensions.cs` silent-skip behavior.** That's a Ch4 candidate (§10 above), out of Chapter 3 scope.
- **No Run 5.** Per `RUN_3_RESULTS.md` §11.5 architect's call. The remaining gap is documented (multi-replica-alone effect on `join_battle_ms` not directly measured), and its production relevance is bounded.

## 12. Updates to upstream artefacts

This Run 4 analysis produced two amendments to `RUN_3_RESULTS.md` (per C.6 in the analysis prompt, plus Run 4's own data-driven refinement of §9.6):

### 12.1 RUN_3_RESULTS.md §9.3 amendment — D6 observability finding

Body of §9.3 split into §9.3.a (preserved initial interpretation) + §9.3.b (corrected attribution after D6 diagnostic). The original "per-replica OTel SDK emission quirk" attribution is preserved as a hypothesis-later-refuted; the corrected attribution names the compose-chain `OpenTelemetry__OtlpEndpoint` missing-env bug as the actual root cause. The §54346fc4 UUID is reattributed to `kombats-battle-2` (not `kombats-battle` as initially inferred). Cross-reference to `RUN_4_SETUP_LOG.md` §11 added. The §10 Ch4 candidate row was renamed accordingly.

### 12.2 RUN_3_RESULTS.md §9.6 amendment — `battle_ms` attribution refined

Body of §9.6 split into §9.6.a (preserved initial "cross-replica delivery" attribution) + §9.6.b (corrected attribution after Run 4 single-replica control). The "cross-replica delivery" framing is refuted (Run 4 single-replica shows the same +21 ms inflation). The corrected attribution names the **backplane PUBSUB hop per group send, local to each Battle process, applied to all publishes regardless of replica count**. Quantifies the steady-state per-battle backplane overhead at ~+21 ms at this load shape — a number not previously known precisely (Run 1's measurement was distorted by bot-pool starvation; Run 3's was confounded with cross-replica delivery; only Run 4's single-replica + full-throughput configuration isolates it).

Both amendments preserve the chapter's iterative-modeling discipline: prior interpretations are visible as initial readings, refuted by later evidence, with the new evidence cited. This is the same character as the §3.1 → compound model (Run 2) → refined compound (Run 3 setup) → handshake-vs-publish (Run 3 results §3b) iteration the chapter has shown throughout.

No other artefacts modified. `RUN_3_SETUP_LOG.md`, `RUN_3_RESULTS.md` §5 (multi-replica execution evidence), `RUN_2_RESULTS.md`, `RUN_1_RESULTS.md`, `RUN_0_BASELINE.md`, `PHASE_2_REPORT.md`, `PHASE_3_D1_REPORT.md`, `RUN_1_BACKPLANE_OVERHEAD_ANALYSIS.md`, `CHAPTER_3_PLAN.md`, `SIGNALR_SURFACE_MAP.md` — all unchanged.

## 13. Plain-language summary (Russian-friendly, kernel for STORY chapter)

> Run 4 — финальный тест главы 3. Single-replica + backplane + skip-negotiation. Две задачи: (1) regression-check matchmaking — не задел ли backplane путь пейринга? и (2) bonus control — изолировать вклад D1.5 от вклада второй реплики в коллапс `join_battle_ms` от Run 1 к Run 3.
>
> Goal 1: pairing throughput 8.84 matches/s vs Run 0's 9.08 (−2.6%, в пределах ±5%). **Matchmaking чист, backplane его не задевает** — что и предсказывало code-reading (в matchmaking нет ни `AddStackExchangeRedis`, ни `IHubContext`, ни SignalR DI вообще). Этот вопрос закрыт.
>
> Goal 2: total_ms p50 = 1130.6 ms — **Run 0 territory** (1106 ms), не Run 1 territory (2182 ms). join_battle_ms p50 = 4.1 ms — тоже Run 0 territory. Это **Outcome A** из карты исходов в RUN_4_SETUP_LOG §7: **D1.5 ОДИН закрывает overhead, который Run 1 показывал. Вклад второй реплики в Run 3's join_battle_ms collapse — на уровне шума измерения (~−6 ms).** Это означает: production-fix Chapter 3 — это в основном D1.5 (BFF→Battle skip-negotiation), backplane (D1) необходим для Event B, но multi-replica деление handshake-нагрузки оказалось почти ненужным на single-replica с D1.5 уже включённым.
>
> Сюрприз Run 4: `battle_ms` p50 = 60.4 ms — даже выше чем Run 3's 56.4 ms, на single-replica где cross-replica delivery физически невозможна. **Это опровергает RUN_3_RESULTS §9.6 attribution** — overhead не от cross-replica delivery, а от **backplane PUBSUB hop per group send, локально на каждой Battle реплике, регардлесс of replica count**. Microsoft.AspNetCore.SignalR.StackExchangeRedis намеренно не делает local-only short-circuit для group sends (источник проверен в RUN_1 overhead analysis §3.1). +21 ms на типичный 5-7-turn бой — это +0.1% wall-clock в production на player-speed (~18s бои). Production-irrelevant, но впервые количественно измерено — Run 1 этот overhead не показал из-за starvation (бои были короткими), Run 3 не мог отделить от cross-replica delivery, только Run 4 single-replica + full throughput даёт чистое измерение.
>
> Плюс D6 fix перед Phase B: оказалось, что весь Chapter 3 (Run 0/1/2/3) запускался с broken OTel pipeline — `observability/docker-compose.observability.override.yml` никогда не был в compose chain, и `Kombats.Common/Kombats.Observability/KombatsObservabilityExtensions.cs:37` тихо пропускал attach OTLP exporter когда `OpenTelemetry:OtlpEndpoint` пуст. Run 3's "per-replica OTel emission gap" (§9.3) был НЕ per-replica SDK quirk — это был compose-chain bug. UUID `54346fc4-...` который Run 3 §9.3 атрибутировал на `kombats-battle` на самом деле был `kombats-battle-2`'s (у которой OTel env был inlined в multi-replica.yml). Repair: Level-3 down -v + up с corrected chain, документировано в RUN_4_SETUP_LOG §11. Run 4 — первый chapter run с полностью рабочим observability pipeline.
>
> Итог главы — пять итераций модели за четыре measurement runs: §3.1 (per-event group send) → compound (Event A × Event B, Run 2) → refined (Event A — layer below backplane, Run 3 setup) → handshake-vs-publish (Run 3 results §3b — saturation была at setup not at battle-phase) → D1.5-isolation (Run 4 — D1.5 alone closes setup overhead, backplane PUBSUB hop is the real steady-state cost). Каждый шаг — измерение и refinement, не догадка. Глава теперь готова к CHAPTER_3_REPORT.md.

---

## Artefact locations

- **This file:** `tests/Kombats.LoadTests/RUN_4_RESULTS.md` (read first).
- **Setup evidence + D6 repair:** `tests/Kombats.LoadTests/RUN_4_SETUP_LOG.md` — §1–10 pre-load Phase A, §11 D6 mid-run observability-pipeline diagnostic and repair.
- **Iteration log:** `tests/Kombats.LoadTests/iteration-logs/iterations-2026-05-14--10-08-13.jsonl`.
- **Comparison sources:**
  - `RUN_0_BASELINE.md` §3 — primary baseline for Goal 1 (pairing throughput) and Goal 2 (single-replica `total_ms` / `join_battle_ms` / `battle_ms`).
  - `RUN_1_RESULTS.md` §3 — Goal 2 critical control (+backplane, no-D1.5).
  - `RUN_3_RESULTS.md` §3 — reference column (multi-replica with dual fix); also amended at §9.3 + §9.6 per §12 above.
  - `RUN_2_RESULTS.md` — failure proof, no direct Run 4 comparison.
- **Mechanism sources:**
  - `RUN_1_BACKPLANE_OVERHEAD_ANALYSIS.md` §3.1 — SendGroupAsync has no local-only short-circuit (the mechanism behind §7 above).
  - `Kombats.Common/Kombats.Observability/KombatsObservabilityExtensions.cs:37` — the silent-skip OTLP attach behavior identified in D6.
  - `observability/docker-compose.observability.override.yml` — the OTLP-endpoint env file that needs to be in every chapter-run compose chain (the D6 finding).

**Chapter 3 measurement runs complete. Awaiting architect review of Run 4 results + Run 3 §9.3 + §9.6 amendments. `CHAPTER_3_REPORT.md` is the chapter closer.**
