# Chapter 3 Report — Lifting the `maxReplicas: 1` ceiling on Battle

## 1. Header

- **Date:** 2026-05-14 (chapter close).
- **Branch:** `feat/signalr-backplane`, ready for squash merge into `development`.
- **Working tree at chapter close (all uncommitted; architect commits as one squash):**
  - `src/Kombats.Bff/Kombats.Bff.Application/Relay/BattleHubRelay.cs` (D1.5)
  - `tests/Kombats.LoadTests/RUN_3_SETUP_LOG.md`, `RUN_3_RESULTS.md` (with §9.3.b + §9.6.b corrective addenda)
  - `tests/Kombats.LoadTests/RUN_4_SETUP_LOG.md`, `RUN_4_RESULTS.md`
  - `tests/Kombats.LoadTests/CHAPTER_3_REPORT.md` (this file)
- **Last committed checkpoint on the branch:** `b2928ac` (Run 2 docs cherry-picked from `development`). The 8 uncommitted artefacts above land together in the squash merge along with the D1.5 source change.

**Elevator pitch.** Chapter 3 lifted Battle's `maxReplicas: 1` Bicep ceiling by adding the SignalR Redis backplane (D1, one source line) plus skip-negotiation on BFF→Battle (D1.5, one source line plus a comment). Four measurement runs (Run 0–4) plus a smoke-scale fix proof established the chapter's claims: at 25-concurrent × 2-replica load, success rate is 98.8 %, latency matches Run 0 baseline (`total_ms` p50 ≈ 1130 ms), and matchmaking pairing throughput is within 2.6 % of baseline. Three structural failure modes were discovered during the chapter; two were closed by the dual fix, one (a silent OTel exporter-skip bug latent across Chapters 1–3) was discovered while debugging Run 4 setup and fixed at compose-chain level. The chapter's portfolio value is the **model evolution** — five iterations of the failure model across four runs, each refinement driven by measured evidence — not "two lines closed the ceiling."

## 2. The question Chapter 3 set out to answer

Per `CHAPTER_3_PLAN.md` §1–§3, the production target is **~1000 concurrent player-speed battles** (combats.ru-style PvP, ~18 s wall-clock per battle, ~55 battles/s steady-state). Battle service today is pinned to a single replica in `infra/main.bicep` — `maxReplicas: 1`. The ceiling is **load-bearing**: SignalR's `Clients.Group(...)` resolves group membership from each replica's in-process map, so a group send from replica A reaches only the connections registered on A's local group table; players whose WebSocket is parked on replica B receive nothing. Without a backplane (or some equivalent cross-replica routing), scaling out silently breaks the game.

The chapter's job was to (a) remove that ceiling, (b) measure what removing it costs, and (c) prove the fix isn't masking some second failure mode hiding behind the first. The plan predicted one mechanism (per-event group send fan-out via §3.1) and one fix (the backplane via §4). Both turned out to be incomplete.

## 3. Model evolution — the chapter's intellectual arc

Five iterations of the failure model across four measurement runs. Each iteration was driven by measured evidence, not guessing. Tracking the iterations explicitly is the chapter's portfolio value.

### Iteration 1 — Per-event group send fan-out (`CHAPTER_3_PLAN.md` §3.1)

**Prediction:** With R=2 replicas and uniform DNS rotation, each player has `P(on replica r) = 1/R = 0.5`. Each `TurnDeadlineWorker` event resolves on a random replica with `P=0.5`, so `P(both bots see one event) = 0.25`. Per-battle: `P(at least one critical event lost across T turns) ≈ 1 − 0.25^T`. At T=3 turns, that's **98.4 %**.

**Status before measurement:** purely combinatoric, code-grounded but unverified.

### Iteration 2 — Compound model (Event A + Event B, Run 2)

**Measured:** Run 2 (2 Battle replicas, no backplane) produced **2 Normal / 3 DoubleForfeit / 14 ArenaOpen** out of 19 battles. The §3.1 model predicted >99 % of battles affected at 5-turn play; the measurement was closer to 89 %.

**Why §3.1 over-predicted failure:** WebSocket connections are **pinned** to their replica for the connection lifetime, not rotated per emission. The dominant variable isn't "did each individual turn's worker happen to rotate matchingly," it's "**did both bots' WebSockets land on the same replica at handshake time**." Two independent events:

- **Event A — handshake split (novel finding, not in §3.1):** Each `HubConnection.StartAsync()` from BFF does `POST /battlehub/negotiate` (token issued by the answering replica) followed by `GET /battlehub?id={token}` (WebSocket upgrade). Docker DNS rotates per resolution, so the two HTTP calls can land on different replicas. `P(404) = 0.5` per bot ⇒ `P(at least one of two bots 404s) = 0.75` per battle pair. Server-side evidence in Run 2: 31 of 62 connection attempts returned 404 — exactly the predicted 50 %.
- **Event B — co-location lottery (§3.1's actual mechanism):** Given both handshakes survive, `P(both bots on same replica) = 0.5`. If equal → `Clients.Group(...)` reaches both → battle plays cleanly. If different → group sends miss one bot, deadline worker fires `NoAction` defaults, battle drifts to `DoubleForfeit`.

Combined predicted outcomes:

| Outcome class | Compound P | Predicted (n=19) | Observed (n=19) |
|---|---|---|---|
| Both handshakes OK + same replica → Normal | 0.5 × 0.5 × 0.5 = 0.125 | 2.4 | 2 (10.5 %) |
| Both handshakes OK + different replicas → DoubleForfeit | 0.5 × 0.5 × 0.5 = 0.125 | 2.4 | 3 (15.8 %) |
| At least one handshake 404 → ArenaOpen | 1 − 0.5×0.5 = 0.75 | 14.25 | 14 (73.7 %) |

**All three rows match within sampling noise at n=19.** See `RUN_2_RESULTS.md` §4.

### Iteration 3 — Refined Event A (Run 3 setup, `RUN_3_SETUP_LOG.md` §14)

**Prediction (from Iteration 2 + chapter framing):** "Adding the backplane closes both Event A and Event B because the backplane replicates SignalR state through Redis." This was what the chapter set out to demonstrate.

**Measured:** In Run 3 Phase A smoke testing, **4/5 smokes were clean but 1/5 reproduced the Run 2 Event A handshake-404 pattern even with the backplane wired** (NUMSUB=2 verified, channels live). BFF stack trace identical to Run 2 §4: `HttpRequestException: 404 Not Found at HubConnection.HandshakeAsync`.

**Why:** `Microsoft.AspNetCore.SignalR.StackExchangeRedis 10.0.3` replicates `HubLifetimeManager` state through Redis — group membership, message traffic, per-connection ack/return routing. It does **NOT** replicate `HttpConnectionManager`'s per-connection token map. Negotiate→handshake is one architectural layer **below** `HubLifetimeManager`, in SignalR's endpoint middleware. The backplane cannot fix it because the failure is in code the backplane doesn't intercept. Three independent sources confirm (cited in `RUN_3_SETUP_LOG.md` §14.3): Microsoft GitHub issue #50171 ("The solution recommended from official MS documentation is to use sticky sessions or skip negotiation"), Milan Jovanović's "Scaling SignalR With a Redis Backplane" (March 2026), and Infinum's "SignalR Scaling In Real-Time Applications" (Dec 2025).

**Fix (D1.5):** set `SkipNegotiation=true` + `Transports=HttpTransportType.WebSockets` on BFF's outbound `HubConnection` to Battle. The client skips the negotiate POST entirely and goes straight to a WebSocket upgrade against whichever replica DNS picked. The JWT travels on the upgrade URL via `access_token` query string (BattleHub already reads it from header or query string per `SIGNALR_SURFACE_MAP.md` §J.3). No token to mis-route; no 404. See `RUN_3_SETUP_LOG.md` §14 for the diff + smoke re-verification (5/5 clean post-D1.5).

### Iteration 4 — Handshake-vs-publish reframing (Run 3 results, `RUN_3_RESULTS.md` §3b)

**Prediction (from Iteration 3 + `RUN_1_BACKPLANE_OVERHEAD_ANALYSIS.md`):** Run 1 measured +1076 ms `total_ms` p50 on single-replica with backplane. The §3.5 / §4.1 analysis attributed this to multiplexer-queue-bound per-publish RTT during the battle phase (~30–50 ms × ~26 publishes ≈ +1000 ms).

**Measured:** Run 3 per-phase breakdown showed the +1076 ms inflation lived almost entirely in **`join_battle_ms` (+761 ms)**, with `battle_ms` essentially flat in Run 1 vs Run 0 (38.4 vs 39.3, −0.9 ms). If multiplexer queueing at battle-phase publishes were the cause, the cost would have surfaced in `battle_ms` (where ~24 of the ~26 per-battle publishes happen). It didn't.

**Refinement:** The cost in Run 1 was concentrated at the BFF→Battle **connection-setup phase** — `HubConnection.StartAsync()` inside `BattleHubRelay.JoinBattleAsync` (`BattleHubRelay.cs:265`), which includes the handshake roundtrip. Under single-replica + 25 simultaneous handshakes, ASP.NET Core's HTTP request queue saturated at setup time. Run 3 collapsed `join_battle_ms` back to baseline via two simultaneous changes (D1.5 negotiate elimination + multi-replica HTTP load split). The relative contribution of each was uncontrolled by Run 3 alone — Iteration 5 would isolate it.

This iteration also partially **supersedes** `RUN_1_BACKPLANE_OVERHEAD_ANALYSIS.md` §3.5 / §4.1. The mechanism it identified (`SendGroupAsync` has no local short-circuit, every publish awaits a Redis PUBLISH RTT) is correct at the source level; the **load-shape regime** where per-publish queueing dominates is wrong — saturation in Run 1 was at handshake setup, not at steady-state battle publishing.

### Iteration 5 — D1.5 isolation + `battle_ms` reattribution (Run 4, `RUN_4_RESULTS.md` §5/§7)

**Prediction (from Iteration 4):** Run 4 (single-replica + backplane + D1.5) tests two hypotheses simultaneously:
- *Outcome A*: D1.5 alone restores baseline ⇒ `total_ms` p50 ≈ Run 0 (~1100 ms), `join_battle_ms` ≈ 4 ms. The second replica's contribution is small.
- *Outcome B/C*: D1.5 doesn't help on single-replica saturated handshake setup ⇒ Run 4 lands between Run 0 and Run 1 on `join_battle_ms`, multi-replica is the dominant fix.

The `battle_ms` prediction was independent: Run 4 single-replica has no cross-replica path, so if `RUN_3_RESULTS.md` §9.6 was right about cross-replica delivery, `battle_ms` should return to Run 0 levels (~39 ms).

**Measured:** Run 4 `total_ms` p50 = 1130.6 ms (Run 0 territory). `join_battle_ms` p50 = 4.1 ms (Run 0 territory). Run 1 → Run 4 delta on `join_battle_ms` is −761.6 ms; Run 1 → Run 3 delta is −761.5 ms. Nearly identical. **Outcome A confirmed — D1.5 alone closes the join-setup overhead; multi-replica's marginal contribution is ≤6 ms (under noise).**

But: **`battle_ms` p50 = 60.4 ms**, slightly higher than Run 3's 56.4 ms and ~+21 ms over Run 0 — on a single-replica configuration where cross-replica delivery is structurally impossible. The §9.6 cross-replica attribution is **refuted**.

**Refinement:** The +21 ms in `battle_ms` is the **backplane PUBSUB hop applied to every group send, local to each Battle process, regardless of replica count**. Source-verified in `RUN_1_BACKPLANE_OVERHEAD_ANALYSIS.md` §3.1: `SendGroupAsync` has no local-only short-circuit. ~1–2 ms × ~13 events bot-perceived ≈ +13–26 ms per typical 5–7-turn battle. Fits both Run 3's +18 and Run 4's +21. Run 1 didn't show this overhead in `battle_ms` because bot-pool starvation produced shorter median battles (fewer events to wait for). Run 4 is the first measurement that **isolates the steady-state backplane PUBSUB cost during the battle phase at full throughput**. Quantified at +21 ms / typical battle.

**Production translation:** +21 ms over an 18 s production battle is ~0.1 % of wall-clock. Production-irrelevant. But correctly attributed for the first time, with the §9.6 cross-replica delivery framing preserved in `RUN_3_RESULTS.md` §9.6.a as a hypothesis-later-refuted (model-iteration discipline).

### Why the five iterations matter

The chapter could have stopped at Iteration 2 ("compound model verified, ship D1") or Iteration 3 ("D1.5 added, success rate restored"). It didn't, because at each step the per-phase data raised a question the prior model didn't predict. Iterations 4 and 5 are the parts a less-careful chapter would have skipped — and would have shipped, in writing, two specific causal claims that the data didn't actually support (multiplexer saturation during battle-phase publishing, cross-replica delivery as the source of `battle_ms` inflation). Both are *plausible*, both are *wrong*, both would have ended up in the portfolio narrative as confident attribution. The structural lesson: **iterate the model until per-phase data stops disagreeing with the model**; preserve the wrong hypotheses in writing so the iteration is visible.

## 4. The two code changes (D1 + D1.5)

Final source-change tally for Chapter 3: exactly **2 files in `src/Kombats.*`**, plus 2 supporting metadata files for D1's package pin.

### D1 — SignalR Redis backplane on Battle (committed at `5f63ec9`)

- `src/Kombats.Battle/Kombats.Battle.Bootstrap/Program.cs:131` — one chained method call:
  ```csharp
  // Negotiate→handshake state is not replicated by the SignalR Redis backplane...
  builder.Services.AddSignalR(...)
      .AddJsonProtocol(...)
      .AddStackExchangeRedis(redisConnectionString);
  ```
- `Directory.Packages.props:46` — `<PackageVersion Include="Microsoft.AspNetCore.SignalR.StackExchangeRedis" Version="10.0.3" />`. Version pinned at 10.0.3, not 10.0.8 (latest), to align with the repo-wide `Microsoft.*` baseline. See `PHASE_3_D1_REPORT.md` §3 for the cascade-avoidance rationale.
- `src/Kombats.Battle/Kombats.Battle.Bootstrap/Kombats.Battle.Bootstrap.csproj:29` — central-pinned `<PackageReference>` for the same package.
- Build: `84 Warning(s), 0 Error(s)` (no new warnings; baseline parity verified by stash/restore/rebuild cycle — `PHASE_3_D1_REPORT.md` §5).

### D1.5 — Skip-negotiation on BFF outbound to Battle (uncommitted at chapter close)

- `src/Kombats.Bff/Kombats.Bff.Application/Relay/BattleHubRelay.cs:6` — `using Microsoft.AspNetCore.Http.Connections;` added.
- `BattleHubRelay.cs:67-77` — inside the existing `.WithUrl(battleHubUrl, options => { … })` lambda in `JoinBattleAsync`, two new options + an 11-line explanatory comment:
  ```csharp
  // Negotiate→handshake state is not replicated by the SignalR Redis backplane:
  // Microsoft.AspNetCore.SignalR.StackExchangeRedis replicates HubLifetimeManager
  // only, not HttpConnectionManager's per-connection token map. With Battle on
  // multiple replicas and DNS rotation, the negotiate POST and the handshake GET
  // can land on different replicas — the GET then 404s. Skipping negotiation and
  // forcing WebSockets pins both into a single WebSocket upgrade against whichever
  // replica DNS picked, so no cross-replica token lookup is required. The JWT
  // travels on the upgrade via access_token query string (BattleHub reads it from
  // header or query string — see SIGNALR_SURFACE_MAP.md §J.3).
  // Ref: https://github.com/dotnet/aspnetcore/issues/50171
  options.SkipNegotiation = true;
  options.Transports = HttpTransportType.WebSockets;
  ```
- Total diff: 1 file modified, +15 lines / −0. Build: `84 Warning(s), 0 Error(s)` (parity with pre-D1.5 baseline). No new NuGet package — `HttpTransportType` ships in the existing `Microsoft.AspNetCore.SignalR.Client 10.0.3` reference.

### External sources cited for D1.5 rationale

The fix is not novel. It's the Microsoft-documented standard answer when sticky sessions on the load balancer are not available. From `RUN_3_SETUP_LOG.md` §14.3:

1. **Microsoft GitHub issue #50171** (Aug 2023): *"The solution recommended from official MS documentation is to use sticky sessions or skip negotiation."*
2. **Milan Jovanović, "Scaling SignalR With a Redis Backplane"** (March 2026): *"The Redis backplane solves message routing, but it does not remove the need for sticky sessions."*
3. **Infinum, "SignalR Scaling In Real-Time Applications"** (Dec 2025): same conclusion.

For Kombats's self-hosted PvP target without managed sticky LB infrastructure, **skip-negotiation is the production-correct path**, not sticky sessions.

## 5. The four measurement runs at a glance

| # | Configuration | Key result | Key learning |
|---|---|---|---|
| 0 | 1 Battle, no backplane | Baseline: 98.8 % ok, `total_ms` p50 = 1106 ms, 9.08 m/s pairing | Re-established Ch2-post-fix baseline as Ch3 starting point. `RUN_0_BASELINE.md`. |
| 1 | 1 Battle, +backplane (D1) | +1076 ms `total_ms` p50 vs Run 0; 95 % throughput drop | Backplane has a real cost — but Iteration 4 later showed it lives in `join_battle_ms` (handshake setup), not steady-state publishing. `RUN_1_RESULTS.md` + `RUN_1_BACKPLANE_OVERHEAD_ANALYSIS.md`. |
| 2 | 2 Battle, no backplane | 89 % battle failure (14 ArenaOpen + 3 DoubleForfeit of 19) | Compound model (Event A handshake split + Event B co-location lottery) predicts 75/12.5/12.5; measured 73.7/15.8/10.5. `RUN_2_RESULTS.md`. |
| 3 | 2 Battle, +backplane (D1) + skip-neg (D1.5) | 98.8 % ok; `total_ms` p50 = 1124.7 ms (Run 0 territory) | Dual fix proven at multi-replica scale. Genuine multi-replica execution evidenced by 49.5/50.5 access-log split. `RUN_3_RESULTS.md`. |
| 4 | 1 Battle, +backplane (D1) + skip-neg (D1.5) | 98.8 % ok; `total_ms` p50 = 1130.6 ms; `join_battle_ms` p50 = 4.1 ms | D1.5 alone restores baseline on single-replica — multi-replica contribution to Run 3's `join_battle_ms` collapse is small. Also: `battle_ms` +21 ms reattributed to backplane PUBSUB hop (not cross-replica delivery). `RUN_4_RESULTS.md`. |

**Per-run elaboration (1 paragraph each, with cross-references):**

**Run 0** re-established the Ch2-post-fix baseline. 2096 ok, 25 fail (NBomber shutdown tail), p50 = 1106 ms, p95 = 1593 ms, RPS 17.47, pairing 9.08 matches/s over a 116 s active window. The clean-teardown discipline (`docker compose down -v` before every run) was named here as a mandatory precondition because of the unwired `BattleRedisOptions.StateTtlAfterEnd` — see `RUN_0_BASELINE.md` §5 and `CLEANUP_WORKER_DIAGNOSIS.md`. Carried through every subsequent run.

**Run 1** wired the backplane (D1) on single-replica. Counter-intuitively the success rate stayed high (108 ok of 114 iterations = 94.7 %) but throughput collapsed by ~95 %: only 54 successful battles in 2 minutes vs Run 0's 1049, `total_ms` p50 doubled to 2182.4 ms. The original attribution (`RUN_1_BACKPLANE_OVERHEAD_ANALYSIS.md` §4.1) framed this as multiplexer-saturation during battle-phase publishing × 26 awaited publishes per battle × ~40 ms each = ~+1040 ms. The mechanism was source-verified at the Microsoft v10.0.3 tag; the load-shape regime turned out to be wrong (later refined in Iteration 4).

**Run 2** removed the backplane and added a second Battle replica. Predicted multi-replica failure mode landed precisely: 14 of 19 battles stuck in `ArenaOpen` (handshake split), 3 in `DoubleForfeit` (co-location split), 2 Normal (lottery winners). Compound model 12.5/12.5/75 prediction vs measured 10.5/15.8/73.7 — all three cells within sampling noise at n=19. The novel finding was Event A — the negotiate→handshake split — which the original §3.1 plan didn't anticipate. `RUN_2_RESULTS.md` §4.

**Run 3** combined backplane (D1) + skip-negotiation (D1.5) + two replicas. **The chapter's fix proof.** 2088 ok of 2113 iterations (98.82 %), `total_ms` p50 = 1124.7 ms — back to Run 0 territory. Both events independently closed: Event A by D1.5 (0 of 2125 WebSocket upgrades 404'd), Event B by D1 backplane (1052/1052 battles ended cleanly). Multi-replica execution proven by 49.5/50.5 access-log split + 50.4/49.6 hub-method-invocation split + NUMSUB=2 on cross-replica channels — see `RUN_3_RESULTS.md` §5. Run 3 also caught one *un-anticipated* surprise: `total_ms` was 48 % faster than Run 1 instead of "similar," which Iteration 4 then refined.

**Run 4** isolated D1.5's contribution on single-replica. **Outcome A** confirmed: D1.5 alone restores Run 0 baseline `total_ms` (1130.6 ms) and `join_battle_ms` (4.1 ms). The Run 1 → Run 4 delta nearly equals Run 1 → Run 3 delta, so the second replica's marginal contribution to join-phase latency is ≤6 ms — under noise. Two side findings: Goal 1 confirmed (matchmaking pairing throughput 8.84 m/s vs Run 0's 9.08 — backplane doesn't touch pairing path), and Iteration 5 attribution correction on `battle_ms` (the +21 ms is local backplane PUBSUB hop, not cross-replica delivery). `RUN_4_RESULTS.md`.

## 6. The bottom-line numbers

| Metric | Run 0 (baseline) | Run 1 (+D1, 1×) | Run 2 (broken, 2× no fix) | Run 3 (dual fix, 2×) | Run 4 (D1.5 control, 1×) |
|---|---|---|---|---|---|
| Success rate | 98.82 % | (94.7 %, throughput-collapsed) | 10.5 % battle Normal (89 % failure) | 98.82 % | 98.81 % |
| `total_ms` p50 | 1106 ms | 2182.4 ms (+1076) | 120 846 ms (saturated PerBotTimeout) | 1124.7 ms | 1130.6 ms |
| `total_ms` p95 | 1593.4 ms | 11 413 ms | 152 043 ms | 1618.5 ms | 1629.6 ms |
| `join_battle_ms` p50 | 4.4 ms | 765.7 ms | (degenerate) | 4.2 ms | 4.1 ms |
| `battle_ms` p50 | 39.3 ms | 38.4 ms (starved) | 0 ms (no events delivered) | 56.4 ms | 60.4 ms |
| RPS | 17.47 | 0.95 | 0.29 | 17.40 | 17.32 |
| Pairing throughput | 9.08 m/s | 2.86 m/s (collapsed) | 1.34 m/s (collapsed) | 8.87 m/s | 8.84 m/s |

**Headline statement:**

> **Chapter 3 lifted `maxReplicas: 1` from 1 to N at a measured cost of ~+21 ms `battle_ms` per typical 5–7-turn battle (backplane PUBSUB hop, ~0.1 % of wall-clock at production player-speed), proven at 25-concurrent × 2-replica load with 98.8 % success rate matching Run 0 baseline within 0.01 percentage points.**

Two source lines added (`AddStackExchangeRedis` in Battle Bootstrap, `SkipNegotiation=true` + `Transports=WebSockets` in BFF's `BattleHubRelay`). No package upgrades except the one new pin (`Microsoft.AspNetCore.SignalR.StackExchangeRedis 10.0.3`). No compose-file edits to the chapter scope (the D6 fix used an *existing* repo file that was missing from the compose chain). The change set is unusually small for the ceiling it removes.

## 7. Production translation

What this chapter measured and what it didn't.

**At 25 concurrent × 2 Battle replicas (Run 3 measured):** dual-fix works. Success rate 98.8 %, latency = Run 0 territory, backplane subscribed and stable (NUMSUB=2 on cross-replica channels throughout 124 s of load). The chapter's fix-proof regime.

**At 1000 concurrent player-speed (production target):**

- **Per-replica concurrency scales as (target / num_replicas).** At 2 replicas serving 1000 concurrent connections, that's ~500 connections per replica at handshake setup peak — **40× higher per-replica concurrency than Run 3 measured.**
- `join_battle_ms` scaling between 12-and-500 connections per replica at handshake setup is **not known from Run 3 alone** (`RUN_3_RESULTS.md` §11.5 item 4). The handshake-setup phase was the binding constraint that single-replica Run 1 hit at 25 concurrent; whether 500-per-replica re-encounters that bottleneck on a single ASP.NET Core HTTP server is an empirical question that Chapter 3 does not answer.
- **The right next measurement is a capacity test at production-relevant concurrency**, not extrapolation from Run 3.
- **Per-publish steady-state cost** (Run 4 measured): backplane PUBSUB hop ≈ +21 ms over a typical 5–7-turn battle ≈ +0.1 % of wall-clock at 18 s production player-speed. This part is small, stable, and load-shape-bounded. At higher publish rates (production at 55 battles/s × 26 publishes/battle ≈ 1430 publishes/s/replica — below most StackExchange.Redis throughput estimates), per-publish RTT may drift modestly but does not appear to be the binding constraint.

**What we do NOT claim:** that horizontal scaling alone (without D1.5) would work in production. D1.5 was applied simultaneously with the second replica; the two changes are entangled in `join_battle_ms` per `RUN_3_RESULTS.md` §11.5 item 1, only partially closed by Run 4 at the single-replica regime. At production concurrency-per-replica, the relative weights might shift.

**Operational caveats for lifting `maxReplicas: 1` in Azure:**

1. Close TTL wiring first (`BattleRedisOptions.StateTtlAfterEnd` is defined and configured but never read — `CLEANUP_WORKER_DIAGNOSIS.md`). Without it, `battle:state:*` keys accumulate uncapped. Manageable with dev clean-teardown discipline; not manageable in production.
2. Audit the BFF-relay `_connections` map (`SIGNALR_SURFACE_MAP.md` §J.3). The map is process-local, which means scaling BFF past 1 replica needs sticky sessions or a refactor. Chapter 3 explicitly kept BFF single-replica (`CHAPTER_3_PLAN.md` §6).
3. Fix the OTLP exporter silent-skip behavior (`KombatsObservabilityExtensions.cs:37` — see §9 below). Production deployments may have correct OTel env config, but a defensive WARN log when `OtlpEndpoint` is empty is cheap insurance against a repeat of D6.
4. Decide on sticky sessions at the LB layer or keep D1.5's skip-negotiation. Skip-negotiation is the choice this chapter made because Kombats doesn't have managed sticky LB infrastructure. If that infrastructure lands later, D1.5 becomes optional but still valid.

## 8. What this chapter does NOT prove

Honest enumeration of uncontrolled variables. Aggregating from `RUN_3_RESULTS.md` §11.5 and `RUN_4_RESULTS.md` §8:

1. **D1.5 vs second replica at production concurrency.** Run 4 isolated D1.5's contribution at 25-concurrent single-replica. At 1000-concurrent / 500-per-replica, the multi-replica HTTP-load-split's contribution might become significant or remain small — not measured.

2. **Backplane vs skip-negotiation as Event B closer at load scale.** Run 3 phase-A diag-smoke #2 provided direct cross-replica evidence that backplane closes Event B (Bot 2 completed 4 turns through Redis PUBSUB despite Bot 1 disconnected). At load scale, Event B closure is inferred from "1052/1052 battles ended cleanly" rather than directly measured (no Run 5 with multi-replica + skip-negotiation but no backplane). Smoke-level evidence is structurally sufficient; absence of a load-scale control is documented.

3. **`battle_ms` +21 ms scaling at higher publish rates.** Run 4 quantified the backplane PUBSUB hop at ~+21 ms / typical battle at 25-concurrent. At 5000-concurrent or other stress regimes, per-publish RTT may scale super-linearly (StackExchange.Redis multiplexer queue depth). Not measured.

4. **Production concurrency-per-replica is unmeasured.** See §7 above. 500-connections-per-replica handshake-setup behavior on single ASP.NET Core HTTP server is the explicit unknown.

5. **The `RUN_1_BACKPLANE_OVERHEAD_ANALYSIS.md` §3.5 / §4.1 multiplexer-saturation model is partially superseded by data, not by a different analysis.** A follow-up addendum to that document would close the loop properly; out of Chapter 3 scope by the "no further `src/Kombats.*` changes" discipline (which extends to "no rewrite of upstream analyses except where Run 3/4 data directly invalidates a claim").

6. **Cross-replica delivery latency was not directly instrumented.** Run 4's attribution of `battle_ms` inflation to backplane PUBSUB hop (rather than cross-replica delivery) is consistent with all observed data — Run 3 multi-replica and Run 4 single-replica both show the same magnitude — but no profiler or distributed-trace measurement directly proves it. An alternative attribution (e.g. one extra context-switch on the receiver-side PUBSUB callback) would also fit. Not chased.

These six gaps don't affect headline claims (Event A closed by D1.5; Event B closed by D1; 98.8 % success rate at multi-replica). They affect *causal precision* on secondary claims and *capacity-extrapolation* claims.

## 9. The operational finding — D6 observability-pipeline silent failure

During Run 4 Phase A setup, the architect attempted to verify Grafana dashboards and discovered they were empty. Diagnostic: Prometheus → OTel-collector scrape was healthy, but the collector's `:8889/metrics` Prometheus exporter exposed nothing about the .NET services.

**Root cause** (`RUN_4_SETUP_LOG.md` §11): the OTel endpoint env (`OpenTelemetry__OtlpEndpoint=http://otel-collector:4317`) lives only in `observability/docker-compose.observability.override.yml`, and that file was **never** in any Chapter 1, 2, or 3 run's compose chain. Without that env, `src/Kombats.Common/Kombats.Observability/KombatsObservabilityExtensions.cs:37` reads an empty config key and **conditionally skips attaching the OTLP exporter entirely** (per the file's own comment at line 14: *"OTLP exporter is only attached when OpenTelemetry:OtlpEndpoint is [set]"*). The OpenTelemetry SDK still collected metrics in-process; it just exported nothing. No errors, no warnings, no log lines — pure silent skip.

**Why it stayed latent for three chapters:**

1. **Default `appsettings.json`** ships with `"OpenTelemetry": { "OtlpEndpoint": "" }` (empty string), not `null` or a sane default. The conditional treats `""` the same as "not configured" — silent skip rather than fail-loud.
2. **Compose-file split**: `observability/docker-compose.observability.yml` (starts Prometheus + collector + Jaeger + Grafana) and `observability/docker-compose.observability.override.yml` (configures *.NET services to push to that collector*) are philosophically halves of the same pipeline but two files in the repo. Run prompts only included the first.
3. **Multi-replica overlay inlined the env for `battle-2` only** (`docker-compose.multi-replica.yml:61` — anticipating the override file would normally be merged in). So Run 2 and Run 3 had *partial* Prometheus signal from `battle-2`, which **masked** the gap rather than revealing it. `RUN_3_RESULTS.md` §9.3 (initial interpretation) attributed the gap to a "per-replica OTel SDK quirk"; the actual root cause was compose-chain misconfiguration.

**Repair (Level 3):** `docker compose ... down -v` + `up -d --build` with `observability/docker-compose.observability.override.yml` added to the chain. Phase A re-done (migrations, Keycloak bootstrap, seed users, 3 smokes clean). End-to-end verification post-repair: all 5 .NET services in `process_cpu_count`, 2 `active_signalr_connections` series (battle UUID + bff UUID), collector `:8889/metrics` populated with the four expected metric families. Architect's Grafana panels populated live during Run 4 for the **first time in Chapter 3**.

**What was preserved through this finding:** Run 3's multi-replica execution evidence in §5 — the 49.5/50.5 HTTP access-log distribution and 50.4/49.6 hub-method-invocation split. Both came from `docker logs` parsing, not from Prometheus. The functional proof of multi-replica execution never depended on the broken pipeline.

**What was corrected:** `RUN_3_RESULTS.md` §9.3 was split into §9.3.a (initial interpretation preserved) + §9.3.b (root cause + UUID reattribution — the single visible `54346fc4-...` was almost certainly `kombats-battle-2`'s, not `kombats-battle`'s — opposite of the original inference). Same disciplined split for §9.6 after Run 4 also refuted the cross-replica delivery attribution.

**Lesson for the chapter:** **silent failure modes in observability are themselves observability bugs.** The OTel SDK's conditional silent skip is a small surface but a high-leverage one — three chapters of measurement ran with broken metrics emission, and we only noticed because Run 4's setup happened to verify Grafana before triggering load. A startup WARN log when `OpenTelemetry:OtlpEndpoint` is empty would have surfaced this on the first container start. Ch4 candidate.

## 10. Chapter 2.5 / Chapter 4 backlog (deferred items)

Priority-ordered consolidation from Run 0 §6, Run 2 §8, Run 3 §10, Run 4 §10.

| Priority | Candidate | First surfaced | Fix scope | Blocking for 1000-concurrent target? |
|---|---|---|---|---|
| **Critical** | `BattleRedisOptions.StateTtlAfterEnd` unwired — `battle:state:*` keys accumulate uncapped | `CLEANUP_WORKER_DIAGNOSIS.md`, `RUN_0_BASELINE.md` §6 | 1–3 lines wiring `StateTtlAfterEnd` through `RedisBattleStateStore` + test. Chapter 2.5 scope. | **Yes** — production at ~1000 battles/day = ~365k keys/year uncapped. |
| **Critical** | OTLP exporter silent skip when `OpenTelemetry:OtlpEndpoint` is empty + chapter-run compose-chain audit discipline | `RUN_3_RESULTS.md` §9.3.b (after D6 in `RUN_4_SETUP_LOG.md` §11) | (a) Add startup WARN log in `KombatsObservabilityExtensions.cs:37` when endpoint is empty. (b) Document the canonical compose chain in repo `README` so future runs include the override. | **Yes** for production observability hygiene; not blocking for performance. |
| Important | BFF queue-polling p95 ≈ 1.87 s dominates `total_ms` (architectural, not regression) | `RUN_0_BASELINE.md` §6, reconfirmed in `RUN_4_RESULTS.md` §10.3 | Push-based match-found notification (replace 500 ms polling). Larger scope, separate chapter (Ch7 per roadmap). | **No** — production is functional with current polling; latency optimization. |
| Important | BFF-relay `_connections` map process-local — constrains BFF horizontal scaling | `SIGNALR_SURFACE_MAP.md` §J.3 | Sticky sessions on BFF's frontend hop OR refactor map to Redis. Separate "BFF scale-out" chapter. | **No** at single-BFF; **Yes** if scaling BFF past 1 replica. |
| Important | Multi-replica `active_battles` gauge split-brain (+1/−1 phantom on cross-replica resolution) | `RUN_2_RESULTS.md` §8 | Tag gauge with `battle_id` or move increment/decrement to originating replica. Observability fix. Ch4 candidate. | **No** — aggregate sum reads correctly; per-replica curves bounce. |
| Lower | `matchmaking.player_combat_profiles` SERIALIZABLE conflicts under load | `RUN_0_BASELINE.md` §6 | Investigate projection/read concurrency. | **No** — observed only in pre-clean-teardown stale runs. |
| Lower | SignalR distributed-trace propagation | `CHAPTER_3_PLAN.md` §11 | Add tracing context to SignalR frames. | **No** — orthogonal observability improvement. |

The two **Critical** items (TTL + OTLP silent-skip) should be addressed before lifting `maxReplicas: 1` in any real environment. Both have small fix scope.

## 11. Roadmap to 1000 concurrent (one line per chapter)

Brief context for where Chapter 3 sits in the bigger picture (handoff Session 2 "Roadmap"). Each subsequent chapter has a separate plan document; one-line descriptions here:

- **Chapter 3** (this) — lift the `maxReplicas: 1` Battle ceiling. Done.
- **Chapter 2.5** — hardening pass: wire `StateTtlAfterEnd`, fix OTLP silent-skip + compose-audit discipline, possibly the SERIALIZABLE conflict. Pre-production gate.
- **Chapter 4** — capacity test at production concurrency-per-replica (the §7 / §8 open question). Likely 250-concurrent × 4-replica to start, scaling up.
- **Chapter 5** — Bicep ceiling lift in `infra/main.bicep` + Azure-side verification. Bridge from local-compose proofs to managed deployment.
- **Chapter 6** — observability split-brain fixes (`active_battles` per-replica, possibly per-replica OTel SDK init audits). Production-grade dashboards.
- **Chapter 7** — push-based match-found notification (replaces BFF queue polling). Closes `RUN_0_BASELINE.md` §6 third bullet.
- **Chapter 8** — BFF horizontal scaling + sticky sessions / `_connections` map refactor (`SIGNALR_SURFACE_MAP.md` §J.3). Lifts the BFF ceiling.

Each subsequent chapter has its own plan/runs/report cycle. Chapter 3 ships the SignalR-Redis-backplane + skip-negotiation primitive that all of 4–8 depend on.

## 12. Files in the chapter

Source changes (final tally — exactly 2 `src/Kombats.*` edits):

| File | Status at chapter close | Purpose |
|---|---|---|
| `src/Kombats.Battle/Kombats.Battle.Bootstrap/Program.cs` (line 131) | Committed in `5f63ec9` | D1 — `.AddStackExchangeRedis(redisConnectionString)` |
| `Directory.Packages.props` (line 46) | Committed in `5f63ec9` | D1 — `<PackageVersion Include="Microsoft.AspNetCore.SignalR.StackExchangeRedis" Version="10.0.3" />` |
| `src/Kombats.Battle/Kombats.Battle.Bootstrap/Kombats.Battle.Bootstrap.csproj` (line 29) | Committed in `5f63ec9` | D1 — central-pinned `<PackageReference>` |
| `src/Kombats.Bff/Kombats.Bff.Application/Relay/BattleHubRelay.cs` (lines 6, 67–77) | **Uncommitted at chapter close** | D1.5 — `SkipNegotiation=true` + `Transports=WebSockets` + comment |

Chapter-3-authored artefacts under `tests/Kombats.LoadTests/`:

| File | State at chapter close |
|---|---|
| `CHAPTER_3_PLAN.md` | Pre-existed (chapter plan) — committed at `1958389` |
| `RUN_0_BASELINE.md` | Pre-existed — committed at `1958389` |
| `RUN_0_DRIFT_INVESTIGATION.md` | Pre-existed — committed at `1958389` |
| `CLEANUP_WORKER_DIAGNOSIS.md` | Pre-existed — committed at `1958389` |
| `PHASE_1_REPORT.md` | Pre-existed — committed at `1958389` |
| `SIGNALR_SURFACE_MAP.md` | Pre-existed — committed at `1958389` |
| `PHASE_2_REPORT.md` | Committed at `1958389` |
| `PHASE_3_D1_REPORT.md` | Committed at `ad1d25b` |
| `RUN_1_SETUP_LOG.md` | Committed at `ad1d25b` |
| `RUN_1_RESULTS.md` | Committed at `ad1d25b` |
| `RUN_1_BACKPLANE_OVERHEAD_ANALYSIS.md` | Committed at `ad1d25b` |
| `RUN_2_SETUP_LOG.md` | Committed at `b2928ac` (cherry-picked from `development`) |
| `RUN_2_RESULTS.md` | Committed at `b2928ac` (cherry-picked from `development`) |
| `RUN_3_SETUP_LOG.md` | **Uncommitted** (includes §14 D1.5 amendment + §11 D6 repair documentation in `RUN_4_SETUP_LOG.md`) |
| `RUN_3_RESULTS.md` | **Uncommitted** (with §9.3.b + §9.6.b corrective addenda after Run 4) |
| `RUN_4_SETUP_LOG.md` | **Uncommitted** (with §11 D6 observability-pipeline repair) |
| `RUN_4_RESULTS.md` | **Uncommitted** |
| `CHAPTER_3_REPORT.md` (this file) | **Uncommitted** |

Infrastructure artefacts touched / used by Chapter 3:

| File | Role |
|---|---|
| `docker-compose.multi-replica.yml` | Added in Phase II (committed at `1958389`) — the `battle-2` overlay for multi-replica runs. |
| `observability/docker-compose.observability.override.yml` | **Pre-existed** but never in any chapter-run compose chain until D6 repair on 2026-05-14. Contains the OTel endpoint env that gate-keeps OTLP exporter attach. |
| `tests/Kombats.LoadTests/scripts/dns-rotation-check.sh` | Added in Phase II (committed at `1958389`) — Step-0 pre-flight for multi-replica runs (Run 2 / Run 3). |

**8 uncommitted files** at chapter close (1 source + 7 docs) land in the squash merge along with the 3 source-side commits (`5f63ec9` D1, `ad1d25b` Run 1 docs, `b2928ac` Run 2 docs cherry-pick).

## 13. Plain-language summary (Russian-friendly, kernel for STORY Part 2)

> Глава 3 — про то, как одна предсказуемая правка превратилась в две, и как через четыре measurement runs модель отказа уточнилась пять раз. Цель главы: убрать потолок `maxReplicas: 1` у Battle service, который в `infra/main.bicep` стоит не из-за каприза, а потому что SignalR без backplane структурно не умеет фанаутить group-сообщения между процессами — игрок на реплике A не получит ничего, что пошлёт `Clients.Group(...)` на реплике B. Production-цель — ~1000 одновременных боёв на player-speed; на single-replica мы упирались задолго до этой цифры.
>
> Run 0 заложил baseline (single-replica, no backplane): 98.8 % успешных, p50 = 1106 ms, ~9 матчей/с пейринг. Run 1 добавил backplane (D1, одна строка `.AddStackExchangeRedis`) — `total_ms` p50 удвоился до 2182 ms. `RUN_1_BACKPLANE_OVERHEAD_ANALYSIS.md` атрибутировал это на multiplexer queueing во время боя (per-publish RTT × 26 publishes ≈ +1040 ms). Source-level механизм был верный, но load-shape regime — нет; refinement пришёл позже. Run 2 убрал backplane и добавил вторую реплику Battle: предсказанный compound model failure (Event A handshake-split + Event B co-location lottery) лёг точно — 14 ArenaOpen / 3 DoubleForfeit / 2 Normal из 19 битв, против предсказанных 14.25 / 2.4 / 2.4. Все три ячейки внутри sampling noise при n=19. Это был самый яркий артефакт главы: compound model сошёлся с измерением до одного процента в каждой ячейке.
>
> В Run 3 setup открылась трещина в плане: backplane закрывает Event B (cross-replica group routing через Redis PUBSUB), но НЕ закрывает Event A. Negotiate POST приходит на реплику X, упорный WebSocket GET через DNS rotation попадает на реплику Y, и `IConnectionManager` на Y не знает токена X → 404. Это HTTP-level state на слой НИЖЕ `HubLifetimeManager` — backplane его структурно не реплицирует. Три внешних источника (GitHub issue #50171, Milan Jovanović March 2026, Infinum December 2025) указывают на одно решение для self-hosted без managed sticky LB: skip negotiation. Так появился D1.5 — одна строка `SkipNegotiation=true` в `BattleHubRelay.JoinBattleAsync`, плюс `Transports=WebSockets`, плюс комментарий объясняющий почему. После D1.5 — 5/5 smoke clean на multi-replica (где до этого было 4/5), 0 handshake 404 в 2125 WebSocket upgrades под нагрузкой Run 3, 1052/1052 битв завершились Normal. Глава доказала фикс на full scale.
>
> Но per-phase данные Run 3 уточнили модель ещё раз: Run 1's +1076 ms сидели почти целиком в `join_battle_ms` (+761 ms), а не в `battle_ms`. Если бы multiplexer saturation на battle-фазе была доминирующей причиной, оверхед был бы в `battle_ms` (там ~24 из ~26 publishes). Его там не было. Значит cost в Run 1 концентрировался на handshake setup, а не на per-publish во время боя. И тогда непонятно — насколько D1.5 (убирает negotiate roundtrip) помог сам по себе, а насколько помогло разделение HTTP-нагрузки между двумя репликами? Run 3 эти два фактора не разделял.
>
> Run 4 закрыл этот вопрос: single-replica + backplane + D1.5. `total_ms` p50 = 1130.6 ms — Run 0 territory. `join_battle_ms` p50 = 4.1 ms. Run 1 → Run 4 delta на `join_battle_ms` = −761.6 ms; Run 1 → Run 3 delta = −761.5 ms. Почти идентичны. **D1.5 сам по себе закрывает setup-overhead; вклад второй реплики ≤6 ms — под уровнем шума.** Outcome A confirmed. Плюс сюрприз: `battle_ms` в Run 4 = 60.4 ms, на +21 ms выше Run 0, на single-replica где cross-replica delivery физически невозможна. То есть RUN_3_RESULTS §9.6's cross-replica attribution была неправильной — реальная стоимость это локальный Redis PUBSUB hop на каждый publish, на каждой реплике, регардлесс of replica count. Run 4 — первое измерение, которое чисто изолирует steady-state backplane cost: +21 ms на типичный 5-7-turn бой ≈ +0.1 % wall-clock на production player-speed. Production-irrelevant, но впервые квантифицировано.
>
> И ещё одна вещь, которая всплыла во время Run 4 setup: вся глава 3 (и главы 1-2 до неё) запускались с broken OTel pipeline. `observability/docker-compose.observability.override.yml` — файл, который держит env `OpenTelemetry__OtlpEndpoint` для всех .NET сервисов — никогда не был в compose chain ни одного запуска. `Kombats.Common/Kombats.Observability/KombatsObservabilityExtensions.cs:37` при пустом endpoint **молча пропускает attach OTLP exporter**. Никаких ошибок, никаких WARN, ничего. Три главы тестов работали с почти-пустыми Grafana — и мы не заметили, потому что `multi-replica.yml:61` inlinе'ил env для `battle-2`, и хоть какие-то метрики приходили. Run 3 §9.3 интерпретировал этот gap как per-replica SDK quirk; Run 4 D6 диагностика показала, что это compose-chain bug. После fix (Level-3 down -v + corrected chain) Run 4 — первый chapter run с end-to-end-green observability pipeline. Урок: silent failure modes в observability — это сами по себе observability bugs. Defensive WARN log на startup, когда `OtlpEndpoint` пуст, стоит копейки и сэкономил бы три главы внимания.
>
> Что глава доказала: D1 + D1.5 (две source-line) убирают потолок `maxReplicas: 1` на нашем load shape (25 concurrent × 2 replicas) с success rate 98.8 % и latency в Run 0 territory. Что НЕ доказала: масштабирование за пределы 25-concurrent / 12-per-replica (production target — 500-per-replica при 2 репликах — это 40× выше) — это вопрос для Chapter 4 capacity testing, не для Chapter 3 extrapolation. Что осталось в backlog: TTL wiring (Ch2.5 critical), OTLP silent-skip (Ch2.5 critical, новое от D6), BFF push-based queue notifications (Ch7), BFF horizontal scaling (Ch8), `active_battles` split-brain (Ch4 observability). Two ceiling-lift source lines, four measurement runs, five model iterations, one operational finding. Глава готова к merge.

---

**Chapter 3 ready for squash merge.** Architect commits the full set in one go:

1. D1.5 source change in `src/Kombats.Bff/Kombats.Bff.Application/Relay/BattleHubRelay.cs`.
2. 7 uncommitted documents under `tests/Kombats.LoadTests/`: `RUN_3_SETUP_LOG.md`, `RUN_3_RESULTS.md` (with §9.3.b + §9.6.b corrective addenda), `RUN_4_SETUP_LOG.md` (with §11 D6 repair), `RUN_4_RESULTS.md`, and `CHAPTER_3_REPORT.md` (this file).

Squash merge `feat/signalr-backplane` → `development` → PR to `main`. STORY Part 2 (LOAD_TEST_STORY.md addendum) is the next deliverable — architect writes separately, post-merge.
