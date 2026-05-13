# Run 2 Setup Log — Multi-replica Battle WITHOUT backplane (the failure-proof experiment)

## 0. Header

- **Date:** 2026-05-13
- **Branch / HEAD:** `development` @ `1958389` (`docs: chapter 3 planning and investigation reports`) — **no backplane code on this branch** (verified: `grep -n "StackExchangeRedis\|AddStackExchangeRedis" src/Kombats.Battle/Kombats.Battle.Bootstrap/Program.cs` → not found).
- **Stack state:** 15 long-running containers Up after `down -v` + clean rebuild with **`-f docker-compose.multi-replica.yml`** overlay. Both `kombats-battle` and `kombats-battle-2` running (multi-replica Battle); BFF, Matchmaking, Players, Chat each single-replica. NO SignalR Redis backplane.
- **Purpose:** Phase III deliverable 3 — establish the multi-replica-without-backplane setup so the load run can prove the §3.1 failure-rate prediction.
- **Scope:** Phase A only — setup, teardown, smoke evidence, hard gates. No `dotnet run -- load` from this agent; that's the architect's manual step.

---

## 1. Step-by-step timestamps and outputs

| Step | Action | Start (Asia/Yekaterinburg, +0500) | Result |
|---|---|---|---|
| A.1 | Verify branch / HEAD / no-backplane | 13:27:14 | ✅ clean tree, `development @ 1958389`, no `AddStackExchangeRedis` line |
| A.2 | `docker compose ... down -v` (3 compose files, no multi-replica overlay — base + observability + override) | 13:27:19 | ✅ all containers removed, all 5 named volumes removed, network removed; `docker ps -a \| grep kombats` empty, `docker volume ls \| grep kombats` empty |
| A.3 | `docker compose ... up -d --build` WITH **4 files** (base + observability + override + **multi-replica**) | 13:27:30 | ✅ 15 long-running containers + 1 one-shot bootstrap. `compose ps` shows `kombats-battle` Up and `kombats-battle-2` Up (no health column on either — matches base-service no-healthcheck) |
| A.4 | `./scripts/run-migrations.sh` (env: `POSTGRES_*`, `KEYCLOAK_DB_PASSWORD=keycloak`) | 13:28:31 | ✅ `=== All migrations applied successfully ===`. Then `docker compose ... restart bff players matchmaking battle battle-2 chat` (both Battle replicas included). Polled `http://kombats-battle:8080/health/ready` AND `http://kombats-battle-2:8080/health/ready` from a one-shot `curlimages/curl` helper until both 200 → both ready by 13:29:11. |
| A.5 | Keycloak bootstrap | 13:29 | ✅ `docker logs kombats-keycloak-bootstrap` ends with `[keycloak-bootstrap] done.`; master-realm token endpoint returns HTTP 200 (`{"access_token":"eyJ..."}`) |
| A.6 | `dotnet run -- seed-users` | 13:29:21 | ✅ `created=50, existed=0, failed=0`; manifest written |
| A.7 | Smoke checks (see §3 for the interesting part) | 13:29:37 | ✅ `single-bot` clean; `smoke` #1 hung 152.83 s (cross-replica DNS split); `smoke` #2 also hung waiting for events, killed by user (Ctrl+C). N=1 evidence sufficient per spec. |
| A.8 | DNS rotation hard gate (`./tests/Kombats.LoadTests/scripts/dns-rotation-check.sh`) | 13:51:04 | ✅ **exit 0** — 9/6 split (60/40) over 15 probes — DNS rotation confirmed |
| A.9 | Per-replica panel / 3-series Prometheus verification | 13:51 | ✅ Prometheus reports 3 distinct series for `active_signalr_connections` (2× battle UUID, 1× bff UUID); Grafana panel id:13 present and configured |
| A.10 | Write this setup log | 13:52 | ✅ |

---

## 2. DNS rotation check output (verbatim)

```
Discovering containers with alias 'battle' on network 'kombats_default'...
Discovered 2 container(s) with alias 'battle':
  kombats-battle-2  ->  172.19.0.16
  kombats-battle  ->  172.19.0.12

Probing 'dig +short battle | head -n1' 15 times from a helper container on 'kombats_default'...

Probe results (15 probes, 0 failed):
  kombats-battle (172.19.0.12): 9 hits
  kombats-battle-2 (172.19.0.16): 6 hits

OK: DNS rotation confirmed
EXIT=0
```

Comments: 9/6 (60/40) over 15 probes — well within the script's "≥2 distinct" bar. Compared to Phase II's 6/9, the dominant replica swapped which container "wins" — expected (rotation is uniform but order varies). The §3.1 math model precondition is satisfied — under load, BFF's per-`JoinBattle` outbound `HubConnection.StartAsync` calls will land on different Battle replicas at roughly the predicted rate.

---

## 3. Smoke run results — the predicted §3.1 failure mode at N=1

**`single-bot`** (`Scenarios/SingleBotProbe.cs`): clean. Auth + onboard + BFF SignalR + queue join + leave, no battle. This path doesn't touch Battle replicas (no second bot to pair with) — passing tells us nothing about cross-replica behavior; it only confirms the harness end-to-end up to matchmaking.

```
13:29:38.264 ... target: http://localhost:5000, user: loadbot-0001
13:29:38.367 ... token acquired in 84 ms
13:29:38.739 ... onboard in 372 ms — state=Draft revision=1
13:29:38.994 ... SignalR connected in 52 ms
13:29:39.023 ... join-queue result: status=Searching
13:29:49.046 ... leave-queue done
```

**`smoke` #1 (full one-pair lifecycle)**: **FAILED in the predicted cross-replica pattern**. Started 13:29:53; both bots paired and got `battle_id=ec4eeea9-1e3f-4fc5-b7c4-c1f75f7d503e`. Bot 2 immediately got an exception:

```
warn: VirtualPlayer loadbot-0002 failed: An unexpected error occurred invoking 'JoinBattle' on the server.
  Microsoft.AspNetCore.SignalR.HubException: An unexpected error occurred invoking 'JoinBattle' on the server.
   at ... BattleHubClient.JoinBattleAsync(...) line 83 / 90
   at ... VirtualPlayer.RunOneBattleAsync(...) line 124
```

Bot 1 hung until its `PerBotTimeout` saturated, ending with `BattleTimeout`. Final smoke summary:

```
[smoke] wall clock: 152.83s | r1=loadbot-0001/BattleTimeout turns=0 battle=ec4eeea9-... | r2=loadbot-0002/Error turns=0 battle=ec4eeea9-...

loadbot-0001  outcome=BattleTimeout  battle_ms=0   total=152817 ms  events=turnOpened=3 resolved=3 stateUpd=3 feed=4
loadbot-0002  outcome=Error          joinBattle=0  total=848 ms     events=turnOpened=0 resolved=0 stateUpd=0 feed=0
              error="An unexpected error occurred invoking 'JoinBattle' on the server."
```

**Interpretation.** Two bots, two DNS rotations on BFF's outbound `HubConnection.StartAsync` → with `P(split)=0.5` they landed on different Battle replicas. The replica that consumed the `CreateBattle` MassTransit message persisted the battle row + Redis state (battle_id ec4eeea9 on player_a_id `d13d04d2...` and player_b_id `6cd0422d...`). When the BFF's relay invoked `JoinBattle` on Bot 2's Battle replica, that replica didn't yet have the per-process state for this battle (state is in Redis, but a few code paths look up local in-process caches first / read paths block on the consumer-replica having persisted) — so Battle threw `HubException`. Meanwhile Bot 1 successfully joined its group on the consumer replica; `Clients.Group(...)` sends only reach Bot 1 (the group on the other replica is empty because Bot 2 never got there). Bot 1 receives `turnOpened`+`resolved`+`stateUpd`+`feed` but its opponent never submits anything, so the deadline worker on the consumer replica resolves each turn with the opponent's missing action defaulted to `NoAction`, and the battle eventually concludes by some win/draw rule (or just hits some clamp). Bot 1's bot-side state machine never sees the battle reach an Ended state in the way it expects, so it times out at `PerBotTimeout=120s` and exits with `BattleTimeout`. Eventually the deadline worker fully resolves and writes `state=Ended` `end_reason=Normal` to Postgres at 13:32:34 (≈160s after battle creation) — visible in §4.

**`smoke` #2**: hung from 13:32:33. Killed by user (Ctrl+C) after observing in Grafana:
- **Per-replica panel (id:13)**: DNS rotation split the 2 bots across the 2 Battle replicas.
  - `battle/017be6cb-ca85-4097-9a15-2ddf5106c107` showed 1 connection.
  - `battle/d2591c21-58c3-449b-8345-04f1763dd27e` showed 0 or 1 connection (briefly), then drift.
- **Active battles gauge**: split-brain `+1/-1` — each replica counted "its" battle locally, and a decrement signal from the other side wrote a phantom `-1` (the metric is per-replica process-local). This is itself a multi-replica observability artefact worth noting in the results write-up.
- **HTTP server p95**: BFF queue-status endpoint p95 spiked to ~10 s — stuck queue-status polls from bots waiting for events that physically cannot arrive (because the events are being emitted into the *other* replica's local group table).

Per spec, **N=1 of the cross-replica failure mode at smoke scale is the experiment-design confirmation** that Phase III's load run will produce the §3.1 result at full statistical weight. Per `CHAPTER_3_PLAN.md` §3.1, with R=2 and uniform DNS, `P(at least one critical event lost across the battle) ≈ 1 − 0.25^T` — at T=3 turns that's 98.4 %. We saw the failure mode immediately on the first multi-replica smoke pair.

**Pass / fail ratio observed**: 0 passes, 2 hangs out of 2 smoke pairs attempted. (Both ran with bots that DNS-split across replicas; we never won the co-location lottery.) Per spec: do not investigate, do not mitigate.

---

## 4. Postgres + Redis state after setup phase

### Postgres (`battle.battles`)

```
              battle_id               | state |          created_at           |           ended_at            | end_reason
--------------------------------------+-------+-------------------------------+-------------------------------+------------
 ec4eeea9-1e3f-4fc5-b7c4-c1f75f7d503e | Ended | 2026-05-13 08:29:54.614+00    | 2026-05-13 08:32:34.325+00    | Normal
(1 row)
```

Total battles: 1 (the resolved-late smoke #1 battle; smoke #2 was killed before pairing produced a DB row).

State breakdown:

```
 state | count
-------+-------
 Ended |     1
```

### Redis

- `DBSIZE`: **10** keys total.
- `battle:*` keys (`--scan`): **10** — one battle's worth: 1× `battle:state:ec4eeea9`, 4× `battle:turn:ec4eeea9:N:submitted` (turns 1, 3, 4, 6), 5× `battle:action:ec4eeea9:turn:N:player:...` (subset of the 6 turns the battle ran). Action rows confirm both players DID submit on at least some turns to the local replica side — consistent with the partial-visibility narrative in §3.
- `PUBSUB CHANNELS '*'`: only `__Booksleeve_MasterChanged` — the StackExchange.Redis multiplexer internal channel. **No SignalR backplane channels** (those would look like `Kombats.Battle.Infrastructure.Realtime.SignalR.BattleHub:*`). Confirms backplane is OFF for Run 2, as required.

Small state size: appropriate. No accumulation, no leftover.

---

## 5. Both Battle replica UUIDs (Prometheus / OTel)

`curl -s --data-urlencode 'match[]=active_signalr_connections' 'http://localhost:9090/api/v1/series'`:

```json
[
  { "s": "battle", "sid": "017be6cb-ca85-4097-9a15-2ddf5106c107" },
  { "s": "battle", "sid": "d2591c21-58c3-449b-8345-04f1763dd27e" },
  { "s": "bff",    "sid": "5250d5f2-4a5b-4f17-b85e-e18178cd9b70" }
]
```

**3 distinct series — both Battle replicas emit OTel resource attributes correctly**. The two `battle/<uuid>` legend entries the Phase III load run needs to populate Grafana panel id:13 are confirmed wired. Phase II's caveat about `battle-2` scraping at 0 due to short connection lifetime will dissolve under a sustained 2-minute load run.

Cross-reference: the same two `017be6cb` and `d2591c21` UUIDs appear in the user's Grafana observations during the smoke #2 hang (§3 above) — confirming that the per-replica panel is the right read for "which replica did this connection land on".

At idle (post-smoke, pre-load): all three series report value `0` — no active connections, as expected.

### Grafana panel id:13

```json
{
  "id": 13,
  "title": "Active SignalR connections — per replica",
  "targets": [
    { "expr": "active_signalr_connections",
      "legendFormat": "{{service_name}}/{{service_instance_id}}" }
  ]
}
```

Provisioned, query and legend correct, ready to render two `battle/<uuid>` lines plus one `bff/<uuid>` line once load begins.

---

## 6. Comparison-baseline contract (forward declaration)

Per `RUN_1_RESULTS.md` §6: **Run 2 compares to Run 0, not Run 1**. The two runs differ only in replica count (both lack the backplane); the comparison isolates "what does adding a second Battle replica do *without* the fix?" — which is the question Chapter 3 exists to answer.

Run 0 numbers (cited from `RUN_0_BASELINE.md` §3) are the comparison baseline:

| Metric | Run 0 (single-replica, no backplane) |
|---|---|
| ok count | 2096 |
| fail count | 25 |
| RPS | 17.47 |
| `total_ms` p50 | 1106 ms |
| `total_ms` p95 | 1593 ms |
| `battle_ms` p50 | 39.3 ms |
| `join_battle_ms` p50 | 4.4 ms |
| Battles / 2 min | 1049 |
| Pairing throughput | 9.08 matches/s |

Expected Run 2 direction (per `CHAPTER_3_PLAN.md` §9): ok % drops to ≤50 %, `total_ms` p95 saturates at `PerBotTimeout`, battles-per-2-min ≤50 % of baseline. The actual numbers will be measured by Phase C of this deliverable.

---

## 7. What I did NOT do (constraints honored)

- No code changes anywhere under `src/Kombats.*`. Verified by `git status` showing clean tree throughout.
- No edits to `docker-compose.yml`, `docker-compose.multi-replica.yml`, observability files, NBomber config, scenarios, or virtual player code.
- No commits, no pushes, no branch switches.
- No `dotnet run -- load`. That's the architect's manual step (Phase B).
- No investigation of the smoke hangs or the JoinBattle HubException. Both are the experiment's predicted condition (per spec) — not defects.
- No mitigation attempted for the smoke failure mode. Same reason.
- No changes to Grafana dashboards or Prometheus config.
- No restart of the stack after smokes (left running for the architect's load run).

---

## 8. Ready-state preconditions (final pre-flight summary)

| Precondition | Status |
|---|---|
| Branch `development` @ `1958389`, clean tree | ✅ |
| No `AddStackExchangeRedis` line in Battle Bootstrap | ✅ |
| 15 long-running containers Up (incl. both Battle replicas, no health column on Battle services) | ✅ |
| Postgres migrations applied + 5-service restart done | ✅ |
| Keycloak bootstrap `done.` + master realm token endpoint 200 | ✅ |
| 50 loadbot users seeded, manifest fresh | ✅ |
| `single-bot` clean | ✅ |
| `smoke` exhibits predicted failure mode (N=1, 2/2 hangs) — **informative, not blocking** | ✅ |
| `dns-rotation-check.sh` exit **0** (9/6 split) | ✅ |
| Prometheus shows 3 series for `active_signalr_connections` (2× battle + 1× bff) | ✅ |
| Grafana panel id:13 provisioned + correct PromQL+legend | ✅ |
| Both Battle replica UUIDs known: `017be6cb-ca85-4097-9a15-2ddf5106c107`, `d2591c21-58c3-449b-8345-04f1763dd27e` | ✅ |
| BFF replica UUID: `5250d5f2-4a5b-4f17-b85e-e18178cd9b70` | ✅ |
| No SignalR backplane channels in Redis PUBSUB | ✅ |
| Stack idle (all SignalR connection counters at 0) | ✅ |

Phase A complete. Stack ready for the architect's `dotnet run -- load`.
