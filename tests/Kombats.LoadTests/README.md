# Kombats.LoadTests

Load-test harness for the Kombats backend. Drives the BFF + Battle hot path
with virtual players (bots) connecting via SignalR, queuing, fighting, and
disconnecting. Reports come out as NBomber HTML.

This project is **local-only**. It is not built or run by any CI pipeline
and is not part of the production deploy.

## Quick start

```bash
# 1. Bring up the full local stack (root infra + observability)
docker compose \
  -f docker-compose.yml \
  -f observability/docker-compose.observability.yml \
  -f observability/docker-compose.observability.override.yml \
  up -d

# 2. Seed Keycloak with N test users
dotnet run --project tests/Kombats.LoadTests/SeedUsers -- --count 50

# 3. Smoke (one battle, no NBomber)
dotnet run --project tests/Kombats.LoadTests -- smoke

# 4. Single-bot probe (verifies auth without running a battle)
dotnet run --project tests/Kombats.LoadTests -- single-bot

# 5. Baseline load (25 pairs / 50 bots — NBomber Community license cap)
dotnet run --project tests/Kombats.LoadTests -- load
```

Reports land in `tests/Kombats.LoadTests/reports/<timestamp>/`. The folder
is gitignored except for `reports/baseline-sample/` (a curated portfolio
artefact).

## CLI

| Command | Purpose |
|---|---|
| `smoke` | One pair of bots, plain `Task.WhenAll`. Verifies the full lifecycle outside NBomber. Exit 0 if both bots reach `BattleEnded`. |
| `single-bot` | One bot acquires a token, opens SignalR, joins queue, waits ~10s, leaves cleanly. Verifies auth. No battle. |
| `load` | NBomber scenario. Default `--count 25 --duration 120`. Hard-rejects `--count > 25` (Community license). |
| `seed-users` | Convenience pass-through that invokes `SeedUsers/SeedUsers.csproj`. Forwards `--count`, `--password`. |

Every scalar config (URLs, durations, timeouts, ramp, seed) is overridable
via CLI flag and via `appsettings.json` / `appsettings.Local.json`.

## Project layout

```
tests/Kombats.LoadTests/
├── Kombats.LoadTests.csproj        Main load-test executable
├── Program.cs                       CLI verbs: smoke | single-bot | load | seed-users
├── appsettings.json                 Defaults — URLs, pair counts, timeouts
├── appsettings.Local.json           Local overrides (gitignored)
│
├── Authentication/
│   └── KeycloakTokenClient.cs       Real ROPC against the kombats-loadtest client
│
├── VirtualPlayer/
│   ├── VirtualPlayer.cs             One-battle lifecycle wrapper
│   ├── VirtualPlayerOptions.cs
│   ├── PlayerBehavior.cs            Random uniform, seeded per bot
│   └── PlayerState.cs               Per-bot battle state (HP, my-id, current turn)
│
├── SignalR/
│   ├── BattleHubClient.cs           One HubConnection to BFF /battlehub; 8-retry JoinBattle
│   └── HubEventTracker.cs           Records TurnOpened / BattleEnded / BattleFeedUpdated timings
│
├── Scenarios/
│   ├── SingleBattleScenario.cs      smoke verb implementation
│   └── ConcurrentBattlesScenario.cs NBomber load scenario
│
├── SeedUsers/
│   ├── SeedUsers.csproj             Sub-project: Keycloak Admin REST API user creator
│   └── Program.cs
│
├── scripts/
│   └── seed-users.sh                Convenience wrapper over `dotnet run --project SeedUsers`
│
└── reports/                          NBomber output (gitignored except baseline-sample/)
    └── baseline-sample/              Curated portfolio artefact
```

## Load model

The NBomber scenario uses the **Close Model** — `RampingConstant` to ramp
from 0 to `Load:PairCount` concurrent iterations over `Load:RampUpSeconds`,
followed by `KeepConstant` holding that copy count for the remaining
`TestDurationSeconds - RampUpSeconds`. As soon as one iteration finishes,
NBomber starts the next so the concurrency stays at the configured copy
count for the duration of the hold window.

This is deliberate. NBomber's Open Model (`Inject(rate, interval, during)`)
spreads a fixed number of iterations across the duration at a constant
rate — appropriate for **stateless** scenarios like hitting an HTTP
endpoint, where each iteration is short and independent. Our scenario is
**stateful**: one iteration is a full bot session (token → onboarding →
queue → JoinBattle → turn loop → BattleEnded → cleanup) that takes tens of
seconds. With Inject, the rate is set independently of how long iterations
last, so most bots end up alone in time and never find a queue partner —
the symptom is a large fraction of QueueTimeout outcomes. Close Model
fixes this by guaranteeing N bots are always alive simultaneously,
matching the "25 concurrent active battles" load shape we actually want
to measure.

The per-iteration cursor in `ConcurrentBattlesScenario` walks `users` with
modulo wrap-around, so the seeded user pool is reused across iterations as
battles complete. Seed `Math.Max(pairs * 2, 50)` users to keep enough
headroom for the matchmaking-side projection cleanup between reuses.

## Authentication

### Why we don't reuse `kombats-web`

The SPA client has `directAccessGrantsEnabled: false`
(`infra/keycloak/realm.json:90`). The ROPC token endpoint rejects requests
against it. Enabling ROPC on the SPA client just for tests would weaken the
production auth surface, so we don't.

### Why we don't use a separate realm

A `realms/kombats-loadtest`-issued token will not validate against services
that have `Keycloak:Authority = http://localhost:8080/realms/kombats`. Two
defaults fire:

- `TokenValidationParameters.ValidateIssuer` — `iss` is checked against the
  OIDC discovery doc's `issuer` field, which is the configured authority.
  A token from a different realm has a different `iss`.
- `TokenValidationParameters.ValidateIssuerSigningKey` — the signing key
  must be in the JWKS fetched from the configured authority. Keys from a
  different realm are not in that set.

Multi-realm support would mean patching
`src/Kombats.Common/Kombats.Abstractions/Auth/KombatsAuthExtensions.cs:13-37`
and the BFF inline copy
`src/Kombats.Bff/Kombats.Bff.Bootstrap/Program.cs:32-57` to extend
`ValidIssuers` and add a second JWKS source. We don't want to touch
production auth code for a load-test convenience.

So the load-test client lives in the existing `kombats` realm. See
[`infra/keycloak/README.md`](../../infra/keycloak/README.md) for the client
config.

### What `kombats-loadtest` does

- Confidential client, fixed secret `loadtest-secret-do-not-use-in-prod`
  checked into `realm.json`.
- Direct access grants only (ROPC) — every other flow is disabled.
- Audience mapper adds `kombats-api` to the token's `aud` claim so services
  accept it.
- 2-hour token lifetime — long enough for any single load-test run; no
  refresh dance.

`KeycloakTokenClient.cs` posts to
`{KeycloakBaseUrl}/realms/kombats/protocol/openid-connect/token` with
`grant_type=password`, caches the token per-username, and refreshes only
if the cached token is within 5 min of expiry.

### Disabling for a production deploy

The Azure pipeline uploads its own realm.json (`pipelines/deploy-stack.yml`).
Strip the `kombats-loadtest` client block from that uploaded copy if the
deploy must not ship a ROPC-enabled client. The block is self-contained — no
mappers or scopes reference it.

## Known issues found during load testing

### F1 — `POST /api/v1/queue/join` returns transient HTTP 400 right after onboarding

**Surfaced at**: 15 pairs (30 bots), first appearance. Reproducible.
**Symptom**: 6 of 30 bots failed with `HTTP 400 invalid_request — "Invalid
request to Matchmaking"` when posting to `/api/v1/queue/join` within ~1s of
finishing onboarding.
**Root cause**: `JoinQueueHandler` rejects identities whose combat profile
hasn't yet propagated to the Matchmaking-local `player_combat_profiles`
projection
(`src/Kombats.Matchmaking/Kombats.Matchmaking.Application/UseCases/JoinQueue/JoinQueueHandler.cs:50-61`).
The projection is fed asynchronously from the Players service's
`PlayerCombatProfileChanged` event via RabbitMQ. Under load, the
onboard → join-queue round trip from the SPA's perspective outruns the
event-bus propagation.
**Production impact**: Real users would see the same 400 if they queue
immediately after the first stat allocation. Frontend doesn't currently
retry this; instead it shows an error and forces a manual re-click. Not a
load-test artefact.
**Mitigation in harness**: `BffHttpClient.JoinQueueAsync` retries on a
detected transient 400 with the same exponential schedule the frontend
uses for `JoinBattle` (`battle-hub.ts:27`). Total budget ~8s — covers any
realistic projection-propagation delay on this stack.
**Suggested downstream fix (not made)**: Either (a) the frontend retries
the call with the same policy this harness uses, or (b) `JoinQueueHandler`
waits briefly for the projection to arrive before returning NotReady, or
(c) `EnsureCharacterExists` blocks until the projection is observed.

### F2 — `active_battles` metric hard to observe at low / short load

**Surfaced at**: 5-pair and 10-pair runs of 30-90s duration.
**Symptom**: `active_battles{service="battle"}` in Prometheus stays at 0
across the whole run window even though battles definitely happened
(NBomber reports 10/10 ok, smoke shows winner/loser).
**Root cause**: OpenTelemetry .NET's `PeriodicExportingMetricReader`
default export interval is 60 s (`KombatsObservabilityExtensions.cs`
doesn't override it). The `active_battles` UpDownCounter is incremented
by `BattleLifecycleAppService.HandleBattleCreatedAsync` when Turn 1 opens
and decremented by `BattleTurnAppService.CommitAndNotifyBattleEnded` on
the EndedNow branch. With battle wall-clock ~50–200 ms on a fresh stack,
the value rises and falls between exports, so the SDK pushes 0 at each
60s tick.
**Production impact**: None — the metric is correct, just sampled too
coarsely for short tests.
**Verification path**: The full 25-pair × 120s baseline will sustain
concurrent battles past the 60s export window and the metric will read
non-zero. For verification on shorter runs, use
`turn_resolution_duration_milliseconds_count` (a monotonic counter,
captures every resolution regardless of window timing).
**Suggested downstream fix (not made)**: Reduce the OTel export interval
to 10 s in the local stack (`OpenTelemetry__OtlpExportIntervalMs=10000`
env var, or add a `MetricReader` configuration override) so short bursts
become observable.

## Constraints (what this project may NOT do)

- Must not modify any `src/Kombats.*` code.
- Must not modify `infra/workload.bicep` or any Azure deploy file.
- Must not modify the observability stack (`observability/`).
- Must not run any NBomber scenario with concurrency > 25 (Community license).
  The `load` CLI rejects `--count > 25` with a clear error.

## See also

- [`../../LOAD_TEST_PLAN.md`](../../LOAD_TEST_PLAN.md) — the Phase 1 design
  doc that produced this project.
- [`../../infra/keycloak/README.md`](../../infra/keycloak/README.md) — realm
  client list with rationale for `kombats-loadtest`.
- [`../../observability/README.md`](../../observability/README.md) — how to
  read the metrics this harness exercises.
- [`../../RECON_REPORT.md`](../../RECON_REPORT.md) — service topology + hot
  path the harness drives.
