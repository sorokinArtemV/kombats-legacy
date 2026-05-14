# Run 1 Setup Log — single-replica Battle WITH SignalR Redis backplane

## 0. Header

- **Date:** 2026-05-13
- **Branch / HEAD:** `feat/signalr-backplane` @ `5f63ec9` (`feat(battle): add SignalR Redis backplane`)
- **Working tree at session start:** clean
- **Stack composition:** single-replica Battle + backplane + BFF/Matchmaking/Players/Chat all single-replica. Multi-replica overlay deliberately NOT included.
- **Spec:** Phase III Deliverable 2 Run 1 (this run) — sanity check that the backplane does not regress Run 0 baseline.

---

## 1. Phase A timeline (verbatim wall clocks, local TZ +05:00)

| Step | Started | Finished | Notes |
|---|---|---|---|
| A.1 git checks | 12:21:00 | 12:21:01 | clean tree, branch `feat/signalr-backplane`, HEAD `5f63ec9d464c75914533c1f91d1723d55f75ce93` |
| A.2 `down -v` | 12:21:02 | 12:21:04 | one anomaly — see §2 |
| A.2b stale container removed | 12:21:25 | 12:21:25 | `docker rm -f kombats-battle-2` + `docker network rm kombats_default` — see §2 |
| A.3 `up -d --build` | 12:22:31 | 12:24:35 | 14 long-running containers come up clean |
| A.3b wait + ps | 12:24:35 | 12:24:50 | all 14 Up; postgres/redis/rabbitmq/keycloak-db healthy |
| A.4 migrations | 12:25:13 | 12:25:30 | `=== All migrations applied successfully ===` |
| A.4b service bounce | 12:25:39 | 12:25:40 | restart bff/players/matchmaking/battle/chat |
| A.5 Keycloak bootstrap | 12:26:15 | 12:26:16 | `[keycloak-bootstrap] done.`; master realm returns access_token |
| A.6 seed-users | 12:26:25 | 12:26:30 | created=50 existed=0 failed=0; manifest regenerated 12:26:29 |
| A.7 single-bot | 12:26:48 | 12:27:00 | clean auth + onboard + BFF SignalR + queue path |
| A.7 smoke | 12:27:06 | 12:27:08 | 0.92 s, 5-turn battle, Won/Lost; clean |
| A.8 dns-rotation-check | 12:27:23 | 12:27:28 | **EXIT 1** (single-replica, expected for Run 1) |
| A.9 PUBSUB CHANNELS | 12:27:35 | 12:27:36 | see §4 — backplane confirmed live |
| A.10 this log | 12:27:50 | (now) | |

---

## 2. Teardown anomaly (resolved)

`docker compose -f docker-compose.yml -f observability/docker-compose.observability.yml -f observability/docker-compose.observability.override.yml down -v` left one container alive:

```
ac8ba4087ffd   kombats-battle-2   "dotnet Kombats.Batt…"   2 hours ago   Up 2 hours   kombats-battle-2
```

This is the `battle-2` container created by Phase II's multi-replica overlay (`docker-compose.multi-replica.yml`, see `PHASE_2_REPORT.md` §1). Since the Run 1 spec explicitly forbids including that overlay file in the compose command (single-replica is the point of Run 1), `down -v` didn't know about `battle-2` and skipped it. The orphan container kept `kombats_default` alive — `down -v` logged `Network kombats_default Resource is still in use` and refused to remove it.

**Fix applied:** `docker rm -f kombats-battle-2 && docker network rm kombats_default`. Both succeeded. State afterwards confirmed:

- `docker ps -a | grep -i kombats` → empty
- `docker volume ls | grep -i kombats` → empty
- `docker network ls | grep -i kombats` → empty

This was the right call: Phase II's `battle-2` is a stale artifact from a prior session, not in-progress work, and Run 1 must be single-replica by spec. Spec teardown intent (clean state) achieved.

**Operational note for future readers:** if a future run brings up multi-replica and a subsequent single-replica run follows without an explicit `-f docker-compose.multi-replica.yml down -v`, the same orphan will recur. Cleanest fix would be to either always include the overlay in `down -v` regardless of run config, or `docker compose --project-name kombats down -v --remove-orphans`. Not changing the spec — flagging for the architect.

---

## 3. `docker compose ps` after bring-up (14 containers, single-replica)

```
NAME                     STATUS
kombats-battle           Up (about 1 min)
kombats-bff              Up (about 1 min)
kombats-chat             Up (about 1 min)
kombats-grafana          Up (about 2 min)
kombats-jaeger           Up (about 2 min)
kombats-keycloak         Up (about 1 min)
kombats-keycloak-db      Up (about 2 min, healthy)
kombats-matchmaking      Up (about 1 min)
kombats-otel-collector   Up (about 2 min)
kombats-players          Up (about 1 min)
kombats-postgres         Up (about 2 min, healthy)
kombats-prometheus       Up (about 2 min)
kombats-rabbitmq         Up (about 2 min, healthy)
kombats-redis            Up (about 2 min, healthy)
```

14 long-running containers. `kombats-keycloak-bootstrap` is one-shot — exits 0 within ~30 s of start and is no longer in `compose ps`; full lifecycle confirmed in §5. Count matches Run 0 baseline (`RUN_0_BASELINE.md` §1).

---

## 4. Backplane sanity — Redis PUBSUB CHANNELS (verbatim)

### 4.1 Spec command literal output

```sh
$ docker exec kombats-redis redis-cli PUBSUB CHANNELS 'SignalR*'
(empty)
```

### 4.2 Important interpretation note

The spec asked for channels matching `SignalR*`, derived from §12 Q7 ("default channel prefix"). The literal glob returns empty BUT this is not a backplane failure — it is a defaults-change in newer versions of `Microsoft.AspNetCore.SignalR.StackExchangeRedis`. The default channel prefix is no longer the literal string `SignalR.*`; it is now the **hub type's full name** (`<Namespace>.<HubClass>:*`). Broad-glob probe confirms the backplane is live:

```sh
$ docker exec kombats-redis redis-cli PUBSUB CHANNELS '*'
__Booksleeve_MasterChanged
Kombats.Battle.Infrastructure.Realtime.SignalR.BattleHub:internal:ack:6922f4b997a7_262210d5cc5d4aadb3b4f1f1749c3394
Kombats.Battle.Infrastructure.Realtime.SignalR.BattleHub:internal:groups
Kombats.Battle.Infrastructure.Realtime.SignalR.BattleHub:all
Kombats.Battle.Infrastructure.Realtime.SignalR.BattleHub:internal:return:6922f4b997a7_262210d5cc5d4aadb3b4f1f1749c3394
```

Four SignalR backplane channels active for `Kombats.Battle.Infrastructure.Realtime.SignalR.BattleHub`:
- `:all` — broadcast channel
- `:internal:groups` — group-membership replication (the channel that makes `Clients.Group(...)` cross-replica work — the entire point of the backplane)
- `:internal:ack:<server-id>` — ack channel per server instance
- `:internal:return:<server-id>` — return-value channel per server instance

The fifth `__Booksleeve_MasterChanged` is StackExchange.Redis's own admin pub/sub channel, unrelated to SignalR. Pre-existing.

### 4.3 Subscriber count on backplane channels

```sh
$ docker exec kombats-redis redis-cli PUBSUB NUMSUB \
    "Kombats.Battle.Infrastructure.Realtime.SignalR.BattleHub:all" \
    "Kombats.Battle.Infrastructure.Realtime.SignalR.BattleHub:internal:groups"
Kombats.Battle.Infrastructure.Realtime.SignalR.BattleHub:all          1
Kombats.Battle.Infrastructure.Realtime.SignalR.BattleHub:internal:groups  1

$ docker exec kombats-redis redis-cli PUBSUB NUMPAT
0
```

NUMSUB=1 on both channels = the single Battle replica is actively subscribed. With 2 replicas (Runs 2/3 territory) we'd expect NUMSUB=2.

### 4.4 Battle log — SignalR/Redis lines (verbatim, first ~6 lines)

```
[07:24:35 INF] Connecting to Redis endpoints: redis:6379. Using Server Name: 6922f4b997a7_ec5a04d9dbb64de592486a0a0bd89562 {"SourceContext": "Microsoft.AspNetCore.SignalR.StackExchangeRedis.RedisHubLifetimeManager"}
[07:24:35 INF] Connected to Redis. {"SourceContext": "Microsoft.AspNetCore.SignalR.StackExchangeRedis.RedisHubLifetimeManager"}
[07:25:40 INF] Connecting to Redis endpoints: redis:6379. Using Server Name: 6922f4b997a7_262210d5cc5d4aadb3b4f1f1749c3394 {"SourceContext": "Microsoft.AspNetCore.SignalR.StackExchangeRedis.RedisHubLifetimeManager"}
[07:25:40 INF] Connected to Redis. {"SourceContext": "Microsoft.AspNetCore.SignalR.StackExchangeRedis.RedisHubLifetimeManager"}
[07:27:08 INF] Initialized battle state for BattleId: 5118b778-f905-4818-b3d0-be7a7ed44ee6, ...
```

`Microsoft.AspNetCore.SignalR.StackExchangeRedis.RedisHubLifetimeManager` connecting/connected fires twice — once at first boot, once after the post-migration service bounce. Server names rotate (`ec5a04…` → `262210…`) because each container start picks a new UUID — that's why the `:ack:` / `:return:` channels currently visible are keyed on `262210…` (the live server). No WARN/ERROR lines emitted by the backplane manager. Connection-pool / cluster-config noise is absent because we're on a standalone Redis.

**Verdict: backplane is provably live, subscribed, and integrated with the running Battle instance.** Proceeding.

---

## 5. Keycloak bootstrap (verbatim)

```
$ docker logs kombats-keycloak-bootstrap | tail -20
[keycloak-bootstrap] waiting for keycloak admin endpoint...
[keycloak-bootstrap] disabling sslRequired on master realm...
[keycloak-bootstrap] done.

$ docker ps -a --filter name=kombats-keycloak-bootstrap --format '{{.Names}}\t{{.Status}}'
kombats-keycloak-bootstrap	Exited (0) About a minute ago

$ curl -sS -X POST http://localhost:8080/realms/master/protocol/openid-connect/token \
    -H "Content-Type: application/x-www-form-urlencoded" \
    -d "grant_type=password&client_id=admin-cli&username=admin&password=admin" \
    | head -c 80
{"access_token":"eyJhbGciOiJSUzI1NiIsInR5cCIgOiAiSldUIiwia2lkIiA6ICJQOXVjT
```

`done.` line present; container exit code 0; master realm token endpoint returns JSON with `access_token`. All green.

---

## 6. Smoke results (verbatim, both runs)

### 6.1 `dotnet run -- single-bot`

```
12:26:49.438 info: probe[0] [probe] target: http://localhost:5000, user: loadbot-0001
12:26:49.545 info: probe[0] [probe] token acquired in 64 ms
12:26:49.930 info: probe[0] [probe] onboard in 384 ms — state=Draft revision=1
12:26:50.003 info: probe[0] [probe] set-name done
12:26:50.147 info: probe[0] [probe] allocate-stats done
12:26:50.201 info: probe[0] [probe] SignalR connected in 53 ms
12:26:50.229 info: probe[0] [probe] join-queue result: status=Searching matchId=(null) battleId=(null)
12:26:50.229 info: probe[0] [probe] waiting 10s before leaving...
12:27:00.257 info: probe[0] [probe] leave-queue done
```

Clean. (Same caveat noted in `PHASE_2_REPORT.md` §3: `single-bot` does not exercise the Battle SignalR path — it only confirms auth + onboard + BFF SignalR + queue. The smoke battle below covers Battle SignalR.)

### 6.2 `dotnet run -- smoke`

```
12:27:08.749 info: smoke[0] [smoke] wall clock: 0.92s | r1=loadbot-0001/Lost turns=5 battle=5118b778… | r2=loadbot-0002/Won turns=5 battle=5118b778…

---- loadbot-0001 ----
  outcome     : Lost
  turnsPlayed : 5
  auth        : 51 ms
  onboard     : 12 ms
  connect     : 40 ms
  queueWait   : 522 ms
  joinBattle  : 191 ms
  battle      : 96 ms
  total       : 912 ms
  events      : turnOpened=4 resolved=5 damaged=9 stateUpd=4 feed=7

---- loadbot-0002 ----
  outcome     : Won
  turnsPlayed : 5
  auth        : 48 ms
  onboard     : 50 ms
  connect     : 9 ms
  queueWait   : 514 ms
  joinBattle  : 191 ms
  battle      : 96 ms
  total       : 904 ms
  events      : turnOpened=4 resolved=5 damaged=9 stateUpd=4 feed=7
```

Clean Won/Lost pair, 5 turns, 4 turnOpened + 5 resolved + 9 damaged + 4 stateUpd + 7 feed events per bot — full event stream delivered. With single-replica + backplane the cross-replica failure mode is impossible by definition, so this only confirms the backplane addition doesn't break the happy path (it doesn't).

---

## 7. DNS rotation pre-flight (verbatim, expected exit 1)

```
$ ./tests/Kombats.LoadTests/scripts/dns-rotation-check.sh
Discovering containers with alias 'battle' on network 'kombats_default'...
Discovered 1 container(s) with alias 'battle':
  kombats-battle  ->  172.19.0.15

Probing 'dig +short battle | head -n1' 15 times from a helper container on 'kombats_default'...

Probe results (15 probes, 0 failed):
  kombats-battle (172.19.0.15): 15 hits

STOP: only 1 instance detected (single-replica or non-rotating DNS)
EXIT=1
```

Exit code **1** = single-replica detected. This is the **correct expected state for Run 1** per Phase III §A.8: "For Run 1 the expected exit code is `1`. Exit `0` would be a bug (accidentally brought up multi-replica overlay)."

---

## 8. Post-smoke state (data points for Run 1 vs baseline comparison)

### 8.1 Postgres `battle.battles`

```
$ docker exec kombats-postgres psql -U postgres -d kombats -tAc \
    "SELECT count(*) FROM battle.battles;"
1

$ docker exec kombats-postgres psql -U postgres -d kombats -tAc \
    "SELECT state, count(*) FROM battle.battles GROUP BY state;"
Ended|1
```

1 row, state = `Ended`. Matches the 1 smoke battle (`5118b778-f905-4818-b3d0-be7a7ed44ee6`), end-of-battle path executed correctly.

### 8.2 Redis `battle:*` keys

```
$ docker exec kombats-redis redis-cli --scan --pattern 'battle:*' | wc -l
16
```

16 keys total: 10 `action`, 5 `turn:N:submitted`, 1 `state` — consistent with `CLEANUP_WORKER_DIAGNOSIS.md` §3 ("battle:state:*" has no TTL; action/turn TTL ≈ 12 h). Per `RUN_0_BASELINE.md` §5/§6 this is expected and the reason clean teardown is mandatory before every measurement run.

Sample (first 10):
```
battle:action:5118b778-f905-4818-b3d0-be7a7ed44ee6:turn:3:player:eaa7f9e3-…
battle:action:5118b778-f905-4818-b3d0-be7a7ed44ee6:turn:1:player:eaa7f9e3-…
battle:action:5118b778-f905-4818-b3d0-be7a7ed44ee6:turn:3:player:fa482e37-…
battle:action:5118b778-f905-4818-b3d0-be7a7ed44ee6:turn:4:player:fa482e37-…
battle:action:5118b778-f905-4818-b3d0-be7a7ed44ee6:turn:5:player:fa482e37-…
battle:action:5118b778-f905-4818-b3d0-be7a7ed44ee6:turn:2:player:eaa7f9e3-…
battle:turn:5118b778-f905-4818-b3d0-be7a7ed44ee6:5:submitted
battle:action:5118b778-f905-4818-b3d0-be7a7ed44ee6:turn:1:player:fa482e37-…
battle:turn:5118b778-f905-4818-b3d0-be7a7ed44ee6:2:submitted
battle:state:5118b778-f905-4818-b3d0-be7a7ed44ee6
```

### 8.3 Prometheus `service_instance_id` label confirmation (re-running Phase I §3 probe)

```
$ curl -s --data-urlencode 'match[]=active_signalr_connections' \
    'http://localhost:9090/api/v1/series' \
  | jq '.data | sort_by(.service_name) | map({s: .service_name, sid: .service_instance_id})'
[
  { "s": "battle", "sid": "14074c80-4083-4971-8ca8-7a654bcbcab0" },
  { "s": "bff",    "sid": "3e8788c5-5d9f-4168-8fe7-3d6ef5e1455f" }
]
```

`service_instance_id` is present as expected (matches Phase I §3 + Phase II §2 step 4). 2 distinct UUIDs — 1× `battle` + 1× `bff` — exactly right for single-replica. No fallback `OTEL_RESOURCE_ATTRIBUTES` needed. Grafana per-replica panel (id 13) will render 2 series during the load run, not 3.

---

## 9. Sanity-check summary (pass/fail)

| Check | Expected | Observed | Status |
|---|---|---|---|
| Git clean, branch correct, HEAD correct | clean / `feat/signalr-backplane` / `5f63ec9…` | matches | ✅ |
| Teardown leaves no kombats containers/volumes/network | 0 of each | 0 / 0 / 0 (after stale `battle-2` removal — §2) | ✅ |
| Stack brings up 14 containers | 14 | 14 | ✅ |
| Migrations succeed | `=== All migrations applied successfully ===` | observed | ✅ |
| Keycloak bootstrap `done.` | `[keycloak-bootstrap] done.` | observed | ✅ |
| Master realm token endpoint returns JSON | `{"access_token":…}` | observed | ✅ |
| Seed users 50 / no failures | created=50 existed=0 failed=0 | matches | ✅ |
| `single-bot` clean | clean exit | clean | ✅ |
| `smoke` clean (one full battle) | both bots succeed, events delivered | both succeed, events delivered | ✅ |
| `dns-rotation-check.sh` exit 1 | exit 1 (single-replica) | exit 1 | ✅ |
| Backplane channels exist in Redis pub/sub | non-empty | 4 channels, NUMSUB=1 on `:all` and `:internal:groups` | ✅ (see §4.2 for interpretation note) |
| Battle log shows SignalR.StackExchangeRedis connecting | `Connecting to Redis endpoints: redis:6379` | observed | ✅ |
| Postgres `battle.battles` row count after smoke | 1, state=Ended | 1, Ended | ✅ |
| Redis `battle:*` after smoke | small (this run only) | 16 keys, 1 battle | ✅ |
| `service_instance_id` label in Prometheus | present | present | ✅ |

**No anomalies blocking the load run.** Setup complete.

---

## 10. Side observations (informational, no action)

1. **`PUBSUB CHANNELS 'SignalR*'` glob is a stale spec assumption.** Phase III §A.9 of the spec ("Should list at least one channel matching `SignalR.*` pattern") is calibrated to an older default channel prefix. The current `Microsoft.AspNetCore.SignalR.StackExchangeRedis 10.0.3` default is the hub's full type name (`Kombats.Battle.Infrastructure.Realtime.SignalR.BattleHub:*`). This is the package's intended behavior — not a misconfiguration. **Suggested spec edit (for future runs):** broaden the glob to either `*Hub:*` or `*BattleHub:*`, or just `'*'`. No code change.

2. **Two `Connecting to Redis…` log lines in Battle.** Reflects (a) initial container boot at 07:24:35, (b) post-migration service bounce at 07:25:40 — exactly the disciplined-teardown sequence from `CHAPTER_3_PLAN.md` §13. Not a re-connect during operation. Server name UUIDs rotated as expected; the live channels are now keyed on the second UUID. Informational.

3. **Single-replica + backplane = backplane goes through Redis to nobody else.** With NUMSUB=1, every group send by this Battle replica publishes to Redis and is delivered back to the publishing replica. The Redis hop is "wasted" on single-replica — by design, since the whole point of the chapter is to measure that overhead. Run 1's verdict will quantify it.

4. **Stale `battle-2` from Phase II.** See §2. Removed manually. Not a Run 1 issue; flagging in case other operators following this spec hit the same.

---

## 11. Things I did not do

- No `git commit`, no `git push`, no `git add`, no working-tree changes.
- No `src/Kombats.*` modifications beyond what is already on `feat/signalr-backplane` HEAD (`5f63ec9`).
- No NBomber / scenario / virtual player edits.
- No `docker-compose.multi-replica.yml` in compose commands — single-replica enforced.
- No load test (`dotnet run -- load`) — that is Phase B, manual by architect.
- No edits to `RUN_0_BASELINE.md`, `PHASE_3_D1_REPORT.md`, `PHASE_1_REPORT.md`, `PHASE_2_REPORT.md`.
- No "while I'm here" cleanups, no spec edits, no comment edits in `src/Kombats.*`.

---

## 12. Files added / modified

| Path | Status | Purpose |
|---|---|---|
| `tests/Kombats.LoadTests/RUN_1_SETUP_LOG.md` | added (new, this file) | A.10 |

No other tracked file modified.

---

## 13. Ready-for-load-run summary

- **Stack:** single-replica Battle + backplane on `feat/signalr-backplane` @ `5f63ec9`, 14 containers Up.
- **Backplane:** live, 4 channels, NUMSUB=1 on `:all` and `:internal:groups`.
- **DNS check:** exit 1 (single-replica, expected).
- **Smoke:** clean (5-turn battle, all events delivered).
- **Post-smoke state:** 1 Ended battle in Postgres, 16 `battle:*` keys in Redis, `service_instance_id` label present.

Architect: please run `cd tests/Kombats.LoadTests && dotnet run -- load` and report the iteration-log jsonl filename.
