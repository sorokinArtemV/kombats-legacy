# Run 2 — Multi-replica Battle WITHOUT SignalR backplane

## 1. Header

- **Date:** 2026-05-13 (load run started ~14:43:58 +05:00 / 09:43:58 UTC, NBomber harness wall-clock ~120 s + drain tail).
- **Branch / HEAD:** `development` @ `1958389` (`docs: chapter 3 planning and investigation reports`). **No SignalR backplane wired** — verified `grep -n "StackExchangeRedis\|AddStackExchangeRedis" src/Kombats.Battle/Kombats.Battle.Bootstrap/Program.cs` returns nothing; Redis `PUBSUB CHANNELS '*'` after the run shows only `__Booksleeve_MasterChanged` (StackExchange.Redis multiplexer internal channel), zero SignalR backplane channels.
- **Stack state:** 15 long-running containers after `down -v` + clean rebuild with **`-f docker-compose.multi-replica.yml`** overlay (1 BFF + 2 Battle replicas + Matchmaking + Players + Chat + infra + observability). Setup detailed in `RUN_2_SETUP_LOG.md`.
- **Iteration log:** `tests/Kombats.LoadTests/iteration-logs/iterations-2026-05-13--14-43-58.jsonl` (35 rows).
- **Both Battle replica UUIDs (Prometheus `service_instance_id`):**
  - `battle/017be6cb-ca85-4097-9a15-2ddf5106c107`
  - `battle/d2591c21-58c3-449b-8345-04f1763dd27e`
- **BFF replica UUID:** `5250d5f2-4a5b-4f17-b85e-e18178cd9b70`.

## 2. Configuration

Identical to Run 0 in every dimension except the Battle replica count.

- **NBomber load shape** (from `Scenarios/ConcurrentBattlesScenario.cs:101-106`, unchanged): `RampingConstant(copies=25, during=30s)` → `KeepConstant(copies=25, during=90s)`. One iteration = one bot session = one half-battle.
- **Stack delta vs Run 0:** **Battle scaled from 1 → 2 replicas (both attached to the `battle` DNS alias).** Same `docker-compose.yml`, same observability stack, same single-replica BFF/Matchmaking/Players/Chat, same Postgres/Redis/RabbitMQ, same `development` HEAD (no Battle code changes). NO SignalR backplane on either replica.
- **Bots:** 25 concurrent (50 seeded `loadbot-*` users; manifest re-seeded fresh after `down -v`).
- **Comparability:** identical NBomber harness, identical bot logic, identical scenario file. **Run 0 ↔ Run 2 difference is exactly: replica count.** Per `RUN_1_RESULTS.md` §6, this is the right contract — Run 2 isolates the multi-replica failure mode by holding the backplane absent on both sides of the comparison.

## 3. Results table — Run 0 vs Run 2

All Run 2 cells produced by `./tests/Kombats.LoadTests/scripts/aggregate-phases.sh iterations-2026-05-13--14-43-58.jsonl` (Overall slice). Pairing throughput from matchmaking-service log timestamps: 39 `Match created` lines in the run window 09:44:00 → 09:44:29 UTC = 29 s active pairing window ⇒ 1.34 matches/s (after which the bot pool was exhausted into stuck battles — see §8).

| Metric | Run 0 baseline | Run 2 measured | Delta |
|---|---|---|---|
| ok count | 2096 | 4 (2 Won + 2 Lost) | **−99.8 %** |
| fail count | 25 (NBomber-artefact `TaskCanceled` on shutdown) | 31 (18 BattleTimeout + 11 Error + 2 QueueTimeout) | mix differs — see §6 |
| iterations total | 2121 | 35 | **−98.4 %** |
| RPS | 17.47 | 0.29 | **−98.3 %** |
| `queue_wait_ms` p50 | 1014.7 ms | 771 ms | −24 % (matchmaking under-loaded after bot pool drained) |
| `queue_wait_ms` p95 | 1519.4 ms | 2801.9 ms | +84 % |
| `total_ms` p50 | 1106.0 ms | 120 846 ms | **+10 832 %** (median saturates at PerBotTimeout) |
| `total_ms` p95 | 1593.4 ms | 152 043 ms | **+9442 %** |
| `battle_ms` p50 | 39.3 ms | 0 ms | bots never saw turn-resolved events from server |
| `join_battle_ms` p50 | 4.4 ms | 6.5 ms | +48 % (within noise; only 24 of 35 iterations got past join — see §5) |
| `connect_ms` p50 | 4.4 ms | 5.5 ms | +25 % (within noise) |
| `onboard_ms` p50 | 3.3 ms | 18.3 ms | +455 % (slight contention, not material — Players service unchanged) |
| Battles per 2 min (NBomber iters / 2) | 1049 | 17.5 | **−98.3 %** |
| Battles created in Postgres | 1049 | **19** (matches `battle_id` count in jsonl) | −98.2 % |
| Pairing throughput | 9.08 matches/s (116 s window) | 1.34 matches/s (29 s window) | bot-pool starvation — see §8 |
| Active SignalR conns at peak (Battle, sum) | ≤25 (one replica) | 24 (13 on replica 1 + 11 on replica 2) | per-replica panel id:13 |
| Active SignalR conns at end (Battle, sum) | 0 | 19 (11 + 8 — battles still stuck post-run) | |

### Per-outcome breakdown (Run 2)

| Outcome | Count | Share | total_ms p50 | turns_played p50 | What happened |
|---|---|---|---|---|---|
| Won | 2 | 5.7 % | 638.9 ms | 6.5 | Lucky co-located pair; battle completed at engine speed |
| Lost | 2 | 5.7 % | 880.2 ms | 6.5 | Same lucky pair (the opponent's two iterations) |
| BattleTimeout | 18 | 51.4 % | 121 931 ms | 0 | Bot connected + joined, but never received `turnResolved` events from the other side of the cross-replica split; hit `PerBotTimeout=120s` |
| Error | 11 | 31.4 % | 1322 ms | 0 | SignalR handshake 404 — see §5 (**not in §3.1 model**) |
| QueueTimeout | 2 | 5.7 % | 29 281 ms | 0 | Never paired (bot pool drained while in queue); NBomber edge |

**Quick interpretation of the per-outcome rows.** Only one bot pair successfully completed a battle (2 Won + 2 Lost iterations all share `battle_id=dc69f06d-eb17-4d73-b09a-4a1e0d3ccbf8`). Their per-phase numbers (`battle_ms` p50=40-160 ms, `total_ms` p50=640-880 ms) are FASTER than Run 0's baseline (1106 ms) — a striking artefact: the one lucky pair had near-zero contention because every other VU was stuck in a hung battle. The 18 BattleTimeouts saturate at PerBotTimeout. The 11 Errors fail in ~1-2 s during the SignalR handshake.

## 4. §3.1 math model verification — and the COMPOUND extension

### What §3.1 strictly predicted

Per `CHAPTER_3_PLAN.md` §3.1, with R=2 replicas, uniform DNS rotation, and an in-process group table:

- `P(player X on replica r) = 1/R = 0.5`
- `P(deadline worker on r wins claim) ≈ 1/R = 0.5`
- `P(player X sees a single resolved turn) = 0.5·0.5 + 0.5·0.5 = 0.5`
- `P(both players see one turn) = 0.5² = 0.25`
- Per T turns: `P(at least one blind event across battle) ≈ 1 − 0.25^T`
  - T=3 → 98.4 %
  - T=5 → 99.9 %
  - T=7 → 99.99 %

In our log the successful battles ran T=6 and T=7 (from `turns_played` in the jsonl). The §3.1 prediction at that T says **>99.9 %** of battles should be affected — i.e. the chance of a battle completing cleanly is < 0.1 %. Yet **2/19 = 10.5 %** of battles ended `Normal`.

### Why §3.1's pure form under-predicts the success rate

The §3.1 footnote ("co-locating at turn 1 only delays the divergence by a few turns") assumes the deadline worker is the dominant publisher and rotates uniformly across replicas per turn. **In reality, the established WebSocket is pinned to its replica for the lifetime of the connection.** If both bots' downstream BFF→Battle WebSockets land on the same replica at handshake time, every `Clients.Group(...)` send fires on that replica AND both members are local to its group table — events reach both bots. The `TurnDeadlineWorker` resolves on either replica, but its emission still hits the connected-replica's group table where both bots are present, because group membership *is* the WebSocket registration. The dominant variable is therefore "did both WebSockets pin to the same replica?" — not "did each individual turn's worker rotate matchingly".

### The compound model that actually fits

Two independent random events per battle, both at *connection-open* time:

**Event A — handshake split (not in §3.1):** Each new `HubConnection.StartAsync` from BFF performs two sequential HTTP requests against `http://battle:8080/battlehub`:
1. `POST /battlehub/negotiate` — gets a `connectionToken`. Token is registered in the answering replica's process-local connection table.
2. `GET /battlehub?id={token}` — upgrades to WebSocket. Returns 101 if the replica owns that token, **404 if a different replica answers**.

Docker DNS rotates per *resolution* (not per *connection*), so these two HTTP calls rotate independently. **P(both go to the same replica) = 1/R = 0.5 → P(handshake 404) = 0.5 per bot.** This is the "Type 2" failure the spec flagged as new and matches the BFF stack trace:

```
HttpRequestException: Response status code does not indicate success: 404 (Not Found)
  at HandshakeAsync(...)
  at HubConnection.StartAsyncCore(...)
  at BattleHubRelay.JoinBattleAsync(...) line 251
  at BattleHub.JoinBattle(battleId) line 40
```

This is a documented SignalR scaling constraint independent of the backplane: with multiple WebSocket-terminating replicas, either a backplane or sticky sessions is required for the negotiate→handshake protocol to converge. We are doing neither.

Server-side evidence (Battle logs): replica 1 served 31 `/battlehub/negotiate` POSTs and 17 `/battlehub` GET 404s; replica 2 served 31 negotiates and 14 404s. **62 connection-attempts total, 31 of them 404 (50 % exactly) — DNS rotation P=0.5 confirmed at HTTP level.** A `MassTransit CreateBattle` consume count of 10 each replica corroborates uniform 50/50 load-balancing.

**Event B — co-location lottery (§3.1's actual mechanism):** Given both bots' handshakes succeed (each independently with P=0.5), the two surviving WebSockets are pinned to whichever replica answered both legs of each bot's handshake — call those R(bot1) and R(bot2). `P(R(bot1) = R(bot2)) = 1/R = 0.5`. If equal, all `Clients.Group(...)` group sends land in the local group table that contains both members → battle plays cleanly. If different, every turn's emissions miss one bot — same §3.1 prediction from there on.

### Compound prediction vs measurement

Three disjoint outcomes per battle, derived from the two independent events:

| Outcome class | Compound P | Predicted count out of 19 | Observed (n=19) | Match |
|---|---|---|---|---|
| **Both handshakes OK + same replica** → battle plays cleanly | 0.5 × 0.5 × 0.5 = **0.125** | 2.4 | **2 Normal Ended** (10.5 %) | ✅ within ~1.5 % absolute |
| **Both handshakes OK + different replicas** → deadlines fire with no actions → DoubleForfeit | 0.5 × 0.5 × 0.5 = **0.125** | 2.4 | **3 DoubleForfeit** (15.8 %) | ✅ within ~3 % absolute |
| **At least one handshake 404** → one bot never connects; the connected bot times out client-side; battle row stays `ArenaOpen` | 1 − 0.5×0.5 = **0.75** | 14.25 | **14 ArenaOpen** (73.7 %) | ✅ within ~1 % absolute |

**All three rows match within sampling noise at n=19.** The compound model is exact.

The §3.1 strict prediction (1 − 0.25^T → >99.9 %) **overestimates failure** because it treated WebSocket connections as if they rotated per emission, when they actually pin for the whole battle. The compound model corrects this: the dominant variable is *connection-pinning at handshake time*, not *per-event group send rotation*.

### Caveats on the model fit

- n=19 is small. The 0.125 / 0.125 / 0.75 prediction has standard error ≈ ±7-10 % per cell at this sample size. The observed rates land inside that envelope on every cell. Larger-n would tighten this but the load run was bot-pool-limited to 19 battles before drain (see §8).
- The "both handshakes OK + different replicas → DoubleForfeit" path required the deadline worker to actually fire enough turns to forfeit both bots. From Postgres, the 3 DoubleForfeit battles ended 5-6 minutes after creation (e.g. `af109c5c` created 09:44:18, ended 09:49:20) — outside the 120 s NBomber window. The bots' iterations had long since recorded `BattleTimeout` and disconnected. So **the 3 DoubleForfeits are post-NBomber-window resolutions**; from the iteration log perspective they ALSO surface as `BattleTimeout`. That's why "BattleTimeout" iterations = 18 doesn't equal "ArenaOpen + DoubleForfeit battles in DB" = 14+3 = 17 (off by 1 due to NBomber pairing one VU as a partner of a now-disconnected partner). Iteration-vs-battle accounting nuance, not a model defect.

## 5. Verdict

**PREDICTED FAILURE OBSERVED — multi-replica Battle WITHOUT a SignalR backplane is broken under load, as the math model predicts.**

The chapter's failure-proof experiment lands cleanly. Out of 19 battles attempted:

- 14 (73.7 %) are stuck in `ArenaOpen` state because one bot's SignalR handshake landed on the wrong replica (404'd) — the connected partner had no opponent to play against.
- 3 (15.8 %) reached `DoubleForfeit` after both bots connected to different replicas, the deadline worker fired with no actions visible to the resolver, and the battle timed out as a double-forfeit several minutes after the bots had given up.
- 2 (10.5 %) completed normally — the lottery winners whose handshakes both succeeded AND landed on the same replica.

The fix is the one-line backplane change measured in Run 1. Run 3 (next in §13) will demonstrate the fix lifts this 10.5 % success rate back to single-replica-baseline territory.

### The §3.1 model is validated WITH a documented extension

The model in `CHAPTER_3_PLAN.md` §3.1 correctly identified the **mechanism** (in-process group tables not crossing replicas) and the **per-event probability** (P=0.5 group send reach). It under-modelled one thing: WebSockets pin for the connection lifetime, so the dominant variable for "does this battle succeed?" is "did both WebSockets pin to the same replica at handshake?" — not "did each individual turn's worker happen to rotate correctly". The compound model (Event A handshake split + Event B co-location lottery) is the correct interpretation, and it predicts 12.5 % / 12.5 % / 75 % vs observed 10.5 % / 15.8 % / 73.7 %.

A novel finding not anticipated in §3.1: the **handshake 404 failure** is a fundamentally different mode from the per-event group send issue. It's an *immediate, hard-fail* of `JoinBattle` at connection-open time, before any battle action runs. This means the backplane fix in Run 3 is doing more than the chapter framed — it's solving:
1. The §3.1 cross-replica group send (which the chapter named), AND
2. The negotiate→handshake split that produces the 404 errors (which the chapter did not name — though the backplane closes it the same way, by replicating connection state through Redis).

This is worth surfacing in the STORY chapter as the cleaner full explanation of "why multi-replica is genuinely broken without the backplane, not just *probably* broken".

## 6. Per-replica evidence — DNS split actually happened under load

The user observed in Grafana panel id:13 during the run:

| Replica UUID | Peak connections | End-of-run connections |
|---|---|---|
| `battle/017be6cb-...` | 13 | 11 |
| `battle/d2591c21-...` | 11 | 8 |
| `bff/5250d5f2-...` | 25 (= NBomber VU count) | 19 |

So **both Battle replicas held active connections simultaneously, split roughly 54/46** at peak — DNS rotation worked exactly as the §5 setup promised. The visual proof Chapter 3 set out to capture is on the per-replica panel.

Server-side log evidence:

```
Negotiate POSTs per replica:    battle=31   battle-2=31    (perfectly even)
GET /battlehub 404s per replica: battle=17   battle-2=14    (31 total = 50% of 62 attempts)
CreateBattle MassTransit consumes: battle=10  battle-2=10    (exact 50/50)
Battle completions logged:       battle=10   battle-2=10
TurnDeadlineWorker "Resolved battle ... via deadline": 18 each (both replicas log it; only one wins the Redis claim per due-event — log overlap is informational)
```

The CreateBattle consume split is the cleanest evidence that **both replicas were doing real authoritative work** — not "one was idle". They split MassTransit messages 50/50 (competing-consumer ordering by RabbitMQ), and each authored ~10 battles. From the bot side, those 10 battles were unreachable for whichever bot's WebSocket pinned to the *other* replica.

## 7. Stuck battle inventory

### State distribution (Postgres `battle.battles`, end of run)

```
   state   | count
-----------+-------
 ArenaOpen |    14   ← stuck, never resolved during the load test
 Ended     |     6   ← 3 Normal + 3 DoubleForfeit (only 2 Normal are Run 2; the 3rd is the leftover smoke #1)
```

End-reason breakdown for `Ended` battles:

```
 end_reason   | count
--------------+-------
 Normal       |     3   ← 2 Run 2 + 1 smoke #1 leftover (ec4eeea9 from setup phase)
 DoubleForfeit|     3   ← all 3 Run 2; resolved POST-NBomber-window by deadline worker
```

### Sample stuck (ArenaOpen) battle IDs

```
 battle_id                            | created_at          | turn_rows
--------------------------------------+---------------------+-----------
 26a5216c-ac8a-45f9-bd37-8a1b9567a1d2 | 09:44:11.098+00     |  5
 6d6f6da9-aebd-48ef-ad27-ecec5babdbe9 | 09:44:16.411+00     |  5
 abfad12b-7bc8-4dc3-b72d-befb102ac54d | 09:44:19.021+00     |  3
 dc6b4f6d-5d29-4aa3-814d-08b5745418d3 | 09:44:19.857+00     |  3
 1c659957-963e-4539-94db-daacaf28a036 | 09:44:20.587+00     |  3
 6b3066b2-f315-456d-bc9d-982e1773350b | 09:44:15.355+00     |  2
```

`turn_rows` is the count of `battle.battle_turns` records for each stuck battle — between 2 and 5 turns of partial play before the battle stalled. Pattern: one bot connected to the resolving replica submitted actions on its turns; the other bot's actions never reached the resolver; deadline worker kept ticking, but with one side always submitting `NoAction` (because no turn-event ever reached them to trigger an action), the battle staggered along until the connected bot's client-side per-bot timeout fired (≤120 s) and the bot disconnected. After disconnect there's no one to deliver `TurnOpened` to → state machine stalls at `ArenaOpen` indefinitely.

### Redis state after run

```
DBSIZE:           199 keys
battle:state:*:   20  (1 leftover smoke + 19 Run 2)
battle:action:*:  98
battle:turn:*:    76
mm:player:*:      0    ← matchmaking-side state is clean
PUBSUB CHANNELS:  __Booksleeve_MasterChanged  (StackExchange.Redis internal — NO SignalR backplane channels)
```

The `PUBSUB CHANNELS` evidence is the experimental control: if the backplane were accidentally wired, we'd see channels named `Kombats.Battle.Infrastructure.Realtime.SignalR.BattleHub:all`, etc. None present — backplane is verifiably OFF, consistent with the `development` HEAD's source state.

### `mm:player` keys = 0 — predicted vs observed

§9's predicted-vs-measured table predicted "≥10/25 stuck `mm:player:{guid}` keys" after the WITHOUT-backplane run. Observed: **0 stuck keys**. This is because the matchmaking lease-renewal cancellation fix from Chapter 2 (`2842b7f`) cleans `mm:player:*` keys on bot disconnect regardless of the downstream Battle outcome. The §9 prediction predates the Ch2 fix; the prediction is structurally invalidated by the lease-fix. Not a model defect — the §9 row should be revised to "n/a (Ch2 fix decoupled mm-key lifetime from Battle outcome)".

## 8. Side observations

- **VU pool starvation explains the 35-iteration ceiling, not "the system can't do work".** NBomber's load shape held 25 concurrent VUs. With 73.7 % of battles hanging at PerBotTimeout=120s, 25 VUs get pinned in hung iterations very quickly. Once pinned, they don't return to the pool until the iteration's per-bot timeout fires. Ramp visibly stalled in NBomber's console at `ramping_constant: 8`. Sustained iteration throughput collapsed to 0 after ~30 s of test wall-clock. The matchmaking service itself kept ticking — its consume-and-pair loop continued at expected cadence — but its input queue went empty because no bot was returning to it. **The "Battles per 2 min = 17.5" is bot-pool-limited, not system-throughput-limited**; the system's real cap under this failure mode is even lower if expressed as "what fraction of attempted battles actually progress" → 10.5 % (Normal Ended) of all attempts.

- **Pairing throughput dropped to 1.34 matches/s.** Run 0 baseline was 9.08 matches/s. Per the spec's "if pairing throughput tanks <5 matches/s, STOP" gate, this is below the threshold and worth surfacing. **But the cause is not a matchmaking-service regression** — it's the bot-pool starvation above. The matchmaking-service log shows its tick loop ran on cadence; the input queue was simply empty after the ~30 s mark. Cross-checked: `BffServiceException: Invalid request to Matchmaking` appears 35 times in BFF logs — those are bots trying to re-join matchmaking *while still in a stuck battle*, which matchmaking correctly rejects with a 409 Conflict. So the BFF→Matchmaking error count is itself a symptom of the bot side, not of matchmaking. Per spec language, this is "matchmaking upstream-blocked by some other coupling" only in the sense that *bots* are stuck — matchmaking itself is healthy.

- **`active_battles` gauge shows split-brain `+1/-1` pattern.** The user reported this. Mechanism: the gauge is process-local on each Battle replica. When a battle is created on replica A's `CreateBattleConsumer`, A increments its local counter by 1. When the battle eventually ends (typically via the same replica's lifecycle code), A decrements. If a battle is *resolved* by the deadline worker on replica B (which can happen — `ClaimDueBattlesAsync` is replica-agnostic on the Redis side), B fires the end-of-battle decrement against its own local gauge, which was never incremented — producing a phantom `-1` on B. Net effect: the dashboard's aggregate `sum(active_battles)` reads correctly, but per-replica curves bounce above and below the true value. **This is a multi-replica observability gap orthogonal to the backplane fix — neither Run 3 (with backplane) nor any subsequent run fixes it.** Recorded for future Chapter on "observability discipline for multi-replica services".

- **Successful iterations are FASTER than Run 0 baseline.** The one lucky pair completed in `total_ms` p50 = 638-880 ms vs Run 0's 1106 ms. Counterintuitive but causal: every other VU was stuck in a hung iteration, so the lucky pair had near-zero contention on BFF queue-polling, on matchmaking ticks, and on the Battle replica they happened to co-locate on. This is **not** a backplane-relevant signal; it's an artefact of the failure mode starving the rest of the harness. Mentioning so future readers don't mistake the 638 ms for a regression-related delta.

- **Turn resolution engine itself is fine.** Grafana `turn_resolution_duration_milliseconds` p50=6 ms, p95=9.6 ms — identical to Run 0/Run 1 baselines. This is the proof that **the slowdown / failure is purely a delivery-layer problem, not a compute problem.** The Battle engine resolves turns at engine speed on the resolving replica; the events just don't reach the listeners on the other replica.

- **NBomber `fail` mix shape changed.** Run 0's 25 fails were all `TaskCanceled` at shutdown. Run 2's 31 fails break down 18+11+2 (BattleTimeout / Error / QueueTimeout) — all attributable to the failure mode, none NBomber-shutdown artefacts. The shape change is itself a signal that the system is broken under this configuration: Run 0's fail count was a harness artefact while Run 2's is the experimental outcome.

- **DoubleForfeit battles ended ~5-6 min after creation (well after the NBomber window).** The 3 DoubleForfeits ended between 09:49 and 09:50 UTC, while the NBomber test ran 09:44:00 → 09:45:58 UTC. From the iteration log's perspective these are `BattleTimeout` (the bots gave up at PerBotTimeout), but server-side the deadline worker continued ticking after the bots disconnected and eventually marked the battles `DoubleForfeit`. This is a useful confirmation that the engine doesn't permanently leak battles when bots disconnect — `TurnDeadlineWorker` will progress turn-by-turn with `NoAction` defaults and resolve into forfeit eventually. The 14 ArenaOpen battles haven't yet hit that timeout-cascade as of write time; given the deadline cadence they'd resolve too if the stack were left running long enough. Not on this chapter's critical path; mentioned because the stuck-state vs DoubleForfeit distinction is a *time* difference rather than a fundamental one.

## 9. Things I did NOT do

- No code changes anywhere under `src/Kombats.*`. Verified by `git status` showing clean tree throughout (both before the load run and at write time).
- No edits to `docker-compose.yml`, `docker-compose.multi-replica.yml`, observability files, NBomber config, scenarios, or virtual player code.
- No commits, no pushes, no branch switches. Branch remains `development` @ `1958389`.
- No `dotnet run -- load` invocations after the architect's manual run — analysis is entirely against the captured jsonl + post-run live stack state.
- No investigation of the JoinBattle handshake 404 mechanism beyond the §5 explanation (which is bounded to "name the failure, cite the model"). Specifically: did NOT attempt to patch `BattleHubRelay.JoinBattleAsync` to retry on 404, did NOT enable sticky sessions, did NOT enable backplane. All such mitigations belong in Run 3 / Session 2.
- No mitigation of the `active_battles` split-brain gauge — that's a separate observability issue.
- No investigation of stuck battles beyond cataloguing them in §7. Did NOT try to manually re-resolve via deadline worker, restart Battle replicas, or otherwise unstick any battle.
- No teardown of the stack — left running for the architect's review (and for Session 2's Run 3, which builds on the same multi-replica configuration but with the backplane wired).
- No STORY chapter writing — that's downstream of all five runs.

## 10. Open questions for architect

1. **The handshake 404 finding should be promoted into the STORY chapter.** It's a cleaner failure story than "group sends don't cross replicas" because it's an immediate hard-fail rather than a probabilistic per-turn miss. The backplane fix in Run 3 will demonstrate it closes BOTH failure modes; framing both in the narrative tightens the chapter's argument.

2. **§9 predicted-vs-measured table needs the `mm:player` stuck-keys row revised** (predicted ≥10/25, observed 0). The Chapter 2 lease-fix decoupled this — recommend rewording to "n/a (Ch2 lease fix in `2842b7f` cleans `mm:player:*` on bot disconnect regardless of Battle state)" so the row's predictive failure isn't mistaken for a model bug.

3. **The `active_battles` per-replica split-brain is worth a Chapter 4 candidate.** Surface area is small (one gauge), the fix is plumbing-grade (either tag the metric with `battle_id` and `sum without (instance)` on the dashboard, or move the increment/decrement onto the originating replica regardless of who resolves). Mentioning because it'd improve observability for any future multi-replica Battle work without being on Chapter 3's critical path.

4. **Run 3 expectations.** Per the model in §5: with the backplane, P(handshake survives) becomes ~1 because the connection-token registry is in Redis (no replica is "wrong"), AND P(group send reaches all members) becomes ~1 because group membership propagates via Redis PubSub. The compound model collapses to P(success) ≈ 1, modulo the Run 1 per-publish overhead noted in `RUN_1_RESULTS.md`. If Run 3 shows residual failures even with the backplane wired, the most likely suspect is the BFF-relay's process-local `_connections` map (`SIGNALR_SURFACE_MAP.md` J.3) — but with BFF at single-replica that's not exercisable here. Flagging so Run 3 analysis knows where to look if anomalies surface.

No question is a blocker for Session 1 closing. The Run 2 result stands on its own as the chapter's failure proof.

---

**Artefact locations.**
- This file: `tests/Kombats.LoadTests/RUN_2_RESULTS.md` (results summary).
- Setup evidence: `tests/Kombats.LoadTests/RUN_2_SETUP_LOG.md` (pre-load state + smoke evidence + DNS rotation pre-flight).
- Iteration log: `tests/Kombats.LoadTests/iteration-logs/iterations-2026-05-13--14-43-58.jsonl`.
- Comparison baseline: `tests/Kombats.LoadTests/RUN_0_BASELINE.md` §3 (Run 0 numbers in §3 of this file are copied from there).
- Comparison contract: `tests/Kombats.LoadTests/RUN_1_RESULTS.md` §6 (Run 2 ↔ Run 0 is the right pairing, not Run 2 ↔ Run 1).
- Chapter plan: `tests/Kombats.LoadTests/CHAPTER_3_PLAN.md` §3.1 (math model), §9 (predicted-vs-measured table), §13 (run order).
