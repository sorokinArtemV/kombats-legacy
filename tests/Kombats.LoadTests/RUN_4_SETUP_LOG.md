# Run 4 Setup Log — Single-replica Battle WITH backplane + skip-negotiation

## 0. Header

- **Date:** 2026-05-13 (Phase A setup ~18:23 → 18:25 +05:00 / 13:23 → 13:25 UTC).
- **Branch / HEAD:** `feat/signalr-backplane` post-D1.5 (working tree state — D1.5 + Run 3 docs uncommitted, architect commits manually after Run 4 closes the chapter).
  HEAD chain at start (unchanged from Run 3 finish):
  ```
  b2928ac docs(loadtests): Run 2 — multi-replica without backplane (failure proof)
  ad1d25b docs(loadtests): Run 1 — single-replica with backplane (baseline shift, not regression)
  5f63ec9 feat(battle): add SignalR Redis backplane                                ← D1
  1958389 docs: chapter 3 planning and investigation reports
  6971d51 chore: chapter 3 infrastructure
  ```
  Working-tree changes carried into Run 4 (all uncommitted):
  - `src/Kombats.Bff/Kombats.Bff.Application/Relay/BattleHubRelay.cs` (D1.5 — `SkipNegotiation=true` at line 77)
  - `tests/Kombats.LoadTests/RUN_3_SETUP_LOG.md`, `RUN_3_RESULTS.md` (Run 3 artefacts)
- **Stack state:** 15 long-running containers + 1 one-shot bootstrap after `down -v` (with all four overlays — `multi-replica` included to drop both Battle replicas) + clean rebuild **without** `-f docker-compose.multi-replica.yml`. **Single-replica Battle + single-replica everything else**, with backplane (D1, baked into image) and skip-negotiation (D1.5, baked into image) both active.
- **Purpose:** Run 4 (CHAPTER_3_PLAN §13, last measurement run). Two goals:
  1. **Goal 1 (primary):** matchmaking pairing throughput regression check — confirm the backplane addition (D1) didn't accidentally touch the matchmaking pairing path. Expected: pairing throughput within ±5 % of Run 0's 9.08 matches/s baseline.
  2. **Goal 2 (bonus, emerged from RUN_3_RESULTS §3b / §11.5 item 1):** single-replica latency control — does D1.5 alone restore Run 0 baseline `total_ms` on a single replica, or do we still see Run 1's +1076 ms inflation? Outcome partially isolates D1.5's contribution from multi-replica's contribution to the Run 1 → Run 3 `join_battle_ms` collapse.
- **Scope:** Phase A only. Architect runs `dotnet run -- load` in Phase B; analysis in Phase C.

---

## 1. Step-by-step timestamps and outputs

| Step | Action | Time (Asia/Yekaterinburg, +0500) | Result |
|---|---|---|---|
| A.1 | State check — branch, D1/D1.5 line refs, post-Run 3 stack still up | 18:22 | ✅ `feat/signalr-backplane`, D1 at `Program.cs:131`, D1.5 at `BattleHubRelay.cs:77`, Run 3 multi-replica stack at "Up 2 hours" |
| A.2 | `docker compose ... down -v` (4-file chain, multi-replica included) | 18:22 → 18:23 | ✅ all 15 + 1 containers removed, 5 volumes removed, network removed; `docker ps -a \| grep kombats` empty |
| A.3 | `docker compose ... up -d --build` with **3 files only** (base + observability + override; **NO** multi-replica overlay) | 18:23:09 → 18:23:30 | ✅ 14 long-running containers + 1 one-shot bootstrap; **no `kombats-battle-2`** — only `kombats-battle`. 15 total vs Run 3's 16. |
| A.4 | `./scripts/run-migrations.sh` (standard env) → `restart bff players matchmaking battle chat` (5 services, **no `battle-2` to restart**) | 18:23:40 → 18:24:30 | ✅ `=== All migrations applied successfully ===`. Battle health endpoint returned `200` on first probe via `curlimages/curl:8.10.1` helper. |
| A.5 | Keycloak bootstrap verify + master realm token endpoint + `dotnet run -- seed-users` | 18:24:40 → 18:24:50 | ✅ `[keycloak-bootstrap] done.`; token endpoint HTTP 200; `created=50, existed=0, failed=0` |
| A.6 | Backplane verification (NUMSUB=1 expected — single replica subscribing to its own channels) | 18:24:50 | ✅ — see §4 |
| A.7 | Smoke (single-bot + 3× smoke). N=3 per spec (lower than Run 3's N=5; this is regression, not structural validation) | 18:24:54 → 18:25:38 | ✅ **3/3 clean**; wall clocks 0.89 s / 0.74 s / 0.72 s — see §3 |
| A.8 | Write this setup log | 18:30 | ✅ |

---

## 2. DNS rotation check — explicitly N/A for Run 4

The `dns-rotation-check.sh` hard gate from Run 2/3 setups is **not run for Run 4 and would not be meaningful here.** The script discovers all containers attached to the `battle` DNS alias on `kombats_default`, then probes 15 times to confirm the alias rotates across more than one IP. With only `kombats-battle` running (no `kombats-battle-2`), the alias resolves to a single IP every probe — by construction, not by accident.

**The absence of DNS rotation here is the structural feature of Run 4, not a defect of setup.** This run isolates the multi-replica variable away; the question Run 4 answers is "what does the dual fix (D1 + D1.5) buy us on a single replica?" — for which DNS rotation is not a factor.

For completeness: `docker inspect kombats-battle | jq '.[].NetworkSettings.Networks.kombats_default.IPAddress'` reports `172.19.0.13`, which is the only IP the in-cluster `battle` DNS alias can resolve to.

---

## 3. Smoke results — 3/3 clean

Single-bot probe (pre-smoke) was clean: auth + onboard + BFF SignalR + queue join + leave in ~10 s, no battle (this scenario doesn't exercise Battle hub via JoinBattle).

| # | Wall clock | r1 outcome | r1 turns | r2 outcome | r2 turns | Notes |
|---|---|---|---|---|---|---|
| 1 | **0.89 s** | Lost | 6 | Won | 6 | clean |
| 2 | **0.74 s** | Won | 7 | Lost | 7 | clean |
| 3 | **0.72 s** | Lost | 6 | Won | 6 | clean |

**0 Error, 0 BattleTimeout, 0 HubException, 0 wall>30s.** All three smokes sub-second.

Run 4 smokes are faster than Run 3's post-D1.5 first smoke (0.92 s) — likely a combination of (a) no first-smoke JIT warmup on the new transport path (the image was rebuilt fresh so the JIT path may be similar, but the bot harness was just exercised by `single-bot` 30 s prior, warming the bot side), and (b) no Run 3-style first-smoke cold-path effects.

**Pre-D1.5 / pre-D1 baseline equivalent (Phase 1 from `PHASE_1_REPORT.md` / `RUN_0_BASELINE.md` setup):** smokes at sub-second baseline on single-replica → matches.

**Compared to Run 3 setup phase (post-D1.5):** 5/5 clean smokes there too, similar wall clocks. The smoke-scale fix is stable across replica count.

---

## 4. Backplane verification — NUMSUB=1 (single replica subscribed to its own channels)

D1 is baked into the Battle image regardless of replica count. With a single Battle replica:
- The backplane Redis subscription is created on startup (same as Run 1 / Run 3)
- The replica is the only subscriber to the cross-replica channels
- Group sends still go through the backplane PUBLISH → SUBSCRIBE loop, but the publisher and subscriber are the same process (Redis loopback)

This is exactly the Run 1 shape — backplane wired, single replica, the +1076 ms `total_ms` overhead Run 1 measured was on this same wiring. **The difference for Run 4 is D1.5** (skip-negotiation, BFF→Battle handshake), which Run 1 did not have.

### Verbatim post-smoke evidence

```
$ docker exec kombats-redis redis-cli PUBSUB CHANNELS '*'
Kombats.Battle.Infrastructure.Realtime.SignalR.BattleHub:internal:return:0b7659472f22_5ffd48106b6b4a90a151633ecd6132f0
__Booksleeve_MasterChanged
Kombats.Battle.Infrastructure.Realtime.SignalR.BattleHub:internal:ack:0b7659472f22_5ffd48106b6b4a90a151633ecd6132f0
Kombats.Battle.Infrastructure.Realtime.SignalR.BattleHub:internal:groups
Kombats.Battle.Infrastructure.Realtime.SignalR.BattleHub:all

$ docker exec kombats-redis redis-cli PUBSUB NUMSUB \
    Kombats.Battle.Infrastructure.Realtime.SignalR.BattleHub:all \
    Kombats.Battle.Infrastructure.Realtime.SignalR.BattleHub:internal:groups
BattleHub:all              : 1
BattleHub:internal:groups  : 1
```

Single set of per-server `ack/return` channels (one server name `0b7659472f22_5ffd...`) — confirms only one Battle process is active. NUMSUB=1 on both cross-replica channels — single replica self-subscribed.

### D1.5 verification — Battle access logs across the 3 smokes

```
Battle log, last 5 minutes:
  POST /battlehub/negotiate                : 0        ← negotiate path eliminated
  GET  /battlehub?id={token} → 404         : 0        ← no handshake split possible
  GET  /battlehub?id={token} → 101         : 6        ← 3 smokes × 2 bots = 6 successful WS upgrades
```

**0 negotiate POSTs** confirms D1.5 still applied on Run 4's BFF (the image was rebuilt from the same `feat/signalr-backplane` working tree). **0 404s** is the structural property of skip-negotiation on a single replica — DNS rotation isn't a factor here, but the elimination of the negotiate path means there's no token-issued-elsewhere-from-where-upgrade-lands gap to worry about.

---

## 5. Postgres + Redis state after setup phase

### Postgres `battle.battles`

```
 state | count
-------+-------
 Ended |     3
```

3 battles, all `Ended/Normal` — matches the 3 smokes, all clean.

### Redis

```
DBSIZE:           60 keys
battle:state:*:    3    (matches 3 ended battles)
mm:player:*:       0    (Ch2 lease fix intact)
PUBSUB CHANNELS:  see §4
```

Small inventory: ~20 keys per battle (battle:state + ~6 action keys + ~6 turn keys + ancillary), consistent with `CLEANUP_WORKER_DIAGNOSIS.md` §3 expectation. TTL unwired (Ch2.5 candidate) — unchanged from prior runs.

---

## 6. Single Battle replica UUID (Prometheus / OTel)

Probed `curl -s --data-urlencode 'match[]=active_signalr_connections' 'http://localhost:9090/api/v1/series'` post-smoke:

```json
[]
```

**0 series at probe time** (same caveat as Run 3 setup §6 — short smoke connections each lived ~0.7-0.9 s, well below the OTel default 60 s metric export interval; the gauge transitioned 0→1→0 within an export cycle and the SDK's UpDownCounter never sampled it at a non-zero value). This is **not a wiring issue** — the metric is registered correctly; the smoke load profile is too short-lived to populate it.

Under the 2-minute sustained load run the architect will trigger in Phase B, the series will populate. Expected: 2 series (`battle/<single-uuid>` + `bff/<single-uuid>`) — half of Run 3's expected 3 (because no `battle-2`).

The Battle replica's container-hostname-derived server-name on the backplane channels is `0b7659472f22_5ffd48106b6b4a90a151633ecd6132f0`. The Prometheus `service_instance_id` (which is the OTel-generated UUID, not the container name) will be visible to the architect during the load run on Grafana panel id:13.

---

## 7. Comparison-baseline contract

Per CHAPTER_3_PLAN §13 / RUN_3_RESULTS §7: **Run 4 has two comparison targets**, one for each goal.

### Goal 1 — Matchmaking pairing throughput regression check

**Primary baseline: Run 0** (single-replica, no backplane). Pairing throughput from `RUN_0_BASELINE.md` §3: **9.08 matches/s** (over 116 s active window in a 120 s test).

| Run | Configuration | Pairing throughput | Compares against | Notes |
|---|---|---|---|---|
| Run 0 | Single-replica, no backplane | 9.08 matches/s | (baseline) | Pre-backplane reference |
| **Run 4** | **Single-replica, +backplane, +D1.5** | **TBD (Phase C)** | **Run 0** | Did backplane addition affect matchmaking? |

Acceptable Run 4 range: 9.0 ± 0.5 matches/s (i.e. 8.5–9.5 matches/s, ±5 % of Run 0). Outside → investigate.

Why Run 0 and not Run 3: Run 3's 8.87 matches/s is on multi-replica Battle, which adds variability (DNS rotation, cross-replica delivery). Run 4 isolates "does adding the backplane code-path affect matchmaking?" without the multi-replica confound.

### Goal 2 — Single-replica latency control (D1.5 isolation)

**Two-column comparison: Run 0 (no D1/D1.5) AND Run 1 (with D1, no D1.5).** Run 4 is the "with D1 AND D1.5" point on the same single-replica axis.

| Run | Configuration | total_ms p50 | join_battle_ms p50 | battle_ms p50 | Compares against |
|---|---|---|---|---|---|
| Run 0 | Single, **no-D1, no-D1.5** | 1106 ms | 4.4 ms | 39.3 ms | (axis origin) |
| Run 1 | Single, **+D1, no-D1.5** | 2182.4 ms | 765.7 ms | 38.4 ms | Run 0 |
| **Run 4** | Single, **+D1, +D1.5** | **TBD** | **TBD** | **TBD** | **Both Run 0 and Run 1** |
| Run 3 | Multi, **+D1, +D1.5** | 1124.7 ms | 4.2 ms | 56.4 ms | Run 1 (per §6 contract) — secondary reference for Run 4 only |

Three possible Run 4 outcomes per the prompt:
- **(A)** `total_ms` p50 ≈ 1100 ms (Run 0 territory) ⇒ D1.5 alone restores baseline on single-replica. Multi-replica's contribution to Run 3 was small.
- **(B)** `total_ms` p50 ≈ 1500–1700 ms (between Run 0 and Run 1) ⇒ D1.5 partial; multi-replica's load-split provides the rest.
- **(C)** `total_ms` p50 ≈ 2000–2200 ms (Run 1 territory) ⇒ D1.5 doesn't meaningfully help single-replica latency; multi-replica was the dominant fix.

This refines `RUN_3_RESULTS.md` §11.5 item 1 (uncontrolled D1.5-vs-replica variable) regardless of which outcome lands. Run 4 is doing genuine double duty: regression check + control experiment.

### NOT a comparison target

**Run 3** is not the comparison target for Run 4. The relevant difference between them is `replica count`, not `with/without backplane`. Run 4 has the same backplane (D1) and same skip-negotiation (D1.5) as Run 3; the only structural delta is `2 Battle replicas → 1 Battle replica`. That means comparing Run 4 to Run 3 directly is unhelpful for both Run 4 goals.

---

## 8. What I did NOT do (constraints honored)

- **No `src/Kombats.*` changes anywhere.** Working tree carried into Run 4 is exactly what was in place at end of Run 3 Phase C — the uncommitted D1.5 in `BattleHubRelay.cs` and the uncommitted Run 3 docs.
- **No new commits.** D1.5 + Run 3 docs + this Run 4 setup log + Run 4 results (when written) all wait for the architect's single commit after Chapter 3 closes.
- **No `dotnet run -- load`.** Architect's Phase B.
- **No DNS rotation gate.** N/A for single-replica — see §2.
- **No new package additions, no compose edits, no Grafana / Prometheus / OTel config changes.** Same image build path as Run 3 — D1 + D1.5 are exactly as Run 3 left them.
- **No `OTEL_METRIC_EXPORT_INTERVAL` override.** The Prometheus 0-series at idle (§6) is the documented PHASE_2 §2 step 4 / RUN_3_SETUP_LOG §6 caveat; the same caveat applies here and the same outcome (series populate under sustained load) is expected.
- **No teardown after smokes.** Stack left running for architect's load.
- **No re-running of Run 3.** Run 4 is a separate single-replica experiment; Run 3 results stand as written.
- **No edit to `RUN_3_RESULTS.md` from this setup phase.** Run 4 results will reference and partially isolate the §11.5 item 1 gap, but that edit comes in Run 4 Phase C, not here.

---

## 9. Ready-state preconditions

| Precondition | Status |
|---|---|
| Branch `feat/signalr-backplane`, D1 + D1.5 unchanged from Run 3 | ✅ |
| 15 long-running containers Up; **only one `kombats-battle`** (no `battle-2`) | ✅ |
| Postgres migrations applied + 5-service restart | ✅ |
| Keycloak bootstrap `done.` + master realm token endpoint 200 | ✅ |
| 50 loadbot users seeded, manifest fresh | ✅ |
| Backplane wired on single Battle replica (Redis log `Connected to Redis.`, channels present, NUMSUB=1 on both cross-replica channels) | ✅ |
| Skip-negotiation active: 0 negotiate POSTs, 0 GET 404s in Battle access log across smokes | ✅ |
| `single-bot` probe clean | ✅ |
| **Smoke ×3 — all 3 clean** | ✅ (0.89 / 0.74 / 0.72 s wall clocks) |
| DNS rotation gate | N/A (single replica) — see §2 |
| Prometheus series for `active_signalr_connections` | ⚠ partial at idle (0 series; OTel export-interval caveat; will populate under sustained load) |
| No SignalR / backplane WARN/ERROR in Battle logs | ✅ |
| 0 stuck `mm:player:*` Redis keys post-smoke | ✅ |

Setup is complete. Stack is healthy, single-replica with D1 + D1.5 active, ready for the architect's manual `dotnet run -- load`.

---

## 10. Stop point — architect Phase B

Phase A complete (initial pass §1-9 — partially superseded by §11 below). **Awaiting architect manual `dotnet run -- load` for Run 4.**

What Phase C will do, when the architect signals load complete with the iteration log filename:
1. Aggregate phases (per Run 3 Phase C pattern).
2. Goal 1: compute pairing throughput from matchmaking logs, compare to Run 0's 9.08 matches/s (±5 % gate).
3. Goal 2: per-phase comparison vs Run 0 and Run 1, determine outcome (A / B / C), update RUN_3_RESULTS.md §11.5 item 1 attribution if isolation gained.
4. Write `RUN_4_RESULTS.md` with the two goals and their conclusions, plus a note on which Chapter 2.5 candidates remain deferred for the chapter closer.
5. No commit. Architect commits the full chapter set after `CHAPTER_3_REPORT.md` is written.

Run 4 is the last load run of Chapter 3.

---

## 11. D6 mid-run observability-pipeline repair (2026-05-14, pre-Phase B)

*Appended 2026-05-14 after architect attempted to verify Grafana before Phase B and discovered Prometheus showed no .NET service metrics. This section documents the root cause, the repair applied, the verification, and what changed in §6 of this log.*

### 11.1 What architect observed

Before triggering Run 4 load, architect ran:

```
curl -s http://localhost:9090/api/v1/query?query=process_cpu_count | jq '.data.result'
# → empty
```

`up`-style scrape target was healthy (`{"job":"otel-collector","value":"1"}`), so Prometheus → collector was working. But the collector's `:8889/metrics` Prometheus exporter exposed **nothing** about the .NET services — meaning no .NET service was pushing to the collector. Restarts of the collector + 5 .NET services didn't help. The pipeline had been broken silently for the entire 15-hour stack uptime.

### 11.2 Root cause (one sentence)

**The OTel endpoint env (`OpenTelemetry__OtlpEndpoint=http://otel-collector:4317`) lives only in `observability/docker-compose.observability.override.yml`, and that file was not included in Run 4 (or any prior Chapter 3 run's) compose chain.** Without that env, `Kombats.Common/Kombats.Observability/KombatsObservabilityExtensions.cs:37` reads an empty config key and skips attaching the OTLP exporter entirely (the SDK still collects metrics in-process but exports nowhere).

### 11.3 How the bug was hidden

Three things conspired to keep this latent across Chapters 1–3:

1. **Default appsettings** ships with `"OpenTelemetry": { "OtlpEndpoint": "" }` (empty string), not `null` or a sane default. The conditional exporter-attach treats `""` the same as "no endpoint configured" → no exporter, no errors.
2. **Compose-file split**: the env override file `observability/docker-compose.observability.override.yml` is structurally separate from `observability/docker-compose.observability.yml`. The latter starts Prometheus + OTel-collector + Jaeger + Grafana; the former *configures the .NET services to push to that collector*. They're philosophically two halves of the same pipeline but two files in the repo. The `multi-replica.yml` header comment (lines 11–15) explicitly documents the override file as part of the required compose chain, but the chapter-run prompts never picked that up.
3. **Run 3 partial signal**: `multi-replica.yml` line 61 inlines `OpenTelemetry__OtlpEndpoint` for `battle-2` only (correctly, per its own header comment "Env is inlined verbatim from the base battle service plus the OTel endpoint from observability.override.yml"). So during multi-replica runs, `battle-2` could export; the base `battle` could not. That meant Run 2 / Run 3 had partial Prometheus signal — which masked the gap rather than revealing it. `RUN_3_RESULTS.md` §9.3 ("per-replica OTel emission gap — Ch4 candidate") is downstream of this same bug, not a separate per-replica SDK quirk. See §11.5 below.

### 11.4 Repair applied (Level 3 — full `down -v` + rebuild with corrected chain)

Level 2 (just recreate services with corrected env via `up -d` without `-v`) would have worked technically, but the chapter's clean-state discipline (`RUN_0_BASELINE.md` §5 — every measurement run preceded by `down -v` + clean rebuild) precludes Phase B from running on top of carry-over state. Pre-repair Postgres state showed 1047 battles (3 from yesterday's Phase A smokes + 1044 from an architect-attempted Run 4 load that ran without OTel metrics flowing), so a clean reset was the right call.

**Corrected compose chain** (added `observability/docker-compose.observability.override.yml`):

```bash
docker compose -f docker-compose.yml \
               -f observability/docker-compose.observability.yml \
               -f observability/docker-compose.observability.override.yml \
               -f docker-compose.override.yml \
               down -v

docker compose -f docker-compose.yml \
               -f observability/docker-compose.observability.yml \
               -f observability/docker-compose.observability.override.yml \
               -f docker-compose.override.yml \
               up -d --build
```

Note for the architect's Phase B and any subsequent runs: **this is the compose chain that should be used going forward.** The chapter-run prompts can be updated to match.

No source code changes. No edits to `observability/otel-collector/config.yaml` (verified config inside collector via earlier exec — runtime config matches repo `observability/otel-collector/config.yaml` exactly; the pipeline routes `otlp → resource → batch → prometheus` correctly, with `resource_to_telemetry_conversion: enabled` set on the Prometheus exporter). The fix was purely compose-chain.

### 11.5 Verification — pipeline fully restored

Phase A redo with corrected chain, post-smoke (3/3 clean: 0.91 s / 0.94 s / 1.16 s; battle IDs `b929ac87`, `bd039df9`, `054075e7`):

```
# All 5 services emit process_cpu_count
$ curl -s --data-urlencode 'query=process_cpu_count' 'http://localhost:9090/api/v1/query' \
    | jq -r '.data.result | sort_by(.metric.service_name) | .[] | "  \(.metric.service_name)"' | sort -u
  battle
  bff
  chat
  matchmaking
  players

# active_signalr_connections has 2 series (single-replica: battle + bff)
$ curl -s --data-urlencode 'match[]=active_signalr_connections' 'http://localhost:9090/api/v1/series'
[
  { "s": "battle", "sid": "384228f9-e46d-4b2f-b3a6-c600319eefe7" },
  { "s": "bff",    "sid": "0e13596b-d230-44e1-897d-ad5bfdcc2f4c" }
]

# Collector :8889 Prometheus exporter populated with expected families
$ docker run --rm --network kombats_default curlimages/curl:8.10.1 -s http://otel-collector:8889/metrics \
    | grep -E "^# HELP (process_cpu_count|active_signalr_connections|http_server_request_duration|turn_resolution)"
# HELP active_signalr_connections SignalR connections currently attached to this process's hub.
# HELP http_server_request_duration_seconds Duration of HTTP server requests.
# HELP process_cpu_count The number of processors (CPU cores) available to the current process.
# HELP turn_resolution_duration_milliseconds Battle turn resolution latency (engine + Redis commit + broadcast).
```

All four metric families that any prior chapter run cited are now visible. The pipeline is end-to-end green for the first time in Chapter 3.

### 11.6 Correction to §6 of this log

§6 above (pre-repair) recorded "0 series at probe time" for `active_signalr_connections` and attributed it to "short smoke connections each lived ~0.7–0.9 s, well below the OTel default 60 s metric export interval". **That attribution was wrong** — the metric was missing because the OTel exporter wasn't attached to begin with (§11.2), not because the gauge oscillated below the scrape window. The post-repair re-run shows 2 series after the same sub-second smokes (`active_signalr_connections` shows up promptly once the exporter is wired); the OTel-export-interval-vs-smoke-lifetime explanation that PHASE_2_REPORT §2 step 4 and RUN_3_SETUP_LOG.md §6 also cited is **partially superseded** — that mechanism exists in principle (UpDownCounter samples can miss short transitions), but the dominant cause of 0 series in those earlier observations was the same missing-endpoint bug, not the export-interval race. Addendum to RUN_3_RESULTS.md §9.3 covers the historical reattribution.

### 11.7 Implications for Run 4 Phase B

- **Stack is in clean state**: 0 stuck battles, 0 `mm:player:*` keys, 3 Postgres battles (the post-repair smoke set), 57 Redis keys, 50 loadbot users seeded fresh. Backplane NUMSUB=1 on cross-replica channels (single-replica self-subscribed). DNS rotation N/A (single-replica). D1.5 verified at smoke level: 0 negotiate POSTs, 0 GET 404s, 6 GET 101s (3 smokes × 2 bots).
- **Architect's earlier Run 4 attempt**: the iteration log `tests/Kombats.LoadTests/iteration-logs/iterations-2026-05-14--09-42-56.jsonl` (643 KB, 1044 battles) survives on the host filesystem and contains valid bot-side phase metrics for that run (auth_ms, queue_wait_ms, join_battle_ms, battle_ms, total_ms, outcomes). Goal 1 (pairing throughput from matchmaking logs) and Goal 2 (per-phase latency from iteration log) **could be analysed from that file alone, since neither goal requires Prometheus**. However, the architect's policy of clean state per measurement run + the need for Grafana visual evidence for the chapter STORY makes a fresh Phase B run the right call. Decision is the architect's.
- **Run 4 Phase A redo was performed as part of this repair** (Level 3) — migrations applied, 5-service restart, Keycloak bootstrap clean, 50 users seeded, 3 smokes clean. Ready-state preconditions in §9 above re-hold post-repair.

### 11.8 Files touched

| Path | Change | Reason |
|---|---|---|
| `tests/Kombats.LoadTests/RUN_4_SETUP_LOG.md` | this §11 appended | document the issue + fix |

**No source code changes. No compose file edits.** The repair was purely a compose-invocation correction — including a file that already existed in the repo (`observability/docker-compose.observability.override.yml`) in the chain. Working tree at end of D6 has the same uncommitted set as end of Run 4 Phase A: D1.5 in `BattleHubRelay.cs` + Run 3 docs + Run 4 setup log.

### 11.9 Cross-document addendum needed

**`RUN_3_RESULTS.md` §9.3** ("Grafana per-replica panel showed only one replica — Chapter 4 candidate") needs an addendum: the "per-replica OTel emission gap" framed there is downstream of this same compose-chain bug, not a separate per-replica SDK behavior. Specifically, during Run 3:
- `kombats-battle` (base service): no OTel endpoint env → no exporter attached → no metrics in Prometheus.
- `kombats-battle-2`: had OTel endpoint env from `multi-replica.yml:61` → exporter attached → metrics in Prometheus.
- `kombats-bff` and other base-service .NET hosts: no OTel endpoint env → no exporter attached.

So Run 3 §9.3's recorded "one series for `service_instance_id=54346fc4-...`" was almost certainly **kombats-battle-2's UUID, not kombats-battle's** — the opposite of what §9.3 inferred. With the corrected chain in place going forward, all 5 services + battle-2 (when multi-replica is on) emit correctly, and the Ch4 candidate "per-replica OTel emission discipline" reduces to "audit the chapter-run compose chains" — already done by this repair. The Run 3 §9.3 attribution should be amended in the chapter closer (`CHAPTER_3_REPORT.md`) or in a small addendum patch to `RUN_3_RESULTS.md` § 9.3 itself; that's the architect's call.
