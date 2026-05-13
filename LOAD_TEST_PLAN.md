# Kombats Load-Test Plan — Phase 1 (planning only)

Target Phase 2 baseline: **50 concurrent battles / 100 virtual players / ~2 minutes** against
the local docker stack (`docker-compose.yml` + `observability/docker-compose.observability.yml`).
This document is the design contract for Phase 2; no code is in scope here.

All `file:line` citations are against the working tree on branch
`fix/observability-polish` (HEAD `76b8938`).

---

## Section 1 — JWT acquisition strategy

### 1.1 — Facts established from the code

- All five services validate JWTs the same way: shared `AddKombatsAuth(IConfiguration)`
  extension at `src/Kombats.Common/Kombats.Abstractions/Auth/KombatsAuthExtensions.cs:13-37`.
  BFF inlines an equivalent block (`src/Kombats.Bff/Kombats.Bff.Bootstrap/Program.cs:27-58`)
  per AD-17 (can't reference `Kombats.Abstractions`).
- Validation is **local JWKS** keyed by `JwtBearerOptions.Authority`
  (`KombatsAuthExtensions.cs:24`). No introspection. The .NET JWT bearer
  middleware fetches `/.well-known/openid-configuration` and JWKS once and
  caches them, refreshing on signature failure. Implication: a JWT signed by
  **any key whose public half is published in Keycloak's JWKS** validates.
- Required claims on the token:
  - `iss` must match the configured `Authority`
    (`http://localhost:8080/realms/kombats` in dev — Battle
    `src/Kombats.Battle/Kombats.Battle.Bootstrap/appsettings.json:17`).
  - `aud` must contain `kombats-api`
    (`KombatsAuthExtensions.cs:25` — sets `JwtBearerOptions.Audience`).
    Keycloak emits this via the `kombats-api-audience` protocol mapper
    (`infra/keycloak/realm.json:101-111`).
  - `sub` must be a valid GUID. Used by every service to derive the player
    identity (`src/Kombats.Common/Kombats.Abstractions/Auth/IdentityIdExtensions.cs:11-22`,
    plus Players-side parser `src/Kombats.Players/Kombats.Players.Api/Extensions/ClaimsPrincipalExtensions.cs:13-24`,
    plus Battle hub `src/Kombats.Battle/Kombats.Battle.Infrastructure/Realtime/SignalR/BattleHub.cs:89-101`).
    `Guid.TryParse(sub)` must succeed; otherwise the hub throws
    `HubException("User not authenticated")`.
  - `preferred_username` is read for display purposes only
    (`HttpCurrentIdentityProvider.cs:32`); not required to be unique and not
    a hard auth gate. Same for `email`.
- For the SignalR `/battlehub`, the token can ride in `?access_token=` because
  BFF maps it onto `context.Token` in the `OnMessageReceived` event
  (`src/Kombats.Bff/Kombats.Bff.Bootstrap/Program.cs:42-57`). The
  `Microsoft.AspNetCore.SignalR.Client` library already does this when given
  an `AccessTokenProvider`.

### 1.2 — Realm export

`infra/keycloak/realm.json` is **public-key-less**. There is no `keys`,
`components.rsa`, or PEM block embedded; only the realm metadata
(`accessTokenLifespan`, mappers, clients, four seed users at `realm.json:129-174`).
Keycloak auto-generates its RSA keypair on first realm import; the private
half lives inside Keycloak's H2/Postgres-backed component config, not in this
file. Implication: **Option A as originally framed ("use the realm's private
key") is not directly available** without either reading it out of a running
Keycloak admin REST API or replacing the auto-generated provider with a known
one.

Two further realm facts that constrain the strategy:

1. **Direct Access Grants (ROPC) are disabled on the only end-user client**
   `kombats-web` (`realm.json:90`). Hitting `/protocol/openid-connect/token`
   with `grant_type=password` against `client_id=kombats-web` returns
   `unauthorized_client`. So Option C/D, as stated, won't work out of the box
   against the shipped realm.
2. **Only four pre-baked test users** (`artem`, `kazumi`, `alice`, `jun` —
   `realm.json:129-174`). At 100 virtual players we need 100 distinct
   `sub`s (matchmaking pops two queue entries with different `sub`s — see
   Section 3); recycling four users does not work.

### 1.3 — Options matrix

| | Option A: mint w/ realm's auto-generated private key | Option B: mint w/ our keypair, patch realm to publish its public half | Option C: real Keycloak password grant per virtual player | Option D: share one Keycloak token across many players |
|---|---|---|---|---|
| Works against the shipped realm.json? | No — private key not in export | No — needs a new `components.rsa` block | No — `directAccessGrantsEnabled: false` on every client | No — single `sub` would self-pair in queue |
| Files to change | `infra/keycloak/realm.json` (or run-time Admin REST hack) | `infra/keycloak/realm.json` (+ private PEM checked in under `tests/Kombats.LoadTests/keys/`) | `infra/keycloak/realm.json` (add `kombats-loadtest` client, `directAccessGrantsEnabled: true`) + bulk-create users via admin REST API at setup | n/a (broken design — see below) |
| Crypto code in harness | Yes (RS256 signer) | Yes (RS256 signer) | No (plain HTTP POST) | No |
| Keycloak round-trips during the run | 0 | 0 | 1 per virtual player (at startup only) | n/a |
| Keycloak round-trips during ramp | 0 | 0 | ~100 ROPC POSTs in ~5s — well within Keycloak's capacity | n/a |
| Determinism (run-to-run reproducibility of `sub`s) | High | High | Medium — `sub`s are issued by Keycloak, but stable if user pool is reused across runs | n/a |
| Bug risk | High — extracting Keycloak's runtime key via admin API is fragile; the key can rotate; signing-algorithm assumptions may drift | Medium — once wired, very stable; one realm-config touch | Low — uses the production-shaped auth path | n/a |
| Portfolio defensibility | Weak — "I bypassed Keycloak signing" reads as a shortcut | Medium — "I added a static key provider for the load test" reads as deliberate but slightly hacky | **Strong — "load test hits the real OIDC token endpoint, same as the SPA"** | n/a |

**Why Option D is broken.** Matchmaking pairs by `sub` (FIFO pop of two
queue entries — `src/Kombats.Matchmaking/Kombats.Matchmaking.Application/UseCases/ExecuteMatchmakingTick/ExecuteMatchmakingTickHandler.cs:41`).
If 100 SignalR connections share one `sub`, queue join is idempotent at the
`mm:queued:{variant}` SET (`src/Kombats.Matchmaking/Kombats.Matchmaking.Infrastructure/Redis/RedisMatchQueueStore.cs:32`)
and only one queue entry survives — so 100 connections fight for one match
slot. Also, `JoinQueueHandler` rejects when the player already has an
active match (`JoinQueueHandler.cs:35-47`), so the same `sub` cannot be
in two battles at once. Killing this option.

### 1.4 — Recommendation: Option C (real password grant per virtual player)

For the Phase 2 baseline I recommend **Option C with a one-time admin-API
setup step**. Concretely:

1. **One-shot edits to `infra/keycloak/realm.json` (commit once, kept across runs):**
   - Add a new client `kombats-loadtest`:
     ```json
     {
       "clientId": "kombats-loadtest",
       "publicClient": true,
       "directAccessGrantsEnabled": true,
       "standardFlowEnabled": false,
       "protocolMappers": [
         { /* same kombats-api-audience mapper as kombats-web,
              copied verbatim from realm.json:101-111 */ }
       ]
     }
     ```
     Public client so we don't need to ship a client secret. ROPC enabled.
     The audience mapper is the only non-default bit; without it the `aud`
     claim won't contain `kombats-api` and every service will reject the
     token.
2. **Setup script (Phase 2 deliverable, not Phase 1):** `tests/Kombats.LoadTests/scripts/seed-users.sh`
   (or in-process equivalent) authenticates as `admin-cli` against the master
   realm (admin/admin per docker-compose default) and bulk-creates `N`
   users named `loadtest-{0..N-1}` with password `loadtest` in the
   `kombats` realm via `POST /admin/realms/kombats/users`. Idempotent:
   re-running with the same `N` skips already-created users.
3. **At load-test startup:** harness POSTs to
   `/realms/kombats/protocol/openid-connect/token` with
   `grant_type=password&client_id=kombats-loadtest&username=loadtest-{i}&password=loadtest`
   for each `i ∈ [0, N)`. Each response carries `access_token` (JWT with
   the audience mapper applied), `expires_in=3600`. We cache the token for
   the lifetime of the virtual player object.

Files that change under this option:
- `infra/keycloak/realm.json` (+1 client block, ~20 lines).
- New `tests/Kombats.LoadTests/scripts/seed-users.sh` (or inline in C#
  startup).
- New `tests/Kombats.LoadTests/...` project (Section 4).

Files that **don't** change:
- All `Program.cs` / `appsettings.json` of the five services (Authority +
  Audience already work).
- The shared `KombatsAuthExtensions.cs`.
- No JWT signing key checked in.
- No crypto code in the harness.

### 1.5 — Why not Option B even though it removes Keycloak from the hot path

For the baseline run (100 players, 2 min) Keycloak takes ~100 token requests
during ramp and 0 thereafter — token validation is local JWKS. Bypassing
Keycloak buys nothing measurable here and trades it for crypto code in the
harness + a private key in the repo. Re-evaluate at >1k players if Keycloak
ROPC throughput at startup becomes a bottleneck (it won't on 0.25-vCPU
Container App settings? — yes, the local stack runs without those caps).

### 1.6 — Players-side onboarding precondition

Joining the queue is not just an auth check. `JoinQueueHandler` requires the
player to have a **ready combat profile** in the Matchmaking-local
projection
(`src/Kombats.Matchmaking/Kombats.Matchmaking.Application/UseCases/JoinQueue/JoinQueueHandler.cs:50-61`).
That projection is fed asynchronously by `PlayerCombatProfileChanged` events
out of the Players service. To make a virtual player queue-eligible:

1. POST `api/v1/game/onboard` (BFF — `OnboardEndpoint.cs:14`) which
   internally hits Players' `EnsureCharacterExists`
   (`EnsureCharacterExistsHandler.cs:29-72`) → emits `PlayerCombatProfileChanged`.
2. POST `api/v1/character/name` (BFF — `SetCharacterNameEndpoint.cs:16`) →
   transitions `OnboardingState` from `Draft` to `Named`
   (`Character.cs:80-102`).
3. POST `api/v1/character/stats` with a body that consumes all 3 unspent
   points → transitions `OnboardingState` from `Named` to `Ready`
   (`Character.cs:104-137`).
4. **Wait for `PlayerCombatProfileChanged` to land in Matchmaking** — there
   is no synchronous "ready" gate; the projection is updated by
   `PlayerCombatProfileChangedConsumer`
   (`src/Kombats.Matchmaking/Kombats.Matchmaking.Bootstrap/Program.cs:142`).
   Practical wait: poll `api/v1/queue/status` (Section 5.3), or sleep ~2s
   after step 3, before step 5.
5. POST `api/v1/queue/join` (BFF — `JoinQueueEndpoint.cs` → relays to
   Matchmaking `JoinQueueEndpoint.cs:15`).

The setup script in 1.4 step 2 can short-circuit steps 1-3 if it directly
writes the Players-side `characters` row with `OnboardingState=Ready`, but
that bypasses the published `PlayerCombatProfileChanged` event so
Matchmaking never gets the profile — bad. Do steps 1-3 through the BFF for
each user once, then keep them ready forever (the seed script can do this
in C# during the same setup pass).

---

## Section 2 — SignalR contract mapping

The full set of methods and events on the realtime path between the
React SPA, BFF, and Battle.

### 2.1 — Client → server invokes

| Direction | Method (hub-side name) | Parameters | Return | Triggered by frontend at | Triggered by BFF relay at |
|---|---|---|---|---|---|
| Frontend → BFF | `JoinBattle` | `Guid battleId` | `object` (a `BattleSnapshotRealtime` shaped JSON blob) | `BattleHubManager.joinBattle` (`src/Kombats.Client/src/transport/signalr/battle-hub.ts:144`) | `BattleHub.JoinBattle` (`src/Kombats.Bff/Kombats.Bff.Api/Hubs/BattleHub.cs:31`) |
| Frontend → BFF | `SubmitTurnAction` | `Guid battleId, int turnIndex, string actionPayload` | `void` | `BattleHubManager.submitTurnAction` (`battle-hub.ts:157`) | `BattleHub.SubmitTurnAction` (`src/Kombats.Bff/Kombats.Bff.Api/Hubs/BattleHub.cs:49`) |
| BFF → Battle | `JoinBattle` | `Guid battleId` | `BattleSnapshotRealtime` | `BattleHubRelay.JoinBattleAsync` (`src/Kombats.Bff/Kombats.Bff.Application/Relay/BattleHubRelay.cs:258`) | `BattleHub.JoinBattle` (`src/Kombats.Battle/Kombats.Battle.Infrastructure/Realtime/SignalR/BattleHub.cs:42`) |
| BFF → Battle | `SubmitTurnAction` | `Guid battleId, int turnIndex, string actionPayload` | `void` | `BattleHubRelay.SubmitTurnActionAsync` (`BattleHubRelay.cs:343`) | `BattleHub.SubmitTurnAction` (`src/Kombats.Battle/...SignalR/BattleHub.cs:66`) |

**Action payload shape.** `actionPayload` is a JSON-stringified object with
three fields:
```json
{
  "attackZone": "Head",
  "blockZonePrimary": "Belly",
  "blockZoneSecondary": "Waist"
}
```
Construction reference: `src/Kombats.Client/src/modules/battle/zones.ts:64-74`.
Server-side parsing: `src/Kombats.Battle/Kombats.Battle.Application/UseCases/Turns/ActionIntakeService.cs:67-138`.
Zone values are the `BattleZone` enum names (case-insensitive,
`ActionIntakeService.cs:81,96,110`): `Head`, `Chest`, `Belly`, `Waist`,
`Legs` (`src/Kombats.Battle/Kombats.Battle.Domain/Rules/BattleZone.cs:7-14`).
Block pair must be ring-adjacent: `(Head,Chest)`, `(Chest,Belly)`,
`(Belly,Waist)`, `(Waist,Legs)`, `(Legs,Head)`
(`BattleZone.cs:24-32`). An invalid/missing payload is silently downgraded
to `NoAction` (`ActionIntakeService.cs:46-49, 60-65, 121-128, 130-136`).
Ten consecutive double-NoActions end the battle as `DoubleForfeit`
(`appsettings.json:49` `NoActionLimit=10`,
`src/Kombats.Battle/Kombats.Battle.Domain/Engine/BattleEngine.cs:191-211`).

### 2.2 — Server → client events

All emitted by `IHubContext<BattleHub>.Clients.Group($"battle:{battleId}").SendAsync(eventName, payload)`
inside `src/Kombats.Battle/Kombats.Battle.Infrastructure/Realtime/SignalR/SignalRBattleRealtimeNotifier.cs`.
Event-name constants: `src/Kombats.Battle/Kombats.Battle.Realtime.Contracts/RealtimeEventNames.cs:9-14`.

| Direction | Event name | Payload contract | Triggers when |
|---|---|---|---|
| Battle → BFF → client | `BattleReady` | `BattleReadyRealtime` (`BattleReadyRealtime.cs:7-13` — `BattleId, PlayerAId, PlayerBId, PlayerAName?, PlayerBName?`) | Right after `CreateBattleConsumer` finishes initializing battle state and opens Turn 1 (`BattleLifecycleAppService.cs:131`) |
| Battle → BFF → client | `TurnOpened` | `TurnOpenedRealtime` (`TurnOpenedRealtime.cs:6-11` — `BattleId, TurnIndex, DeadlineUtc`) | After each successful turn resolution that does NOT end the battle (`BattleTurnAppService.cs:561-605`), and at battle start for Turn 1 (`BattleLifecycleAppService.cs:132`) |
| Battle → BFF → client | `PlayerDamaged` | `PlayerDamagedRealtime` (`PlayerDamagedRealtime.cs:6-13` — `BattleId, PlayerId, Damage, RemainingHp, TurnIndex`) | Once per damage event during turn resolution — typically 0, 1, or 2 per turn |
| Battle → BFF → client | `TurnResolved` | `TurnResolvedRealtime` (`TurnResolvedRealtime.cs:6-16` — `BattleId, TurnIndex, PlayerAAction, PlayerBAction, Log?`) | Every resolved turn |
| Battle → BFF → client | `BattleStateUpdated` | `BattleStateUpdatedRealtime` (shape mirrors `BattleSnapshotRealtime` — `BattleSnapshotRealtime.cs:9-25`) | After every turn resolution, mid-battle and end-of-battle |
| Battle → BFF → client | `BattleEnded` | `BattleEndedRealtime` (`BattleEndedRealtime.cs:14-22` — `BattleId, Reason, WinnerPlayerId?, EndedAt, WinnerXp?, LoserXp?`) | When the engine reports a terminal result (KO, DoubleForfeit) |
| BFF → client only | `BattleFeedUpdated` | `BattleFeedUpdate` (frontend type — narration entries) | BFF-side narration pipeline after each `TurnResolved` or `BattleEnded` (`BattleHubRelay.cs:122-144, 166-184`). **Not emitted by Battle**; pure BFF synthesis. |
| BFF → client only | `BattleConnectionLost` | `{ Reason: string }` (anon object) | BFF detects its downstream HubConnection to Battle closed (`BattleHubRelay.cs:224-241`) |

Frontend handlers: `src/Kombats.Client/src/transport/signalr/battle-hub.ts:161-173`.
All 8 events have a registered `connection.on(...)` handler in the SPA.

### 2.3 — Mismatches / observations relevant to the harness

- **`BattleSnapshotRealtime.Phase` is serialized as a string enum**
  (`BattleHubRelay.cs:77-80` — `JsonStringEnumConverter` is added explicitly
  to the BFF→Battle downstream connection because typed `On<T>` handlers
  silently drop messages with int-encoded enums otherwise). The harness's
  downstream `HubConnection` must do the same — see Section 4.4. The
  default `HubConnectionBuilder` does **not** add `JsonStringEnumConverter`,
  so a naive harness will deserialize `Phase: "ArenaOpen"` as zero, etc.
- **`TurnResolved.PlayerAAction` / `PlayerBAction` are the canonical
  payload strings** stored in Redis (`BattleTurnAppService.cs:561-605`).
  The harness can read these to log "what the opponent did" but doesn't
  need to.
- **No `[HubMethodName]` attributes** — invocation names are the literal
  method names (verified by grep across `src/Kombats.Battle/`).
- **No hub method returns a `Task<T>` with `T` other than what's in the
  table.** `JoinBattle` is the only `Task<T>` (returns the snapshot);
  `SubmitTurnAction` is fire-and-forget except for the awaited completion
  frame.
- **Frontend `JoinBattle` retries on transient "Battle … not found"** for
  ~8 seconds (`battle-hub.ts:27-32, 138-152`). This compensates for the
  race between matchmaking publishing `CreateBattle` (which updates
  `mm:player:*` to `Matched`) and `CreateBattleConsumer` finishing battle
  initialization in Redis. The harness must replicate this — bare
  `InvokeAsync<BattleSnapshotRealtime>("JoinBattle", id)` will fail ~20% of
  the time at high concurrency.
- **`BFF.BattleConnectionLost` is unique to BFF.** It is NOT emitted by
  Battle directly. The harness, when going through BFF, must handle it
  (or ignore it).
- **No client→server method exists to leave a battle / forfeit
  voluntarily.** A client can only disconnect; Battle has no
  `LeaveBattle` / `Surrender` API. (Verified: only `JoinBattle` and
  `SubmitTurnAction` are public methods on either hub.) Implication for
  the harness: to make a battle end fast, either fight to KO or do 10
  consecutive double-NoActions for `DoubleForfeit`. Disconnecting alone
  does not end the battle for the other player.

### 2.4 — Real bugs visible at this layer

None confirmed in this pass. Two suspected divergences worth probing in
Phase 2 but not fixing here:

- **`active_signalr_connections` divergence** between BFF and Battle is
  flagged in `OBSERVABILITY_DIAGNOSIS_2.md:150` — observed `bff=2, battle=0`
  after a clean session. Could indicate a missing OnDisconnected hook on
  the BFF side or a stuck counter; load testing will likely amplify it.
- **BFF Hub's `GetAccessToken` throws on missing token**
  (`src/Kombats.Bff/Kombats.Bff.Api/Hubs/BattleHub.cs:92`). If the bearer
  drops between `OnConnectedAsync` (which increments the metric) and
  `JoinBattle` (which calls `GetAccessToken`), the gauge will not be
  decremented since the connection is up. Probably benign; flag and move
  on.

---

## Section 3 — Battle lifecycle from a single client's perspective

Walk from authenticated player to disconnect.

### 3.1 — Step-by-step

1. **Onboard via HTTP (one-time per identity).** Required because
   `JoinQueueHandler.cs:50-61` rejects identities without a `Ready` combat
   profile.
   - `POST /api/v1/game/onboard` → BFF `OnboardEndpoint.cs:14-32` → Players
     `EnsureCharacterExists`. Idempotent — second call returns the same
     character (`EnsureCharacterExistsHandler.cs:30-35`).
   - `POST /api/v1/character/name` with `{ "name": "loadbot-{i}" }` → BFF
     `SetCharacterNameEndpoint.cs:16` → Players. Transitions `Draft → Named`
     (`Character.cs:80-102`).
   - `POST /api/v1/character/stats` with a body summing to 3 (e.g.
     `{ "strength": 1, "agility": 1, "intuition": 1, "vitality": 0,
     "expectedRevision": <r> }`) → Players. Transitions `Named → Ready`
     (`Character.cs:104-137`). Note: requires the current `Revision`,
     readable from `GET api/v1/game/state`.

2. **Wait for the `PlayerCombatProfileChanged` event to land in
   Matchmaking.** Driven by MassTransit via RabbitMQ; latency is normally
   sub-second on a healthy local stack. There is **no synchronous gate** —
   the Players outbox commits in step 1 step 3, the
   `MassTransitCombatProfilePublisher` (`Program.cs:90`) publishes, and
   `PlayerCombatProfileChangedConsumer` in Matchmaking
   (`Program.cs:142`) updates `matchmaking.player_combat_profiles`. The
   harness can either:
   - Sleep ~1s. Brittle.
   - **Poll `api/v1/queue/status`** until `JoinQueue` would succeed.
     Indirectly verifies the projection.
   - Just try `api/v1/queue/join`, and on `Queue.NotReady` retry with
     backoff (`JoinQueueHandler.cs:58-61` returns this exact error code).
     **Recommended** — it explicitly checks the gate we care about.

3. **Connect SignalR to BFF.** Build a `HubConnection` to
   `http://localhost:3000/battlehub` (BFF default) with `accessTokenFactory`
   pointing at the cached JWT. Frontend reference: `battle-hub.ts:94-100`.
   Hub URL is configured via `config().bff.baseUrl` (`battle-hub.ts:95`);
   for the load test against the local stack it's the BFF port from
   `docker-compose.yml`. **Connection alone does not enter the queue or a
   battle** — it's an empty hub session until `JoinBattle` is invoked.

4. **`POST /api/v1/queue/join`** with body `{ "connectionRef": "<unique>" }`.
   Response is one of:
   - `200 { status: "Searching" }` — happy path.
   - `409 { status: "Matched", battleId: "<guid>" }` — already in a
     battle (shouldn't happen for a fresh bot but worth handling).
   - `400 problem` with code `Queue.NotReady` — combat profile not yet
     ready; retry.

5. **Poll `GET /api/v1/queue/status`** every ~500-1000 ms. Frontend uses
   `MatchmakingPoller` (`src/Kombats.Client/src/transport/polling/matchmaking-poller.ts:36`).
   Response shape `{ status, matchId?, battleId?, matchState? }`. Wait until
   `status === "Matched" && battleId != null`. There is **no SignalR push
   for "you've been paired"** — pairing notification is purely HTTP poll.
   This is the single biggest difference from a typical real-time game
   client design.

6. **`HubConnection.InvokeAsync<BattleSnapshotRealtime>("JoinBattle", battleId)`**
   with the retry policy from `battle-hub.ts:138-152` (8 attempts over ~8s,
   only retrying on "Battle <id> not found"). The returned snapshot
   contains:
   - `PlayerAId, PlayerBId` — so the bot knows which side it is
     (`me == PlayerAId ? "A" : "B"`).
   - `PlayerAHp, PlayerBHp, PlayerAMaxHp, PlayerBMaxHp` — current HP.
   - `TurnIndex, DeadlineUtc, Phase` — current turn state.

7. **Wait for `TurnOpened` event.** It will already have fired at battle
   start (`BattleLifecycleAppService.cs:132`); however SignalR Groups only
   replay messages sent after a connection joins the group, so the bot
   probably **will not have received** the original Turn 1 `TurnOpened`.
   The snapshot returned from `JoinBattle` is the source of truth for "what
   turn am I on" (`TurnIndex`, `DeadlineUtc`).

8. **Pick an action.** Action format per Section 2.1. Phase 2 baseline
   `RandomPlayerBehavior`: uniform pick over the 5 attack zones × 5 valid
   block pairs = 25 options. Per turn:
   ```
   attackZone = uniform(ALL_ZONES)
   (blockPrimary, blockSecondary) = uniform(VALID_BLOCK_PAIRS)
   payload = JSON.stringify({attackZone, blockZonePrimary, blockZoneSecondary})
   ```

9. **`HubConnection.InvokeAsync("SubmitTurnAction", battleId, turnIndex, payload)`**.
   `turnIndex` is the **current open turn** (from the last `TurnOpened`
   event or the initial snapshot). Submitting a stale `turnIndex` is
   downgraded to `NoAction` server-side; submitting the right one is
   accepted.

10. **Loop on `TurnOpened` / `TurnResolved` / `PlayerDamaged` /
    `BattleStateUpdated` events.** Update local HP from
    `BattleStateUpdated.PlayerAHp` / `PlayerBHp`. Submit the next action
    when a new `TurnOpened` arrives. Note: turn resolution can occur (a)
    **early**, when both players have submitted, or (b) **on deadline**,
    when `TurnDeadlineWorker` reaps the turn (30s configured —
    `appsettings.json:48`). The bot does not need to care which.

11. **End of battle**: `BattleEnded` event arrives with `Reason ∈
    {KnockOut, DoubleForfeit, ...}` and optional `WinnerPlayerId`. After
    this event, BFF auto-disposes the downstream Battle hub connection
    (`BattleHubRelay.cs:185-189`). The frontend Hub may also receive a
    final `BattleConnectionLost` event (`BattleHubRelay.cs:224-241`). The
    bot should record outcome, then call `connection.stop()` and exit.

12. **Disconnect**: `connection.stop()` on the BFF connection. The
    `OnDisconnectedAsync` on BFF tears down via
    `BattleHubRelay.DisconnectAsync` (`src/Kombats.Bff/Kombats.Bff.Api/Hubs/BattleHub.cs:63-72`),
    and Battle's hub decrements its connection gauge
    (`src/Kombats.Battle/...SignalR/BattleHub.cs:82-87`).
    `mm:player:{playerId}` is deleted by Matchmaking's `BattleCompletedConsumer`
    (`src/Kombats.Matchmaking/Kombats.Matchmaking.Infrastructure/Messaging/Consumers/BattleCompletedConsumer.cs:63-64`)
    asynchronously after the `BattleCompleted` outbox flush. The bot doesn't
    need to wait for this unless it intends to re-queue immediately.

### 3.2 — Steps that are subtly multi-path or could surprise

- **No `Authenticate` step on the hub.** JWT is validated by the bearer
  middleware on the negotiate request; there's no app-level `Authenticate`
  hub method. (Verified: BattleHub on both BFF and Battle have only
  `JoinBattle`, `SubmitTurnAction`, plus `OnConnectedAsync` /
  `OnDisconnectedAsync` overrides.)
- **"Whose turn is it"** is a red herring — Kombats is a simultaneous-action
  turn-based system. Both players submit independently every turn. The bot
  should always be ready to submit when `TurnOpened` fires.
- **Available actions** are not advertised by the server. There's no
  `availableActions` field in the snapshot. The bot must know the 5-zone /
  adjacent-block-pair rules. Cite `BattleZone.cs:7-14` and
  `BattleZone.cs:24-32` (`ValidBlockPatterns`).
- **Race at step 5→6**: the SignalR `JoinBattle` retry loop in the frontend
  is calibrated for the matchmaking-publishes-CreateBattle window. The
  harness must do the same retry to be reliable at high concurrency
  (Section 8 risk #2).
- **Battle Postgres history is best-effort**: the `battle_turns` table
  insert (`BattleTurnAppService.cs:534`) is fire-and-forget per turn. Bots
  cannot rely on Postgres state to discover the current turn — only Redis +
  SignalR snapshots.

### 3.3 — Variant: bot acting as Player B during Phase 2

Phase 2 baseline pairs bots against each other. The lifecycle is identical
on both sides; the only side-specific logic is "which HP value is mine"
(read snapshot, save `myPlayerId`, then `me == PlayerAId ? PlayerAHp :
PlayerBHp`).

---

## Section 4 — Project structure proposal

### 4.1 — Folder layout

```
tests/
  Kombats.LoadTests/
    Kombats.LoadTests.csproj
    Program.cs                          # CLI entry (smoke | load | probe)
    appsettings.json                    # base config (URLs, defaults)
    appsettings.Development.json        # overrides for local docker stack
    scripts/
      seed-users.sh                     # one-shot: bulk-create Keycloak users
    Auth/
      LoadTestTokenProvider.cs          # ROPC: POST /protocol/openid-connect/token
      TokenCache.cs                     # in-memory token cache, lazy refresh
    Players/
      VirtualPlayer.cs                  # lifecycle wrapper (one battle, end-to-end)
      VirtualPlayerOptions.cs           # name, BFF url, behavior, seed
      VirtualPlayerResult.cs            # outcome record (Won/Lost/Draw/Error + timings)
      IPlayerBehavior.cs                # action selection strategy interface
      Behaviors/
        RandomPlayerBehavior.cs         # uniform over 25 options (baseline)
        DoubleForfeitBehavior.cs        # always NoAction (for failure-mode tests)
    Transport/
      BffHttpClient.cs                  # POST /api/v1/queue/* and /api/v1/character/*
      BattleHubClient.cs                # owns one HubConnection to BFF /battlehub
    Scenarios/
      OneVsOneSmokeScenario.cs          # plain Task.WhenAll, no NBomber
      PairedBattlesScenario.cs          # NBomber scenario (50 pairs)
    Cli/
      SmokeCommand.cs                   # dotnet run -- smoke
      LoadCommand.cs                    # dotnet run -- load
      ProbeCommand.cs                   # dotnet run -- probe  (single bot auth check)
```

### 4.2 — Csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>      <!-- inherited from Directory.Build.props:4 -->
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <!-- Already centralized in Directory.Packages.props -->
    <PackageReference Include="Microsoft.AspNetCore.SignalR.Client" />          <!-- 10.0.3, line 41 -->
    <PackageReference Include="Microsoft.Extensions.Configuration" />           <!-- 10.0.3 -->
    <PackageReference Include="Microsoft.Extensions.Configuration.Binder" />
    <PackageReference Include="Microsoft.Extensions.Configuration.Json" />
    <PackageReference Include="Microsoft.Extensions.Http" />                    <!-- 10.0.3 -->
    <PackageReference Include="Microsoft.Extensions.Logging" />
    <PackageReference Include="Microsoft.Extensions.Logging.Console" />
    <PackageReference Include="Serilog.AspNetCore" />                           <!-- 10.0.0 -->

    <!-- NEW central versions to add to Directory.Packages.props -->
    <PackageReference Include="NBomber" />                                      <!-- 6.x latest stable -->
    <PackageReference Include="NBomber.Http" />                                 <!-- only if we add HTTP-step scenarios later; not needed for baseline -->
  </ItemGroup>

  <ItemGroup Label="Project references">
    <!-- We reuse the realtime contract DTOs so we don't redefine them. -->
    <ProjectReference Include="..\..\src\Kombats.Battle\Kombats.Battle.Realtime.Contracts\Kombats.Battle.Realtime.Contracts.csproj" />
  </ItemGroup>
</Project>
```

NBomber Community license is free for non-commercial use up to 50 concurrent
scenarios per process — see Section 5.4. Adding `NBomber` to
`Directory.Packages.props` is a one-line central-version bump; nothing else
in the codebase will reference it.

### 4.3 — `VirtualPlayer` public surface

```csharp
namespace Kombats.LoadTests.Players;

public sealed record VirtualPlayerOptions(
    string Username,           // "loadtest-{i}"
    string Password,           // "loadtest"
    string KeycloakAuthority,  // http://localhost:8080/realms/kombats
    string BffBaseUrl,         // http://localhost:3000 (or whatever docker maps)
    int RandomSeed,            // for deterministic behavior
    IPlayerBehavior Behavior); // pluggable action picker

public sealed record VirtualPlayerResult(
    Guid IdentityId,
    Guid? BattleId,
    BattleOutcome Outcome,     // Won | Lost | Draw | Error
    string? ErrorMessage,
    int TurnsPlayed,
    TimeSpan QueueWait,        // from JoinQueue to first Matched poll response
    TimeSpan BattleDuration,   // from JoinBattle to BattleEnded
    TimeSpan TotalDuration);

public enum BattleOutcome { Won, Lost, Draw, Error, Timeout }

public sealed class VirtualPlayer : IAsyncDisposable
{
    public VirtualPlayer(VirtualPlayerOptions options, ILogger<VirtualPlayer> logger);

    // Ensures Players-side onboarding (idempotent). Call once per identity.
    // Caches the token from Keycloak. Safe to call many times.
    public Task EnsureReadyAsync(CancellationToken ct);

    // The full single-battle flow. Internally:
    //   1. EnsureReadyAsync (no-op if already ready)
    //   2. Connect SignalR to BFF
    //   3. POST /queue/join
    //   4. Poll /queue/status until Matched + battleId
    //   5. JoinBattle (with the 8-step retry loop from battle-hub.ts:138-152)
    //   6. Loop: await TurnOpened, submit action, await BattleStateUpdated
    //   7. On BattleEnded, return result
    public Task<VirtualPlayerResult> RunOneBattleAsync(CancellationToken ct);

    public Guid IdentityId { get; }
    public ValueTask DisposeAsync();
}

public interface IPlayerBehavior
{
    // Called once per turn. Implementations must be thread-safe; same
    // behavior instance can be shared across players, but the seed is
    // per-player so the Random must be passed in.
    string PickActionPayload(int turnIndex, BattleSnapshot snapshot, Random rng);
}

public sealed record BattleSnapshot(
    Guid BattleId, Guid MyId, Guid OpponentId,
    int MyHp, int OpponentHp, int MyMaxHp, int OpponentMaxHp,
    int TurnIndex);
```

### 4.4 — `BattleHubClient` outline

```csharp
namespace Kombats.LoadTests.Transport;

internal sealed class BattleHubClient : IAsyncDisposable
{
    public BattleHubClient(string bffBaseUrl, Func<string> accessTokenFactory, ILogger logger);

    public Task ConnectAsync(CancellationToken ct);

    // Mirrors the frontend retry loop (battle-hub.ts:138-152) — retries on
    // "Battle <id> not found" HubException up to 8 times totaling ~8s.
    public Task<BattleSnapshotRealtime> JoinBattleAsync(Guid battleId, CancellationToken ct);

    public Task SubmitTurnActionAsync(Guid battleId, int turnIndex, string payload, CancellationToken ct);

    // Event sinks (set once before ConnectAsync; not thread-safe).
    public event Action<TurnOpenedRealtime>? TurnOpened;
    public event Action<TurnResolvedRealtime>? TurnResolved;
    public event Action<PlayerDamagedRealtime>? PlayerDamaged;
    public event Action<BattleStateUpdatedRealtime>? BattleStateUpdated;
    public event Action<BattleEndedRealtime>? BattleEnded;
    public event Action? BattleConnectionLost;

    public ValueTask DisposeAsync();
}
```

Critical detail: the `HubConnectionBuilder` must register a
`JsonStringEnumConverter` for the payload protocol, same as
`BattleHubRelay.cs:77-80`, otherwise typed `On<T>` handlers will silently
drop messages.

### 4.5 — CLI surface

```
dotnet run --project tests/Kombats.LoadTests -- smoke
    # 1 pair of bots, plain Task.WhenAll, ~30s timeout, no NBomber.
    # Exit code 0 iff both bots reach BattleEnded.

dotnet run --project tests/Kombats.LoadTests -- probe --username loadtest-0
    # 1 bot, EnsureReadyAsync + connect SignalR + join queue + leave queue.
    # Verifies auth + onboarding works. No battle.

dotnet run --project tests/Kombats.LoadTests -- load \
    --pairs 50 --duration 2m --report-folder ./reports
    # NBomber scenario, 50 concurrent battles, 2-minute target.
    # Outputs NBomber HTML to ./reports/<timestamp>/report.html.
```

### 4.6 — `appsettings.json` shape

```json
{
  "Keycloak": {
    "Authority": "http://localhost:8080/realms/kombats",
    "Client": "kombats-loadtest",
    "Password": "loadtest"
  },
  "Bff": {
    "BaseUrl": "http://localhost:3000",
    "HubPath": "/battlehub"
  },
  "Scenario": {
    "Pairs": 50,
    "Duration": "00:02:00",
    "PerBotTimeout": "00:02:00",
    "SeedBase": 42
  },
  "Reporting": {
    "Folder": "./reports"
  }
}
```

All scalar fields are overridable via CLI flags (`--pairs`, `--duration`,
`--bff-url`, `--keycloak-authority`, `--seed`).

---

## Section 5 — NBomber scenario design

### 5.1 — Scenario unit: "one full battle" vs "one player session"

**Pick: one player session per scenario.** A scenario in NBomber is one
"virtual user thread" performing a sequence of steps. The natural mapping
is one VirtualPlayer per scenario instance, because:

- NBomber treats scenarios as independent — they cannot communicate.
  Modeling "one full battle" as a scenario would require atomic spawning
  of two coordinated bots inside one scenario step, which fights the
  framework.
- "Per-player session" is what the user-facing system measures (latency,
  throughput, error rate). Aggregating to "per-battle" via post-processing
  is straightforward in NBomber's reporting.
- A pair forms naturally: with 2N scenario instances and a FIFO queue,
  each pair of consecutive `JoinQueue` calls will match up.

Trade-off: per-battle metrics (turns-per-battle, win/loss ratio,
battle-duration histogram) must be reconstructed post-hoc from per-bot
records by joining on `battleId`. Acceptable — the harness emits a JSONL
event log alongside the NBomber report so this is trivial offline.

### 5.2 — Scenario shape

```
PairedBattlesScenario:
  Step "auth"          → LoadTestTokenProvider.GetTokenAsync(username)
  Step "ensure_ready"  → BffHttpClient.OnboardAsync + SetNameAsync + AllocateStatsAsync
                         (idempotent; no-ops on repeat)
  Step "connect_hub"   → BattleHubClient.ConnectAsync
  Step "join_queue"    → BffHttpClient.JoinQueueAsync
  Step "wait_match"    → poll BffHttpClient.GetQueueStatusAsync every 500ms until Matched
  Step "battle"        → BattleHubClient.JoinBattleAsync + turn loop until BattleEnded
  Step "disconnect"    → BattleHubClient.DisposeAsync
```

NBomber gives latency histograms per step. The `battle` step is the long
one (typically tens of seconds) and is what we care most about for the
load report. `wait_match` is the matchmaking-pairing-latency proxy.

### 5.3 — Pairing under per-scenario isolation

Scenarios cannot share state, but they all hit the same Matchmaking
Redis queue. As long as ramp-up is fast enough that scenarios overlap
in the queue, FIFO pop will pair them. Concretely:

- NBomber `Inject` with `rate = 50/s, interval = 1s, during = 1s` injects
  50 bots in the first second. They each hit `/queue/join` within ~200ms
  of each other. Pairing worker tick is 100ms
  (`src/Kombats.Matchmaking/Kombats.Matchmaking.Bootstrap/appsettings.json:27`).
  Within 200ms-1s, all 25 pairs are formed.
- Use **even bot count** (2 × pairs target). For 50 battles target, inject
  100 scenarios.

### 5.4 — NBomber Community license limits

The current NBomber free Community license caps a load test at
**50 concurrent scenario instances** at any one time (and rate-limits
total scenario starts). The baseline target of "50 concurrent battles =
100 concurrent bots" **exceeds** this cap.

Two options:
1. **Halve the target for the baseline run**: 50 concurrent bots = 25
   concurrent battles. Document the 50/25 split as the Community cap and
   move on.
2. **Run two NBomber processes** side-by-side (each at 50 bots). Pairing
   still works because they share the Matchmaking queue. Reports are
   produced separately and stitched.

**Recommended**: option (1) for the first run, document the cap in the
report, and revisit if 50 bots / 25 battles is a smaller signal than we
need. This needs **user confirmation** (Section 9 Q1) because the
"50 concurrent battles" baseline figure from the prompt may have been
chosen without knowing the Community cap.

### 5.5 — Handling odd pairings

If an odd number of bots queue (e.g. 99 due to one auth failure) or one
bot disconnects from the queue, one bot stays in `Searching`. After the
scenario's `wait_match` step times out, the bot calls
`POST /api/v1/queue/leave` and ends the scenario with `Outcome.Timeout`.
Timeout default: 60s (longer than the matchmaking pairing window of
~200ms, short enough that NBomber's scenario doesn't stall the report).

The Matchmaking `MatchTimeoutWorker`
(`src/Kombats.Matchmaking/Kombats.Matchmaking.Bootstrap/Workers/MatchTimeoutWorker.cs`)
plus `QueuePresenceSweepWorker` together will clean up server-side state
for any abandoned queue entries within ~20s
(`src/Kombats.Matchmaking/Kombats.Matchmaking.Bootstrap/appsettings.json:37`),
so re-running the scenario doesn't pile up stale entries.

---

## Section 6 — Determinism strategy

### 6.1 — Per-player RNG seed

`VirtualPlayerOptions.RandomSeed` defaults to `SeedBase + playerIndex`
(SeedBase=42 in `appsettings.json`, playerIndex=0..N-1). Inside
`VirtualPlayer`, the RNG is constructed once: `new Random(seed)`. The
behavior implementation **must** take the RNG as a parameter rather than
calling `Random.Shared`. This ensures reruns with the same `(SeedBase, N)`
produce identical action sequences per bot.

Caveat: server-side resolution is also seed-driven
(`src/Kombats.Battle/Kombats.Battle.Domain/Engine/BattleEngine.cs:` uses
`DeterministicTurnRng.Create(seed, turnIndex)`), but the per-battle seed
is generated by `_seedGenerator.GenerateSeed()`
(`BattleLifecycleAppService.cs:89`) which is NOT a CLI-controllable knob —
results will differ run-to-run even with identical bot actions. Bot-side
determinism gets us deterministic *inputs* but not deterministic
*outcomes*. That's the right scope for this iteration.

### 6.2 — Player ID generation

Each `loadtest-{i}` user gets a Keycloak-issued `sub` (UUIDv4) on
creation, which is stable across runs once the user exists in the realm.
The seed-users script creates users named `loadtest-{i}` for `i ∈ [0, N)`;
re-running with `N=200` creates `loadtest-{0..199}` (no-op for already-
existing). This gives us a stable identity per bot index across all
future runs.

Alternative considered: have the harness generate GUIDs and assert them
into Keycloak. Rejected — Keycloak controls `sub` at user-creation time
in the Admin API; specifying it explicitly requires elevated permissions
we'd rather not configure.

### 6.3 — Other determinism levers

- **Pair assignment is FIFO over queue join order**, so the order in
  which scenarios call `/queue/join` determines who pairs with whom. NBomber
  `Inject` with a fixed timing schedule plus per-player startup delays
  drawn from a seeded RNG keeps this stable.
- **Battle seed is server-generated and not controllable.** Outcomes will
  differ run-to-run.
- **Action submission order within a turn** matters for the
  `StoreActionAndCheckBothSubmittedAsync` Lua return value (one player
  sees `bothSubmitted=true` and triggers early resolution). Submission
  timing depends on bot startup race; nothing we can do.

### 6.4 — Reproducibility scope

Same `(SeedBase, N, scenario timing)` reproduces:
- Same `sub`s for same `i`.
- Same action sequence each bot would emit conditional on snapshot state.

Doesn't reproduce:
- Server-side battle resolution outcomes.
- Pair assignment beyond the queue join ordering (which itself drifts on
  contention).

This is the right amount of determinism for a portfolio baseline.

---

## Section 7 — Verification plan

Each check has an exact command and a success criterion.

### 7.1 — Stack up

```
docker compose \
  -f docker-compose.yml \
  -f observability/docker-compose.observability.yml \
  -f observability/docker-compose.observability.override.yml \
  up -d
```

Success: all 5 backend service containers `Up`, plus postgres / redis /
rabbitmq / keycloak / otel-collector / prometheus / grafana / jaeger.
`docker compose ps` shows 9 containers (5 services + 4 infra +
4 observability).

### 7.2 — Build clean

```
dotnet build tests/Kombats.LoadTests/Kombats.LoadTests.csproj -c Release
```

Success: exit 0, no warnings except framework-shipped ones, output
binary at `tests/Kombats.LoadTests/bin/Release/net10.0/Kombats.LoadTests.dll`.

### 7.3 — Seed users

```
tests/Kombats.LoadTests/scripts/seed-users.sh 100
```

Success: idempotent. After first run, `100` users named `loadtest-0` …
`loadtest-99` exist in the `kombats` realm. Verify with:
```
curl -s -u admin:admin \
  "http://localhost:8080/admin/realms/kombats/users?max=200" | jq '. | length'
```
Should report `≥100`.

### 7.4 — Single-bot probe

```
dotnet run --project tests/Kombats.LoadTests -- probe --username loadtest-0
```

Success: exit 0, log shows:
- ROPC succeeded (got JWT).
- `EnsureCharacter` succeeded (HTTP 200 from BFF).
- `JoinQueue` succeeded with status=Searching.
- After 60s timeout, `LeaveQueue` succeeded with status=Left.

This verifies auth + HTTP + onboarding before we add SignalR.

### 7.5 — Smoke test (1 pair, plain Task.WhenAll)

```
dotnet run --project tests/Kombats.LoadTests -- smoke
```

Success: exit 0. Two `VirtualPlayerResult` records logged; one has
`Outcome=Won`, the other `Outcome=Lost` (or both `Draw` if they reached
the NoActionLimit, possible if the seeded behavior coincidentally
deadlocks). Both have `TurnsPlayed > 0` and `BattleId` non-null and equal
between the two records.

Sanity probe in Prometheus during the run:
```
curl -sG http://localhost:9090/api/v1/query \
  --data-urlencode 'query=active_battles{service="battle"}'
```
Should report `value=1` for several seconds, then `0`.

### 7.6 — Small load test (10 pairs)

```
dotnet run --project tests/Kombats.LoadTests -- load \
  --pairs 10 --duration 30s --report-folder ./reports
```

Success: exit 0. NBomber report at `./reports/<timestamp>/report.html`
opens cleanly and shows ≥18 of 20 scenarios completed (allow 10% for
queue-timeout edge cases). Per-step latency table populated for
`auth`, `ensure_ready`, `connect_hub`, `join_queue`, `wait_match`,
`battle`, `disconnect`.

### 7.7 — Baseline load test (Section 5.4-constrained)

```
dotnet run --project tests/Kombats.LoadTests -- load \
  --pairs 25 --duration 2m --report-folder ./reports
```

(25 instead of 50 due to NBomber Community license cap — see 5.4.)

Success criteria:
- HTML report renders.
- `active_battles` in Prometheus reaches ≥20 sustained for ≥30s during the
  run window — verify with:
  ```
  curl -sG http://localhost:9090/api/v1/query \
    --data-urlencode 'query=max_over_time(active_battles{service="battle"}[2m])'
  ```
- `turn_resolution_duration_milliseconds_count` for `service="battle"`
  is monotonically increasing throughout the run.
- Per-bot error rate < 5%.

If any of these fail, that's our first finding for the report (Section 8).

### 7.8 — Observability smoke after the run

```
open http://localhost:3001                  # Grafana, dashboard "Kombats overview"
open http://localhost:16686                 # Jaeger
```

Success: Grafana's "active_battles" panel shows the load envelope; turn
resolution latency panel populates (modulo the metric-name fix from
`OBSERVABILITY_DIAGNOSIS_2.md` Section D fix #2 — if that fix is still
pending at Phase 2 time, the panel will be empty by design; verify the
raw metric is present via the Prometheus query above instead). Jaeger
shows traces for at least one `battle` service span per resolved turn.

---

## Section 8 — Risks and unknowns

| # | Risk | Likelihood | Mitigation / fallback |
|---|---|---|---|
| 1 | JWT signing key is not in the realm export. | **Confirmed** — `infra/keycloak/realm.json` has no `keys` block (Section 1.2). | Already addressed by choosing Option C (Section 1.4). |
| 2 | `JoinBattle` fails with "Battle … not found" under load. | **High** at >25 concurrent battles — the same race the frontend retries against (`battle-hub.ts:32-36`). | Implement the same retry policy in `BattleHubClient.JoinBattleAsync` (Section 4.4). |
| 3 | NBomber Community license caps at 50 concurrent. | **Confirmed** — Section 5.4. | Run at 25 pairs (= 50 bots) for the baseline; document the cap; if needed, run two processes or upgrade the license. |
| 4 | Matchmaking has skill-based pairing that prevents bot-vs-bot matches. | **Low** — verified FIFO-only (`ExecuteMatchmakingTickHandler.cs:41`, RECON_REPORT Section 3.4). | No mitigation needed. |
| 5 | Battle never ends if both bots play passively forever (no auto-surrender). | **Low** — `NoActionLimit=10` (`appsettings.json:49`) terminates as `DoubleForfeit` after 10 consecutive double-no-actions = 5 min wall clock at the 30s turn timer. With `RandomPlayerBehavior` the bots always send valid actions and battles end via KO in tens of seconds. | Add a hard `PerBotTimeout` (default 2 min) in `VirtualPlayerOptions`; abort the bot on timeout. |
| 6 | `BattleHub` method names change between binary builds. | **Low** — hub methods are stable per `BattleHub.cs` (no `[HubMethodName]`); not modified in active feature branches. | No mitigation needed; if changed, harness fails fast on `InvokeAsync`. |
| 7 | Hub method signatures don't match what the SPA calls. | **Low** — verified identical (Section 2.1). | None. |
| 8 | BFF's process-local `BattleHubRelay._connections` map fills its 0.5Gi container memory budget at high concurrency. | **Medium at 50+ battles**, RECON_REPORT Section 9. | The baseline (25 pairs) is well under the threshold; flag in the report if memory_usage_bytes for `service="bff"` climbs sharply. |
| 9 | Postgres pool exhaustion: each service has `Maximum Pool Size=20` (RECON_REPORT Section 4.1). 50 simultaneous turn resolutions could saturate. | **Medium**. | Already observable via the Npgsql meter (after the `AddMeter("Npgsql")` fix from OBS_DIAGNOSIS_2.md — verify it's been applied at Phase 2 time, see Section 9 Q5). Report `db_client_connections_busy` as a finding. |
| 10 | Single shared Postgres pool ceiling 80 connections cluster-wide means migrations or other startup connections compete with the load. | **Low** — migrations are a one-shot job (`Kombats.Migrator`). | No mitigation; flag if startup races visible. |
| 11 | RabbitMQ broker stalls under `BattleCompleted` burst. | **Low at 25 battles**. | `MessagingOptions.ConcurrentMessageLimit=8` and `PrefetchCount=32` per service (RECON_REPORT Section 9). 25 simultaneous `BattleCompleted` events well within limits. Flag if `messaging.masstransit.consume.duration` spikes. |
| 12 | Keycloak ROPC throughput at startup (~100 ROPC POSTs in <5s). | **Low** — Keycloak handles ~hundreds of token requests per second on a dev box. | If observed slow, stagger startups by 10ms each. |
| 13 | Test users created in Keycloak persist across runs and accumulate. | **Low** — idempotent by username; the seed script skips existing users. | Add `--clean` flag to seed script for tear-down. |
| 14 | `OBSERVABILITY_DIAGNOSIS_2.md` Section D fixes #2 (rename `turn_resolution_duration_ms` → `turn_resolution_duration`) and #3 (add `AddMeter("Npgsql")`) may or may not be applied by Phase 2. | **Medium** — they're listed as recommended-not-applied. Affects which metric name the verification script in Section 7 queries. | Section 9 Q5 asks the user. The verification plan covers both possibilities. |
| 15 | The `Kombats.Battle.Realtime.Contracts` project reference from the load-test project pulls in nothing but DTOs, but transitively might pull domain code. | **Low** — verified the contracts assembly has no project references (it's pure DTOs per `RECON_REPORT.md` Section 2.6). | None. |
| 16 | Pair imbalance because one bot fails auth or onboarding mid-ramp; the other waits forever. | **Medium**. | `wait_match` step has a 60s timeout; the bot calls `/queue/leave` and reports `Outcome.Timeout`. |
| 17 | The `active_signalr_connections` divergence between BFF and Battle (OBS_DIAGNOSIS_2.md Section E Q1) means the gauge drifts during load. | **Confirmed** — visible at low load even now. | Mention in the report but don't block on it. |
| 18 | `BattleEnded` may arrive at the same time as the SignalR completion frame for the final `SubmitTurnAction` (`BattleHubRelay.cs:351-362`). The harness must tolerate `OperationCanceledException` on the final invoke. | **Confirmed pattern**. | Wrap the final invoke in try/catch for `OperationCanceledException` / `TaskCanceledException` and treat as success if `BattleEnded` was already received. |
| 19 | NBomber needs to be added to `Directory.Packages.props`. The cental package management rules require it. | **Low** — one-line change. | Include in the Phase 2 implementation diff. |
| 20 | Docker compose ports are not pinned in the repo (RECON_REPORT didn't explicitly list them). | **Medium** — needs verification before Phase 2; if BFF doesn't expose 3000 to host, the harness can't reach it. | Section 9 Q3. |

---

## Section 9 — Open questions for the user

Numbered in priority order — the first three matter for whether Phase 2
can start without follow-up.

1. **NBomber Community license cap (50 concurrent scenarios) vs. the
   "50 concurrent battles / 100 virtual players" target from the prompt.**
   Option A: drop the baseline to 25 pairs (50 bots). Option B: run two
   NBomber processes. Option C: upgrade the license. Which do you want?
2. **Should we modify `infra/keycloak/realm.json` to add a
   `kombats-loadtest` client with `directAccessGrantsEnabled: true`?**
   It's the cleanest path (Section 1.4) but it does touch a config that's
   also used by the real frontend and the deploy. If you'd rather not
   ship a load-test client in the canonical realm, the alternative is to
   keep a separate `realm-loadtest.json` and import it only when running
   tests.
3. **What's the exact BFF port on the local docker stack?**
   The frontend uses `config().bff.baseUrl` which we don't have a single
   pinned value for in this codebase. Phase 2 needs the canonical
   `http://localhost:???` URL — please confirm or point me at
   `docker-compose.yml` line that maps it. (RECON_REPORT didn't surface
   this.)
4. **Do you want Phase 2's load run to also exercise the BFF's
   `BattleFeedUpdated` narration pipeline (Section 2.2), or skip it?**
   The narration code runs server-side regardless; the harness can
   either record those events or ignore them. Recording is useful for
   measuring BFF CPU; ignoring keeps the report focused on the hot path.
5. **Are the OBS_DIAGNOSIS_2.md Section D fixes #2 and #3 going to land
   before Phase 2 begins?** They affect which Prometheus metric names
   the verification script queries (Section 7.7). If not, the
   verification script needs to query the legacy
   `turn_resolution_duration_ms_milliseconds_count` name.
6. **Per-bot timeout default**: 2 minutes (`PerBotTimeout` in
   `VirtualPlayerOptions`)? At 30s/turn × 10-no-action-limit that's the
   pathological max battle duration. Acceptable, or set lower?
7. **Should we add the seed-users script as a one-shot dotnet program
   instead of a shell script?** Shell-script is faster to write; a
   dotnet program is more portable on Windows (you might dev on macOS but
   it matters for CI later).
8. **Do you want NBomber reports committed to the repo or kept out
   (`.gitignore`)?** Portfolio readers benefit from a checked-in sample
   report; CI runs will produce new ones each time. Recommend
   `.gitignore reports/` with one curated `reports/sample/` checked in.
9. **Onboarding via BFF for each new bot is slow (3 HTTP round-trips per
   bot at startup, ~200ms each = 20s of ramp for 100 bots).** Alternative:
   seed the Players-side `characters` table directly via a separate
   migration in `Kombats.Migrator` for load-test identities. Worth the
   complexity? My recommendation: no — the 20s overhead is once-per-user
   (idempotent), not per-run, so it's amortized after the first run.
10. **`Kombats.Battle.Realtime.Contracts` project reference from the
    load-test project — acceptable cross-tier coupling?** It saves us
    redefining ~80 lines of DTOs in the harness. Alternative is
    duplicating the records, which is brittle but keeps the load-test
    tree truly leaf. Recommend: reference the contracts project.

---

## Appendix A — One-shot files Phase 2 should touch

- `Directory.Packages.props` — add `<PackageVersion Include="NBomber" Version="…"/>`.
- `infra/keycloak/realm.json` — add `kombats-loadtest` client block (~20 lines).
- New `tests/Kombats.LoadTests/` tree per Section 4.1.

## Appendix B — Files Phase 2 must NOT touch

- Any `src/Kombats.*/Kombats.*.Bootstrap/Program.cs`.
- Any `src/Kombats.*/...appsettings.json`.
- `src/Kombats.Common/Kombats.Abstractions/Auth/KombatsAuthExtensions.cs`.
- `src/Kombats.Client/`.
- The observability stack — already verified end-to-end per
  `OBSERVABILITY_DIAGNOSIS_2.md`.

