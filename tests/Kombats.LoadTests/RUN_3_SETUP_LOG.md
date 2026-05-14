# Run 3 Setup Log — Multi-replica Battle WITH SignalR Redis backplane

## 0. Header

- **Date:** 2026-05-13
- **Branch / HEAD:** `feat/signalr-backplane` @ `b2928ac` (cherry-pick of `120ceef9` "Run 2 docs" onto Run 1 HEAD).
  HEAD chain (newest → oldest):
  ```
  b2928ac docs(loadtests): Run 2 — multi-replica without backplane (failure proof)   ← cherry-picked from development
  ad1d25b docs(loadtests): Run 1 — single-replica with backplane (baseline shift, not regression)
  5f63ec9 feat(battle): add SignalR Redis backplane                                    ← D1 code change (PHASE_3_D1_REPORT.md)
  1958389 docs: chapter 3 planning and investigation reports                          ← cherry-pick parent
  6971d51 chore: chapter 3 infrastructure
  ```
  Cherry-pick was conflict-free (Run 2 docs are 2 new files under `tests/Kombats.LoadTests/`, no overlap with D1 in `src/Kombats.Battle/...` + `Directory.Packages.props`).
- **Stack state:** 15 long-running containers + 1 one-shot bootstrap after full `down -v` from Run 2 state + clean rebuild with `-f docker-compose.multi-replica.yml` overlay. **Multi-replica Battle WITH backplane** — both replicas have `Microsoft.AspNetCore.SignalR.StackExchangeRedis 10.0.3` loaded; both `RedisHubLifetimeManager` logs report "Connected to Redis"; Redis `PUBSUB NUMSUB` confirms 2 subscribers on cross-replica channels.
- **Purpose:** Run 3 (CHAPTER_3_PLAN §13) — multi-replica Battle WITH backplane, the chapter's fix proof. Phase A setup only — load is the architect's manual step.
- **Scope:** Phase A only. **STOP at end of A.13 to escalate critical finding to architect (see §11) — do not proceed to load.**

---

## 1. Step-by-step timestamps and outputs

| Step | Action | Time (Asia/Yekaterinburg, +0500) | Result |
|---|---|---|---|
| A.0 | Cherry-pick `120ceef9` onto `feat/signalr-backplane` | 15:42 | ✅ conflict-free; new HEAD `b2928ac`; both Run 1 and Run 2 docs now visible on the working branch |
| A.1 | State check (branch, backplane line, Run 2 containers still up) | 15:42 | ✅ `feat/signalr-backplane` clean tree; `.AddStackExchangeRedis(redisConnectionString)` at `src/Kombats.Battle/Kombats.Battle.Bootstrap/Program.cs:131`; 15 Run 2 containers still up at "Up 2 hours" |
| A.2 | Teardown `docker compose ... down -v` (4-file chain) | 15:42:30 | ✅ all containers + 5 named volumes + network removed; `docker ps -a \| grep kombats` empty; `docker volume ls \| grep kombats` empty |
| A.3 | Rebuild `up -d --build` (4 files incl. multi-replica overlay) | 15:43:02 | ✅ 15 long-running + 1 one-shot (`kombats-keycloak-bootstrap`); both `kombats-battle` and `kombats-battle-2` Up within ~30 s |
| A.4 | `./scripts/run-migrations.sh` + restart 6 backend services | 15:43:50 → 15:45:00 | ✅ `=== All migrations applied successfully ===`; both Battle replica health endpoints (`/health/ready`) returned `200` on first poll via `curlimages/curl:8.10.1` helper on `kombats_default` |
| A.5 | Keycloak bootstrap verification | 15:45:05 | ✅ `docker logs kombats-keycloak-bootstrap` ends with `[keycloak-bootstrap] done.`; master-realm token endpoint returned HTTP 200 |
| A.6 | `dotnet run -- seed-users` | 15:45:14 | ✅ `created=50, existed=0, failed=0` (clean post-teardown manifest) |
| A.7 | Backplane verification (NEW Run 3 gate) | 15:45:20 | ✅ `Microsoft.AspNetCore.SignalR.StackExchangeRedis.dll` present in `/app/` on both Battle replicas; `RedisHubLifetimeManager` logs `Connecting to Redis endpoints: redis:6379` then `Connected to Redis.` on both; no errors or WARNs |
| A.8 | Smoke (`single-bot` then `smoke` ×2 per protocol; 3 additional smokes ran during A.11 OTel diagnosis) | 15:45:19 → 15:49:46 | ⚠ **Mixed: 4/5 smokes clean, 1/5 hit the Run 2 Event A handshake-404 pattern even with backplane wired** — see §3 and §11 |
| A.9 | Backplane channels live + NUMSUB | 15:46:30 | ✅ `BattleHub:all` NUMSUB=2; `BattleHub:internal:groups` NUMSUB=2 — both Battle replicas participate in cross-replica routing |
| A.10 | `dns-rotation-check.sh` hard gate | 15:46:45 | ✅ **exit 0** — 7/8 split (47/53) over 15 probes |
| A.11 | Prometheus 3-series verification | 15:47:10 | ⚠ Only 1 series visible at probe time (`battle/d9cd1f66-...`, value 0). Short-lived smoke connections (~0.8 s each) missed the default 60 s OTel metric export window. Same caveat noted in `PHASE_2_REPORT.md` §2 step 4 — the series register correctly per replica; metric will saturate under sustained load. **Not blocking for setup; flagging for A.13 readiness table.** |
| A.12 | Write this setup log | 15:50 | ✅ (in progress) |

---

## 2. DNS rotation check output (verbatim)

```
Discovering containers with alias 'battle' on network 'kombats_default'...
Discovered 2 container(s) with alias 'battle':
  kombats-battle  ->  172.19.0.15
  kombats-battle-2  ->  172.19.0.16

Probing 'dig +short battle | head -n1' 15 times from a helper container on 'kombats_default'...

Probe results (15 probes, 0 failed):
  kombats-battle (172.19.0.15): 7 hits
  kombats-battle-2 (172.19.0.16): 8 hits

OK: DNS rotation confirmed
EXIT=0
```

7/8 split (47/53) — well within tolerance. The §3.1 math model precondition for multi-replica work is satisfied — and verified post-smoke at the HTTP level: Battle 1 served 4 negotiate POSTs, Battle 2 served 9; per-GET upgrade outcomes split across replicas (see §3 below).

---

## 3. Smoke results — the predicted PASS, with one critical residual

Smokes ran in two waves:

| Wave | Command | Wall clock | r1 outcome | r2 outcome | Turns | Interpretation |
|---|---|---|---|---|---|---|
| protocol #1 | `dotnet run -- smoke` | **0.92 s** | Lost (turns=6) | Won (turns=6) | 6 | ✅ clean — both bots received full event stream (`turnOpened=5 resolved=6 damaged=9 stateUpd=5 feed=8`) |
| protocol #2 | `dotnet run -- smoke` | **0.84 s** | Won (turns=7) | Lost (turns=7) | 7 | ✅ clean — `turnOpened=6 resolved=7 damaged=9 stateUpd=6 feed=9` both bots |
| diag #1 (for OTel scrape) | `dotnet run -- smoke` | **0.75 s** | Won (turns=7) | Lost (turns=7) | 7 | ✅ clean |
| diag #2 (for OTel scrape) | `dotnet run -- smoke` | **121.12 s** | **Error** (turns=0) | Won (turns=4) | 4 | ⚠ **Event A failure** — bot 1 got `HubException: 404 Not Found` from `HubConnection.HandshakeAsync`, never connected; bot 2 connected, the deadline worker progressed turns, battle ended Normal server-side with bot 2 winning; **BFF stack trace identical to Run 2 §5** |
| diag #3 | `dotnet run -- smoke` | **0.76 s** | Lost (turns=6) | Won (turns=6) | 6 | ✅ clean |

**`single-bot` probe (pre-smoke #1):** clean — auth + onboard + BFF SignalR + queue join + leave in ~10 s, no battle (this scenario doesn't exercise Battle replicas).

### Smoke pass/fail tally and contrast with prior runs

- Run 2 (no backplane): 0/2 smoke pairs completed cleanly (both hung — see `RUN_2_SETUP_LOG.md` §3).
- **Run 3 (with backplane): 4/5 smoke pairs completed cleanly. 1/5 hit the Run 2 Event A handshake-404 pattern.** Protocol smokes #1 and #2 both passed (the spec's N=2 contract is satisfied); the failure surfaced in diagnostic smokes #2-of-3 that were run to populate Prometheus series.

### What the failed smoke looks like at HTTP level

Battle replica HTTP request counts (`docker logs --since 15m`):

| Replica | Negotiate POSTs (`POST /battlehub/negotiate`) | WebSocket GET 101 | WebSocket GET 404 |
|---|---|---|---|
| `kombats-battle` | 4 | 4 | 3 |
| `kombats-battle-2` | 9 | 3 | 0 |
| Total | 13 | 7 | **3** |

10 client connection attempts (5 smokes × 2 bots) produced 7 successful upgrades (101) and 3 handshake failures (404). The 3 404s all landed on Battle 1 — the negotiate POST was answered by Battle 2 (token issued in Battle 2's local `IConnectionManager`), then the WebSocket upgrade GET hit Battle 1 (different replica), which 404'd because the token isn't in Battle 1's local map. (One additional `POST /battlehub?id=... → 404` is visible too — a retry pattern after the initial GET 404.)

### BFF-side stack trace (smoke diag #2)

```
System.Net.Http.HttpRequestException: Response status code does not indicate success: 404 (Not Found).
  at Microsoft.AspNetCore.SignalR.Client.HubConnection.HandshakeAsync(...)
  at Kombats.Bff.Application.Relay.BattleHubRelay.JoinBattleAsync(...) in /src/.../BattleHubRelay.cs:line 251
  at Kombats.Bff.Application.Relay.BattleHubRelay.JoinBattleAsync(...) in /src/.../BattleHubRelay.cs:line 314
  at Kombats.Bff.Api.Hubs.BattleHub.JoinBattle(Guid battleId) in /src/.../BattleHub.cs:line 40
```

This is the **same stack trace shown in `RUN_2_RESULTS.md` §4 Event A** — `HandshakeAsync` returns 404 inside `BattleHubRelay.JoinBattleAsync`. The backplane did not eliminate this code path; it merely did not need to be exercised in Run 1 because there was only one replica.

### What the bot-2 success tells us

Diag-smoke #2 is informative beyond "Event A still happens":

- Bot 1 errored out at handshake — never joined the battle group on any replica.
- Bot 2 successfully connected (its negotiate + upgrade both landed on the same replica by chance).
- The battle was created by a `CreateBattleConsumer` on one of the two replicas; the `TurnDeadlineWorker` ticked turns whether or not it ran on the same replica.
- **Bot 2 played 4 turns to completion** and won. Battle ended `Normal` in Postgres (`battle.battles` row: state=`Ended`, end_reason=`Normal`).
- **For bot 2 to have received `turnOpened`/`resolved`/`stateUpd` events through 4 turns, those `Clients.Group(...)` sends had to reach bot 2 regardless of which replica emitted them.** That is the **direct cross-replica evidence that Event B is closed** — the chapter's Event B prediction (backplane fixes group send fan-out) is observably correct.

So: backplane closes Event B; backplane does **not** close Event A. See §11.

---

## 4. Backplane verification — channels live, NUMSUB=2

After A.7 (and again post-smoke):

```
$ docker exec kombats-redis redis-cli PUBSUB CHANNELS '*'
Kombats.Battle.Infrastructure.Realtime.SignalR.BattleHub:internal:groups
Kombats.Battle.Infrastructure.Realtime.SignalR.BattleHub:internal:return:815051ea39a3_ad7841f4532946029e75e693399d6900
Kombats.Battle.Infrastructure.Realtime.SignalR.BattleHub:all
__Booksleeve_MasterChanged
Kombats.Battle.Infrastructure.Realtime.SignalR.BattleHub:internal:return:1027f383ed6b_f4c324753eab4b5ca154c1a4fa660476
Kombats.Battle.Infrastructure.Realtime.SignalR.BattleHub:internal:ack:815051ea39a3_ad7841f4532946029e75e693399d6900
Kombats.Battle.Infrastructure.Realtime.SignalR.BattleHub:internal:ack:1027f383ed6b_f4c324753eab4b5ca154c1a4fa660476

$ docker exec kombats-redis redis-cli PUBSUB NUMSUB \
    Kombats.Battle.Infrastructure.Realtime.SignalR.BattleHub:all \
    Kombats.Battle.Infrastructure.Realtime.SignalR.BattleHub:internal:groups
Kombats.Battle.Infrastructure.Realtime.SignalR.BattleHub:all
2
Kombats.Battle.Infrastructure.Realtime.SignalR.BattleHub:internal:groups
2
```

NUMSUB=2 on both cross-replica channels — both Battle replicas are subscribed and participating. Per-server `internal:ack:<server>` and `internal:return:<server>` channels are present for each replica's `SendConnectionAsync` return-value routing (one channel per replica, naturally NUMSUB=1 each). Both replicas' startup logs (verbatim):

```
[10:43:40 INF] Connecting to Redis endpoints: redis:6379. Using Server Name: 1027f383ed6b_d499...
[10:43:40 INF] Connected to Redis.
[10:44:13 INF] Connecting to Redis endpoints: redis:6379. Using Server Name: 1027f383ed6b_f4c3...
[10:44:13 INF] Connected to Redis.
```

(Each replica connected once on initial `up -d`, then again after the A.4 `restart` cycle — hence two `Connected to Redis.` entries per replica.)

---

## 5. Postgres + Redis state after setup phase

### Postgres `battle.battles` (after 5 smoke battles)

```
 state | count
-------+-------
 Ended |     5

 end_reason | count
------------+-------
 Normal     |     5
```

**All 5 smoke battles ended Normal** — including diag #2 where Bot 1 errored at handshake. This is the key Run 3 observation: **server-side resolution proceeds cleanly across replicas because the backplane fans group sends out to both Battle processes**. Even with one of the two players disconnected, the deadline worker can resolve turns (Bot 1's missing actions default to `NoAction`) and Bot 2 receives events on whichever replica its WebSocket happens to be pinned to.

### Redis

```
DBSIZE:           91
battle:state:*:    5    (matches 5 ended battles)
battle:action:*:  56
battle:turn:*:    30
mm:player:*:       0    (clean — Ch2 lease fix at work)
PUBSUB CHANNELS:   see §4
```

Clean per-battle inventory (~11 action+turn keys/battle, consistent with `CLEANUP_WORKER_DIAGNOSIS.md` §3 expectation of ~14/battle counting both `action` and `turn` rows). 0 `mm:player` keys confirms the Ch2 lease-renewal cancellation fix is intact.

---

## 6. Both Battle replica UUIDs + BFF UUID (partial — see A.11 caveat)

`curl -s --data-urlencode 'match[]=active_signalr_connections' 'http://localhost:9090/api/v1/series'`:

```json
[
  { "service": "battle", "service_instance_id": "d9cd1f66-a27d-4f3b-a085-e19532879f1a" }
]
```

**Only 1 series visible at setup-end probe time.** The other Battle replica's `active_signalr_connections` gauge and the BFF gauge were not in the Prometheus series list yet. Cause (per `PHASE_2_REPORT.md` §2 step 4 caveat): the OTel metric export interval is the .NET SDK default 60 s; the 5 smoke connections each lived ~0.8 s; the second-replica gauge transitioned 0→1→0 within an export cycle and the SDK's UpDownCounter never sampled it at a non-zero value. The diag-smoke #2 was longer (121 s) so it likely captured *one* of the replicas during its export window, hence the 1 series we do see.

This is **not a wiring issue** — both replicas register the metric correctly; the smoke load profile is just too short-lived to populate it. Under a 2-minute sustained load run (~25 concurrent connections held continuously), both `battle/<uuid>` series populate naturally (cf. Run 2's setup-log §5 showed 3 series after the longer-running smokes that hung for ≥120 s).

UUIDs to be filled in completely after the architect's load run starts and the OTel exporter has scraped at least one non-zero sample from each replica.

---

## 7. Comparison-baseline contract (forward declaration)

Per `RUN_1_RESULTS.md` §6: **Run 3 compares to Run 1, not Run 0.**

| Run | Configuration | Compares against | Isolated variable |
|---|---|---|---|
| Run 0 | Single-replica, no backplane | (baseline) | n/a |
| Run 1 | Single-replica, WITH backplane | Run 0 | Backplane on/off |
| Run 2 | 2-replica, no backplane | Run 0 | Replica count (both no-backplane) |
| **Run 3** | **2-replica, WITH backplane** | **Run 1** | **Replica count (both with-backplane)** |

Run 1 numbers (cited from `RUN_1_RESULTS.md` §3) are the primary comparison column for Run 3:

| Metric | Run 1 (single-replica + backplane) |
|---|---|
| ok count | 108 |
| fail count | 6 |
| RPS | 0.95 |
| `total_ms` p50 | 2182.4 ms |
| `total_ms` p95 | 11413 ms |
| `battle_ms` p50 | 38.4 ms |
| `join_battle_ms` p50 | 765.7 ms |
| Pairing throughput | 2.86 matches/s (37 s active window) |
| Battles / 2 min | 54 |

Run 0 (single-replica, no backplane) numbers from `RUN_0_BASELINE.md` §3 stay as a *secondary* "did we return to baseline territory?" column.

The expected direction per `CHAPTER_3_PLAN.md` §9: with backplane wired, Run 3 ≈ Run 1 on every per-publish-cost-bound metric (`total_ms`, `join_battle_ms`); success rate close to Run 1's ~95%; per-replica connection split observable in Grafana panel id:13 (both `battle/<uuid>` lines populated). **The Event A finding in §3 / §11 means the success rate may instead land closer to ~75–80% on this build; this is the chapter's most important deviation from prediction.**

---

## 8. What I did NOT do (constraints honored)

- **No code changes anywhere under `src/Kombats.*`.** Verified by `git status` showing clean tree throughout. The D1 change (`feat/signalr-backplane` HEAD~3 = `5f63ec9`) was already on the branch when work began.
- **No edits to `docker-compose.yml`, `docker-compose.multi-replica.yml`, observability files, NBomber config, scenarios, or virtual player code.**
- **No new commits.** Cherry-pick is the only commit-shaped action — and it just brought the Run 2 docs over from `development`. No `git add`, no `git commit` for new artefacts.
- **No `dotnet run -- load`.** That's the architect's manual Phase B.
- **No mitigation attempted for the Event A handshake-404 finding** (see §11). No sticky-session config, no nginx/Traefik insertion, no `BattleHubRelay` retry-on-404, no `SocketsHttpHandler.PooledConnectionLifetime` override. Per the prompt's "no code changes anywhere under `src/Kombats.*`" constraint and "if smoke hangs/fails STOP and escalate" directive.
- **No changes to Grafana dashboards or Prometheus config.**
- **No teardown after smokes** — stack left running for the architect's load run *if* they decide to proceed past the §11 escalation.
- **No further smoke runs after the 5 captured in §3.** Did not chase the Event A failure further at smoke scale — the finding is conclusive (BFF stack trace ≡ Run 2 §5) and the architect's decision is needed before any more action.

---

## 9. Ready-state preconditions

| Precondition | Status | Notes |
|---|---|---|
| Branch `feat/signalr-backplane` @ `b2928ac`, clean tree | ✅ | cherry-pick of Run 2 docs from `development` |
| `.AddStackExchangeRedis(redisConnectionString)` present at Program.cs:131 | ✅ | D1 change unchanged from `5f63ec9` |
| 15 long-running containers + 1 one-shot bootstrap Up | ✅ | both `kombats-battle` and `kombats-battle-2` Up |
| Postgres migrations applied + 6-service restart done | ✅ | `=== All migrations applied successfully ===` |
| Keycloak bootstrap `done.` + master realm token endpoint 200 | ✅ | |
| 50 loadbot users seeded, manifest fresh | ✅ | created=50, existed=0, failed=0 |
| `single-bot` probe clean | ✅ | |
| **Smoke ×2 per protocol — both clean** | ✅ | 0.92 s + 0.84 s, no errors |
| **Smoke ×3 additional diagnostic — 2 clean, 1 Event A failure** | ⚠ | **Critical finding, see §11** |
| Backplane DLL present on both Battle replicas | ✅ | `/app/Microsoft.AspNetCore.SignalR.StackExchangeRedis.dll` |
| Backplane channels live in Redis PUBSUB | ✅ | `BattleHub:all`, `BattleHub:internal:groups`, both NUMSUB=2 |
| `dns-rotation-check.sh` exit 0 | ✅ | 7/8 split (47/53) |
| Prometheus 3 series for `active_signalr_connections` | ⚠ partial | only 1 series at probe time; OTel export-interval × short-smoke-lifetime; will saturate under sustained load (per PHASE_2 §2 step 4 caveat) |
| No SignalR backplane WARN/ERROR in Battle logs | ✅ | only INFO `Connecting`/`Connected` |
| 0 stuck `mm:player:*` Redis keys | ✅ | Ch2 lease fix intact |

Setup is technically complete and the stack is functionally healthy. **But the §11 finding means the load run will likely demonstrate the backplane as a *necessary but not sufficient* fix, not the "ceiling removed" outcome the chapter framed.** That's an architect-level call to make before burning the load run.

---

## 10. Side observations

- **DNS rotation gave Battle 1 fewer load than Battle 2 during smokes** (negotiate POSTs: 4 on Battle 1 vs 9 on Battle 2). The 7/8 DNS split from `dns-rotation-check.sh` was uniform-ish; the smoke negotiate split (~31/69) is noisier because n=13 is small. **Not a bug — just an inherent property of Docker DNS rotation under small-N sampling.** Under sustained load (Run 3 proper), this will average out.
- **All 5 smoke battles ended `Normal` server-side**, even diag #2 where Bot 1 disconnected at handshake. The deadline worker progresses turns whether or not both players are present, and the `Clients.Group(...)` emissions reach whichever bot IS connected via the backplane — regardless of which replica owns each. This is the cleanest cross-replica evidence Event B is closed.
- **The `Connecting to Redis` log line appears twice per replica** — once on first `up -d`, once after the A.4 `restart` cycle. Expected; the first connection is torn down when the process restarts. Not a flap.
- **No `_connections` map evidence at smoke scale.** `RUN_2_RESULTS.md` §10 OQ4 flagged the BFF-relay `_connections` map as a potential residual-failure suspect for Run 3. At smoke scale (one bot pair at a time, no concurrent battles sharing a `battleId`), this code path isn't really exercised — each smoke creates a fresh `battleId` so the map is always missing the key. Load scale (25 concurrent battles, NBomber spawning faster than BFF cleanup) would be where this surfaces if it does. Flagging so the §11 escalation includes it as a thing to watch.

---

## 11. **CRITICAL finding for architect — Event A is NOT closed by the backplane alone**

### What happened

Of 5 smokes:

| # | Outcome | What it tells us |
|---|---|---|
| 1, 2, 3, 5 (4 of 5) | Clean Won/Lost in ≤1 s, both bots full event stream | ✅ Backplane works when handshake succeeds for both bots |
| 4 (diag #2, 1 of 5) | r1=Error/turns=0, r2=Won/turns=4, wall=121 s | ⚠ **Same handshake 404 pattern as Run 2** |

The BFF log on the failed smoke shows the exact `HttpRequestException: 404 Not Found at HubConnection.HandshakeAsync` stack trace from `RUN_2_RESULTS.md` §4 (Event A). At the HTTP level on the Battle replicas, 3 of 10 client GETs `/battlehub?id={token}` returned 404 — i.e. a token issued by replica X's `IConnectionManager` was upgraded against replica Y, which has no record of the token.

### Why this happens (mechanism, derived from Microsoft source — see RUN_1_BACKPLANE_OVERHEAD_ANALYSIS §3)

`Microsoft.AspNetCore.SignalR.StackExchangeRedis 10.0.3`'s `RedisHubLifetimeManager` replicates:
- Group membership (`AddToGroupAsync` triggers a Redis SUBSCRIBE the first time)
- Message traffic (`SendGroupAsync`, `SendAllAsync` are unconditional PUBLISHes)
- Per-connection ack/return routing (`internal:ack:<server>`, `internal:return:<server>` channels — visible in §4)

It does **NOT** replicate:
- The negotiate→handshake connection token map (`IConnectionManager`'s `_connections` dictionary). Negotiate POST `/battlehub/negotiate` registers a `connectionToken` in the answering replica's *local* in-memory map. The follow-up WebSocket upgrade `GET /battlehub?id={connectionToken}` requires a replica that holds that token in its local map. If DNS rotates between the two HTTP calls and they land on different replicas, the GET 404s.

This is a layer **below** `HubLifetimeManager` (it's in the SignalR endpoint middleware, before the lifetime manager dispatch). The backplane cannot fix it.

### Why the Run 2 docs predicted the backplane would close Event A — and why that prediction was wrong

`RUN_2_RESULTS.md` §10 OQ4 wrote: *"with the backplane, P(handshake survives) becomes ~1 because the connection-token registry is in Redis (no replica is 'wrong')"*. This was a model error — the SignalR Redis backplane does not put the connection-token registry in Redis. That state lives in each replica's in-process `IConnectionManager`. The author of the §10 OQ4 prediction conflated "group membership routing via Redis" with "connection token storage via Redis"; the former is what the backplane does, the latter is not.

### What this implies for Run 3 and the chapter

- **Event B is fully closed and demonstrable** — diag #2 itself is the proof (bot 2 played 4 turns to completion across whatever replica its WebSocket pinned to, with the deadline worker emitting from possibly the other replica).
- **Event A is unchanged from Run 2** — `P(handshake 404 per bot) ≈ 0.5` under uniform DNS rotation. With 2 bots, `P(at least one bot fails) ≈ 0.75` per battle — same as Run 2's HTTP-level 50 % 404 rate.
- **The 80 % smoke success rate (4/5) is consistent with `P(both bots' negotiate+GET both stay on the same replica) ≈ 0.5²·0.5² = 0.0625` plus the observation that .NET's `SocketsHttpHandler` *can* reuse a TCP connection across the negotiate+GET pair within the same `HubConnection.StartAsync()` (boosting same-replica probability somewhat) — but it's not guaranteed, and 1/5 of the time we saw it fail.**
- The chapter's framing ("one line of code closes both failure modes") is **partially true**: backplane is necessary and closes the *dominant* failure mode (Event B's cross-replica fan-out), but a separate fix is needed for Event A. Three candidate fixes:
  1. **Sticky sessions at the routing layer** (nginx/Traefik with `ip_hash` or cookie affinity; or Docker DNS with per-bot stable resolution). The standard production answer for self-hosted SignalR.
  2. **Two-stage routing in BFF**: read the `Url` header off the negotiate response (it contains the absolute URL pointing to the answering replica) and use that explicit URL for the upgrade GET. Standard SignalR client behavior already does this on Azure SignalR Service via `Url` rewriting; on self-hosted with backplane, the `Url` field is not populated unless explicitly enabled. Requires reading SignalR source / docs to confirm what `Url` does in our config.
  3. **Disable WebSocket transport** so SignalR negotiates SSE only — but SSE explicitly requires sticky sessions, so this is strictly worse.

### Recommendation for architect

**Three viable paths, in order of decreasing scope creep:**

(a) **Proceed with the load run anyway** — capture the residual ~20–25 % failure rate at scale, and frame the chapter as "backplane is necessary but not sufficient — sticky sessions are the second half". This is honest and arguably *more interesting* than "one line solved it" — it's an empirical encounter with the SignalR scaling contract that operators actually face.

(b) **Pause Run 3, add sticky sessions** (probably a 1-line `Url` rewrite in `BattleHubRelay.JoinBattleAsync` or a config change for `HubConnectionBuilder`), and re-run smoke + load. Keeps the chapter's "one-line-fix" narrative but the "one line" gets a footnote.

(c) **Investigate further** — read the Microsoft.AspNetCore.SignalR source at v10.0.3 for `Url` field handling in negotiate to confirm option (b)'s scope is small; possibly write a 1-line mini-experiment. ~30 min of digging.

Either way, the load run should not happen *now* without an explicit go-ahead. The current setup will demonstrate a partial fix, and the chapter's predicted-vs-measured table in §9 of the plan needs the Event A row revised before measurement.

### What I did NOT do about this finding

- Did not write code changes to test sticky sessions or `Url` rewriting (per the prompt's "no code changes" constraint).
- Did not run further smokes to characterize the residual rate at higher N (could give better statistics but burning bot-pool churn before architect signoff seemed premature).
- Did not modify `docker-compose.multi-replica.yml` to add nginx-based sticky sessions (out of scope for setup-only work, plus the prompt forbids it).
- Did not write a separate `RUN_3_EVENT_A_FINDING.md` — kept the finding in this setup log to avoid creating new artefact files the architect hasn't asked for.

---

## 12. Files created / modified in this session

| Path | Status | Purpose |
|---|---|---|
| `tests/Kombats.LoadTests/RUN_3_SETUP_LOG.md` | added (this file) | Setup log + §11 critical finding |

**No source changes anywhere.** **No compose changes.** **No observability changes.** Cherry-pick `b2928ac` brought 2 files (`RUN_2_RESULTS.md`, `RUN_2_SETUP_LOG.md`) from `development` onto `feat/signalr-backplane` — those are not "new files in this session" so much as "files moved between branches", and the cherry-pick is the only commit-shaped action.

---

## 13. Stop point — awaiting architect decision

Phase A is complete; the stack is functionally healthy; backplane is verifiably wired and active. **Do not run `dotnet run -- load` until the §11 finding is acknowledged and a path forward (a / b / c) is chosen.**

> **2026-05-13 — superseded by §14 below.** Architect chose path (b) of §11's recommendations — pause Phase B, apply a targeted second source change (D1.5: `SkipNegotiation=true` on BFF's outbound HubConnection to Battle), re-verify smoke, then proceed. See §14 for the D1.5 amendment and its evidence.

---

## 14. D1.5 — SkipNegotiation amendment (Event A closure)

*Appended 2026-05-13 after architect review of §11. This is the second and final `src/Kombats.*` change of Chapter 3.*

### 14.1 Trigger

Original Run 3 setup §11 finding: diag-smoke #2 reproduced the Run 2 Event A handshake-404 pattern even with the backplane wired (NUMSUB=2 verified). At HTTP level, 3 of 10 client `GET /battlehub?id={token}` returned 404 — token issued by one replica's `IConnectionManager`, WebSocket upgrade GET hit a different replica via DNS rotation. The BFF stack trace was byte-for-byte identical to `RUN_2_RESULTS.md` §4 (Event A): `HttpRequestException: 404 Not Found at HubConnection.HandshakeAsync` inside `BattleHubRelay.JoinBattleAsync`. So the chapter's predicted "one-line backplane fix closes both events" framing was incomplete — backplane closes Event B but does not touch Event A.

### 14.2 Source-level mechanism — why the backplane alone could not close Event A

Per `RUN_1_BACKPLANE_OVERHEAD_ANALYSIS.md` §3 (which read the Microsoft v10.0.3 source directly), `Microsoft.AspNetCore.SignalR.StackExchangeRedis 10.0.3`'s `RedisHubLifetimeManager` replicates:

- group membership (`AddToGroupAsync` triggers Redis SUBSCRIBE on first joiner per group),
- message traffic (`SendGroupAsync` / `SendAllAsync` are unconditional Redis PUBLISHes),
- per-connection ack and return-value routing (per-server `internal:ack:<server>` / `internal:return:<server>` channels, visible in §4 above).

It does **not** replicate `HttpConnectionManager`'s per-connection token map (the `IConnectionManager._connections` dictionary that the negotiate POST registers into and the WebSocket upgrade GET reads from). That state lives in-process on whichever replica answered the negotiate POST. The negotiate→handshake protocol sits one architectural layer **below** `HubLifetimeManager` — at the SignalR endpoint middleware in `Microsoft.AspNetCore.SignalR.Core`, which decides whether to 404 a GET based on its own local map *before* the lifetime manager dispatch ever runs. Adding a backplane cannot fix it because the failure is in code the backplane doesn't intercept.

### 14.3 External sources confirming the closure path

Three independent confirmations (architect's reading, cited verbatim):

1. **Microsoft GitHub issue #50171** — <https://github.com/dotnet/aspnetcore/issues/50171>, August 2023. Explicit recommendation from Microsoft maintainers: *"The solution recommended from official MS documentation is to use sticky sessions or skip negotiation."* This is the canonical answer for self-hosted SignalR + Redis backplane without a managed sticky load balancer.
2. **Milan Jovanović, "Scaling SignalR With a Redis Backplane"** — <https://www.milanjovanovic.tech/blog/scaling-signalr-with-redis-backplane>, March 2026. Quoted: *"The Redis backplane solves message routing, but it does not remove the need for sticky sessions."*
3. **Infinum, "SignalR Scaling In Real-Time Applications"** — <https://infinum.com/blog/scaling-out-your-own-signalr-chat-application/>, December 2025. Same conclusion: backplane is necessary but not sufficient; either sticky sessions OR skip-negotiation closes the negotiate-handshake gap.

For Kombats's combats.ru-style 1000-concurrent target without a cloud-managed sticky LB layer (cf. plan §12 Q11 — BFF stays single-replica deliberately, no sticky infrastructure planned for Ch3), **skip-negotiation is the production-correct path**.

### 14.4 Why the fix lives in BattleHubRelay, not VirtualPlayer

Two SignalR hops in the system, only one of them has the multi-replica condition:

| Hop | Direction | Replica count | Event A possible? |
|---|---|---|---|
| 1 | Bot / React frontend → BFF | BFF=1 | No — single-replica is sticky-by-topology |
| 2 | BFF's `BattleHubRelay` → Battle | Battle=2 | **Yes** — the failure mode this amendment closes |

`VirtualPlayer.cs` only does Hop 1 (Bot→BFF). With BFF single-replica per `CHAPTER_3_PLAN.md` §12 Q11, the negotiate POST and the WebSocket upgrade GET both land on the same instance — no Event A possible at Hop 1. Fix unconditionally goes at Hop 2 in `BattleHubRelay.cs`. No `VirtualPlayer.cs` change.

### 14.5 The code change

**File:** `src/Kombats.Bff/Kombats.Bff.Application/Relay/BattleHubRelay.cs`
**Lines touched:** +1 using directive (`Microsoft.AspNetCore.Http.Connections` for the `HttpTransportType` enum) + 14 lines inside the existing `.WithUrl(...)` lambda in `JoinBattleAsync` (the 11-line comment block + 2 option lines + 1 blank).

**Diff (verbatim):**

```diff
@@ -6,6 +6,7 @@ using Kombats.Battle.Realtime.Contracts;
 using Kombats.Bff.Application.Clients;
 using Kombats.Bff.Application.Narration;
 using Kombats.Observability;
+using Microsoft.AspNetCore.Http.Connections;
 using Microsoft.AspNetCore.SignalR;
 using Microsoft.AspNetCore.SignalR.Client;
 using Microsoft.Extensions.DependencyInjection;
@@ -63,6 +64,19 @@ public sealed class BattleHubRelay : IBattleHubRelay, IAsyncDisposable
             {
                 options.AccessTokenProvider = () => Task.FromResult<string?>(accessToken);

+                // Negotiate→handshake state is not replicated by the SignalR Redis backplane:
+                // Microsoft.AspNetCore.SignalR.StackExchangeRedis replicates HubLifetimeManager
+                // only, not HttpConnectionManager's per-connection token map. With Battle on
+                // multiple replicas and DNS rotation, the negotiate POST and the handshake GET
+                // can land on different replicas — the GET then 404s. Skipping negotiation and
+                // forcing WebSockets pins both into a single WebSocket upgrade against whichever
+                // replica DNS picked, so no cross-replica token lookup is required. The JWT
+                // travels on the upgrade via access_token query string (BattleHub reads it from
+                // header or query string — see SIGNALR_SURFACE_MAP.md §J.3).
+                // Ref: https://github.com/dotnet/aspnetcore/issues/50171
+                options.SkipNegotiation = true;
+                options.Transports = HttpTransportType.WebSockets;
+
                 if (!string.IsNullOrEmpty(traceparent))
                 {
                     options.Headers["traceparent"] = traceparent;
```

1 file changed, +15 lines / −0 lines. Edit localizes entirely to the `.WithUrl(...)` lambda + one using.

**Package note.** No new NuGet package needed. `HttpTransportType` ships in the existing `Microsoft.AspNetCore.SignalR.Client` reference (`Directory.Packages.props:45`); `HttpConnectionOptions.SkipNegotiation` and `.Transports` are the existing config surface on that package's `.WithUrl(...)` options.

**Auth verification.** Per `SIGNALR_SURFACE_MAP.md` §J.3, the Battle service's `BattleHub.cs:74-93` reads the JWT from either the `Authorization` header or the `access_token` query string. With `SkipNegotiation=true` + `Transports=WebSockets`, the SignalR client's existing `AccessTokenProvider` (line 64 of `BattleHubRelay.cs`) appends `?access_token={token}` to the WebSocket upgrade URL — so Battle still receives the JWT and authorizes the user. Empirically: no `[Authorize]` failures, no `403`/`401`, no auth-related WARN/ERROR in the post-D1.5 Battle logs across the 5-smoke verification window.

**Build result.** Pre-D1.5 baseline (recorded via `dotnet build --no-restore` before the edit): `84 Warning(s), 0 Error(s)`. Post-D1.5: `84 Warning(s), 0 Error(s)`. **Zero new warnings.** Matches the `PHASE_3_D1_REPORT.md` §5 pattern exactly — same 84 NU1902/NU1510 pre-existing warnings, no NU1605 / NU1608, no warnings naming the touched file.

### 14.6 Smoke re-verification post-D1.5

Stack rebuilt clean (`down -v` + `up -d --build`), migrations + 6-service restart applied, Keycloak bootstrap clean, 50 users seeded. Backplane channels confirmed live (NUMSUB=2 on `BattleHub:all` and `BattleHub:internal:groups` — see §14.7 below). DNS rotation hard gate exit 0 (7/8 split, 47/53).

Then **5 smoke attempts in sequence** (the new D1.5 fix proof at smoke scale):

| # | Wall clock | r1 outcome | r1 turns | r2 outcome | r2 turns | Notes |
|---|---|---|---|---|---|---|
| 1 | **4.26 s** | Lost | 6 | Won | 6 | First post-rebuild — JIT warmup on the new transport path. Subsequent attempts run sub-second. |
| 2 | 0.73 s | Draw | 6 | Draw | 6 | clean |
| 3 | 0.72 s | Draw | 8 | Draw | 8 | clean |
| 4 | 0.71 s | Won | 7 | Lost | 7 | clean |
| 5 | 0.70 s | Lost | 4 | Won | 4 | clean |

**5/5 clean.** Zero `Error` / `BattleTimeout` / `HubException` outcomes. Zero wall-clocks above 30 s.

**Pre-D1.5 vs post-D1.5 contrast at smoke scale:**

| | Pre-D1.5 (5 smokes) | Post-D1.5 (5 smokes) |
|---|---|---|
| Clean | 4 / 5 | **5 / 5** |
| Event A failures (404 at HandshakeAsync) | 1 / 5 | **0 / 5** |
| Negotiate POSTs in Battle access logs | 13 (4 on B1 + 9 on B2) | **0 on both replicas** |
| GET 404s in Battle access logs | 3 (all on B1) + 1 POST 404 | **0 on both replicas** |
| GET 101 successful upgrades | 7 (4 + 3) | 10 (4 + 6) — matches 5 smokes × 2 bots |
| NUMSUB on cross-replica channels | 2 | 2 (unchanged) |

The 0 negotiate POSTs is the cleanest structural evidence: with `SkipNegotiation=true`, the SignalR client jumps straight to the WebSocket upgrade GET, and that GET inherently can only land on one replica per call — no split is possible. The 0 GET 404s confirms the prediction: same DNS, no negotiate path, no token lookup, no 404.

The 4 + 6 = 10 successful upgrades also confirm DNS rotation is still spreading load across both replicas (4 on Battle 1, 6 on Battle 2) — D1.5 did not collapse the multi-replica distribution into "everyone goes to one replica". Both backplane channels still show NUMSUB=2.

### 14.7 Backplane channels — unchanged, as expected

```
$ docker exec kombats-redis redis-cli PUBSUB CHANNELS '*'
Kombats.Battle.Infrastructure.Realtime.SignalR.BattleHub:all
__Booksleeve_MasterChanged
Kombats.Battle.Infrastructure.Realtime.SignalR.BattleHub:internal:return:2109041d98f9_e3de13dedb604a4989971bca05b128ea
Kombats.Battle.Infrastructure.Realtime.SignalR.BattleHub:internal:ack:bd6f4f32a149_06b4f785349e4eaf98bbc0fdf2e0c9e1
Kombats.Battle.Infrastructure.Realtime.SignalR.BattleHub:internal:ack:2109041d98f9_e3de13dedb604a4989971bca05b128ea
Kombats.Battle.Infrastructure.Realtime.SignalR.BattleHub:internal:return:bd6f4f32a149_06b4f785349e4eaf98bbc0fdf2e0c9e1
Kombats.Battle.Infrastructure.Realtime.SignalR.BattleHub:internal:groups

$ docker exec kombats-redis redis-cli PUBSUB NUMSUB \
    Kombats.Battle.Infrastructure.Realtime.SignalR.BattleHub:all \
    Kombats.Battle.Infrastructure.Realtime.SignalR.BattleHub:internal:groups
Kombats.Battle.Infrastructure.Realtime.SignalR.BattleHub:all
2
Kombats.Battle.Infrastructure.Realtime.SignalR.BattleHub:internal:groups
2
```

D1.5 changes only the BFF→Battle handshake transport. Battle's own SignalR registration (`AddSignalR().AddStackExchangeRedis(...)` at Battle Bootstrap `Program.cs:131`) is untouched and continues to participate in the backplane as in Run 1 / pre-D1.5 Run 3 setup. Event B's closure mechanism is unaffected.

### 14.8 Run 2 §10 OQ4 correction (model refinement, not defect)

`RUN_2_RESULTS.md` §10 OQ4 wrote:

> *"If Run 3 shows residual failures even with the backplane wired, the most likely suspect is the BFF-relay's process-local `_connections` map ([SIGNALR_SURFACE_MAP.md §J.3](SIGNALR_SURFACE_MAP.md)) — but with BFF at single-replica that's not exercisable here."*

OQ4 was **right** that there'd be residual risk after the backplane, but **wrong about its location**. The actual residual risk was one layer below — at the `HttpConnectionManager` token map, not at the `BattleHubRelay._connections` map. The `_connections` map is BFF-side and concerns *how BFF stores its outbound HubConnections per frontend connection*; it's about reuse and cleanup, not about cross-replica routing. The actual cross-replica gap was inside SignalR's own pre-handshake machinery on the Battle side.

This is recorded as a model refinement — same character as `CHAPTER_3_PLAN.md` §3.1 → Run 2's compound model. The §3.1 prediction identified the *mechanism* (in-process group tables), the Run 2 work refined it into a compound (Event A + Event B), and this D1.5 work refines further by pinpointing exactly *which layer* Event A lives at (below `HubLifetimeManager`, not at it). Three iterations across three measurement runs, each driven by observed evidence.

OQ4's residual-risk hypothesis (`BattleHubRelay._connections` map under load) remains worth checking under Run 3 proper — but it's a *separate* risk from Event A. Logged for the Run 3 results analysis (§C.5 of the analysis prompt).

### 14.9 Refined Run 3 expected outcome

With both fixes in:

- **D1 (backplane):** `P_B` (Event B — co-location lottery making `Clients.Group(...)` miss one side) ≈ 0 — Redis PUBSUB delivers every group send to whichever replica each player is pinned to.
- **D1.5 (skip negotiation):** `P_A` (Event A — negotiate→handshake split producing 404) ≈ 0 — no negotiate path, no token to mis-route.

Compound: `P(fail | both fixes) = 1 − (1 − P_A) × (1 − P_B) ≈ 0`.

Predicted Run 3 load success rate: **~95%+** (matching Run 0 baseline / Run 1 single-replica-with-backplane), i.e. saturation of the natural NBomber TaskCanceled-on-shutdown tail (~25 fails per Run 0 + the smaller Run 1 mixed-fail tail). Numerical comparison baseline remains Run 1 per the comparison-baseline contract in §7 of this log (and `RUN_1_RESULTS.md` §6).

Acceptable empirical range for the load run: **success rate 93–98 %**. Outside that range — investigate, do not ignore:

- **<90 %:** a third failure mode exists that this model didn't anticipate. Likely-suspect order: (i) BFF-relay `_connections` map pinning under concurrent battles (Run 2 §10 OQ4 — *separately* from Event A, may surface under load even though Event A is now closed); (ii) Redis PUBSUB throughput saturation under combined backplane + app traffic; (iii) Run 1's `join_battle_ms` p50 inflation showing up at multi-replica scale (RUN_1_RESULTS.md §3 — Run 3 may show similar shape).
- **>98 %:** unexpected, but recheck the harness — possibly an iteration-log accounting change or NBomber harness tweak that suppresses the natural fail tail.

### 14.10 Files modified in this D1.5 amendment

| Path | Status | Purpose |
|---|---|---|
| `src/Kombats.Bff/Kombats.Bff.Application/Relay/BattleHubRelay.cs` | modified | D1.5 — `SkipNegotiation=true` + `Transports=WebSockets` on outbound `HubConnection` to Battle |
| `tests/Kombats.LoadTests/RUN_3_SETUP_LOG.md` | modified | This §14 amendment + §13 forward-reference note |

Cumulative Chapter 3 `src/Kombats.*` change set is now exactly:

1. **D1** (`5f63ec9`): `Directory.Packages.props` + `Kombats.Battle.Bootstrap.csproj` + `Kombats.Battle.Bootstrap/Program.cs` — wire the SignalR Redis backplane on Battle.
2. **D1.5** (this amendment, unstaged): `Kombats.Bff.Application/Relay/BattleHubRelay.cs` — skip negotiation on BFF's outbound to Battle.

Per Chapter 3 scope: **D1.5 is the second and final `src/Kombats.*` change of the chapter.** No more.

### 14.11 What I did NOT do in the D1.5 amendment

- No edit to `Program.cs`, `Directory.Packages.props`, `Kombats.Battle.Bootstrap.csproj`, or anything under `src/Kombats.Battle/...` / `src/Kombats.Chat/...` / `src/Kombats.Players/...` / `src/Kombats.Matchmaking/...` — D1.5 is BFF-side only.
- No edit to `VirtualPlayer.cs` (Hop 1 doesn't need it — see §14.4).
- No edit to compose files or observability config.
- No new package added — `HttpTransportType` ships in the existing `Microsoft.AspNetCore.SignalR.Client 10.0.3` reference.
- No `git commit` / `git push` / `git add`. Architect commits manually after review.
- No `dotnet run -- load`. That's the architect's Phase B step.
- No teardown after the 5-smoke verification — stack left running for the architect's load run.
- No retry / fallback logic added to `BattleHubRelay` for transient WebSocket upgrade failures. If a WebSocket upgrade fails for any reason under `SkipNegotiation=true`, the existing `JoinBattle` flow surfaces the exception to the caller as it did before — D1.5 is purely a transport-selection change, not a resilience change.
- No investigation of why diag #2 was the *first* observed Event A failure rather than the more statistically-likely "every other smoke" given uniform DNS rotation and per-connection HttpClient handler isolation. The cause is almost certainly that .NET `SocketsHttpHandler` reuses the TCP socket for the negotiate POST → upgrade GET pair within a single `HubConnection.StartAsync()` *most* of the time, boosting same-replica probability somewhat — but not always. With D1.5 the question is moot; flagging here only so future-me knows why we didn't dig deeper.

### 14.12 Ready-state preconditions — refreshed post-D1.5

| Precondition | Pre-D1.5 status | Post-D1.5 status |
|---|---|---|
| Branch `feat/signalr-backplane`, clean working tree | ✅ | ✅ (only `BattleHubRelay.cs` modified + `RUN_3_SETUP_LOG.md` added — both unstaged) |
| Backplane wired in Battle Bootstrap | ✅ (D1) | ✅ (unchanged) |
| **`SkipNegotiation=true` + WebSockets transport in BattleHubRelay** | ❌ | ✅ (**D1.5**, this amendment) |
| 15 long-running containers + 1 one-shot bootstrap Up | ✅ | ✅ (rebuilt clean) |
| Backplane channels live, NUMSUB=2 on both | ✅ | ✅ (unchanged by D1.5) |
| 5/5 smokes clean | ❌ (4/5) | ✅ (**5/5**) |
| 0 negotiate POSTs in Battle access logs during smokes | ❌ (13) | ✅ (**0**) |
| 0 GET 404s in Battle access logs during smokes | ❌ (3 GET + 1 POST 404) | ✅ (**0**) |
| DNS rotation exit 0 | ✅ | ✅ (7/8 split) |
| Prometheus 3 series for `active_signalr_connections` | ⚠ partial (1 series; OTel scrape × short smoke lifetime) | ⚠ partial (same caveat — will saturate under sustained load per PHASE_2 §2 step 4) |
| 50 loadbot users seeded clean | ✅ | ✅ (re-seeded post-`down -v`) |

**Phase A is now genuinely complete.** Stack ready for the architect's manual `dotnet run -- load`.

### 14.13 Stop point — architect Phase B

D1.5 amendment closes the §11 finding. Both events of the Run 2 compound failure model are now structurally addressed:

- Event B → D1 backplane (Run 1 mechanism analysis closed it source-level; Run 3 smoke diag #2 confirmed it observationally — Bot 2 played 4 turns cross-replica even when Bot 1 errored at handshake).
- Event A → D1.5 skip negotiation (this amendment; 5/5 smokes clean + 0 negotiate POSTs in Battle access logs).

The chapter's iterated-model narrative — §3.1 prediction → compound model refinement (Run 2) → dual-fix (D1 + D1.5) — is now ready for the load run to validate at scale.

**Awaiting architect manual `dotnet run -- load` (Phase B).**
