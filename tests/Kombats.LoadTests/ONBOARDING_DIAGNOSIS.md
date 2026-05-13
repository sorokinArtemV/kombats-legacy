# Onboarding diagnosis — 25-pair baseline run

**Verdict on the original hypothesis ("VirtualPlayer skips or mis-does
onboarding so bots reach matchmaking without a valid combat profile"):**
**denied.** Onboarding is functionally correct. The 21 QueueTimeouts and
14 "Battle not found" errors in the run have a different root cause —
identified below in Section 5.

---

## Section 1 — Canonical frontend onboarding flow

Read off the React SPA. Every HTTP call from auth completion to "queue-ready":

1. `POST /api/v1/game/onboard` — fired automatically on first arrival when
   `isLoaded && !isCharacterCreated`
   (`src/Kombats.Client/src/modules/onboarding/hooks.ts:30, 36-44`). Server
   creates a `Character` in `Draft` state with `revision=1`, 3 unspent
   points, default stats 3/3/3/3, default avatar `shadow_oni`
   (`src/Kombats.Players/Kombats.Players.Domain/Entities/Character.cs:39-61`).
   **Idempotent.**
2. `POST /api/v1/character/name` with `{name}` — `Draft → Named` server-side
   (`Character.cs:80-102`), **revision +1**. Fired from
   `submitOnboarding`
   (`src/Kombats.Client/src/modules/onboarding/screens/NameSelectionScreen.tsx:27`).
3. `POST /api/v1/character/avatar` with `{expectedRevision: rev+1, avatarId}`
   — sets the avatar, **revision +1** (`Character.cs:63-78`). Same submit
   handler as step 2
   (`NameSelectionScreen.tsx:31-35`). State doesn't change (stays `Named`).
4. `POST /api/v1/character/stats` with
   `{expectedRevision, strength, agility, intuition, vitality}` — `Named →
   Ready` (`Character.cs:130-133`), **revision +1**. Fired from the
   InitialStatsScreen via the shared hook
   (`src/Kombats.Client/src/modules/player/useAllocateStats.ts:95-102`).
   `totalAdded > 0` is enforced client-side
   (`useAllocateStats.ts:152-154`); server raises `ZeroPoints` otherwise
   (`Character.cs:118`).

While searching, the SPA additionally fires:

5. `POST /api/v1/queue/heartbeat` every **10 s**
   (`src/Kombats.Client/src/modules/matchmaking/hooks.ts:20, 274-276`) while
   the derived UI status is `searching`. The presence ref TTL is 15 s
   server-side (`src/Kombats.Matchmaking/Kombats.Matchmaking.Bootstrap/appsettings.json:35`),
   so missing this call drops the player from the queue (Section 5).

`GET /api/v1/game/state` is polled by TanStack Query throughout — provides
the live `OnboardingState` + `revision` the next mutation needs but is not
itself a state-transition.

After step 4 returns and the character store sees `OnboardingState=Ready`,
`OnboardingGuard` redirects to `/lobby` (`guard-decisions.ts:38-55`) and
the user clicks "Find match", firing `POST /api/v1/queue/join`.

---

## Section 2 — What VirtualPlayer actually does

`tests/Kombats.LoadTests/VirtualPlayer/VirtualPlayer.cs:183-237` —
`EnsureReadyAsync`:

1. `POST /api/v1/game/onboard` (`VirtualPlayer.cs:186`, via
   `BffHttpClient.OnboardAsync` →
   `Transport/BffHttpClient.cs:29-39`).
2. If `state == "Draft"` → `POST /api/v1/character/name`
   with `{name: username}` (`VirtualPlayer.cs:191-195`,
   `BffHttpClient.cs:41-58`).
3. **(no avatar call — see Section 3 mismatch row #3)**.
4. If `state != "Ready"` → `POST /api/v1/character/stats` with
   `{expectedRevision: revision + 1, strength: 1, agility: 1, intuition: 1,
   vitality: 0}` (`VirtualPlayer.cs:198-215`,
   `BffHttpClient.cs:60-76`). Retries once on revision-drift exception
   using a fresh `GET /api/v1/game/state`.
5. A trailing 10-iteration "wait for projection" loop
   (`VirtualPlayer.cs:217-236`) that polls `GET /api/v1/queue/status`.
   **Note**: the body of the loop is dead code — there is a bare
   `return;` at the bottom of the `try` block
   (`VirtualPlayer.cs:230`) that unconditionally exits after the first
   iteration. Not a defect that affects readiness (Section 5 has the real
   readiness check that does matter), but worth fixing for clarity.

After `EnsureReadyAsync`, the bot connects SignalR, then `POST
/api/v1/queue/join` (which has its own
`Queue.NotReady` / `Queue.NoCombatProfile` / "Invalid request to
Matchmaking" 400-retry loop in
`Transport/BffHttpClient.cs:78-126`), then `PollUntilMatchedAsync`
polling every 500 ms.

**`POST /api/v1/queue/heartbeat` is never called.**

---

## Section 3 — Diff

| Step | Frontend | VirtualPlayer | Match? |
|---|---|---|---|
| 1. `POST /game/onboard` | Yes, auto on first arrival | Yes, always | ✅ |
| 2. `POST /character/name` | Yes, with `{name}` | Yes if `state==Draft`, `{name: username}` (12 chars, within the 3–16 range — `Character.cs:93`) | ✅ |
| 3. `POST /character/avatar` | Yes, with `{expectedRevision: rev+1, avatarId}` | **No — skipped** | ❌ |
| 4. `POST /character/stats` | Yes, with `expectedRevision = latestRev` (post-name + post-avatar) | Yes if `state!=Ready`, `expectedRevision = revision+1` | ⚠️ revision will be 1 lower than the frontend's, but server-side it matches because we didn't increment via avatar — see Section 4 |
| 5. `POST /queue/heartbeat` (every 10 s while queued) | Yes, fires every 10 s | **No — never** | ❌ |
| Wait for matchmaking projection of `PlayerCombatProfileChanged` | (Implicit — frontend doesn't wait, retries via UI on failure) | Tries to wait (`EnsureReadyAsync` step 5) but dead-code loop; the actual retry happens later in `JoinQueueAsync` on 400 NotReady | ⚠️ functionally OK |

Two real mismatches: missing avatar call (row 3) and missing heartbeat
(row 5).

---

## Section 4 — Smoking-gun DB check

Queried `players.characters` directly via
`docker compose exec postgres psql ...`:

```
SELECT count(*) AS total,
       sum(CASE WHEN onboarding_state=0 THEN 1 ELSE 0 END) AS draft,
       sum(CASE WHEN onboarding_state=1 THEN 1 ELSE 0 END) AS named,
       sum(CASE WHEN onboarding_state=2 THEN 1 ELSE 0 END) AS ready
FROM players.characters;
```

| total | draft | named | **ready** |
|---|---|---|---|
| 52 | 0 | 0 | **52** |

Plus per-row sample (first 10 loadbots) — all show `onboarding_state=2`
(Ready) with stats `4/4/4/3` (the base `3/3/3/3` + my bot's
`+1/+1/+1/+0`) and double-digit `wins+losses` counts indicating they
fought many battles.

Matchmaking projection mirror (`matchmaking.player_combat_profiles`):

```
SELECT count(*) AS total, sum(CASE WHEN is_ready THEN 1 ELSE 0 END)
FROM matchmaking.player_combat_profiles;
```

| total | ready_count |
|---|---|
| 52 | 52 |

And in `battle.battles`: **305 rows** — battles really did get created and
completed.

Matches table:

| state | count |
|---|---|
| 3 | 303 |
| 4 | 2 |

(state=3 is `Completed`, state=4 is `TimedOut` per the match state enum.)

**Conclusion: every seeded loadbot reached Ready in both Players and the
Matchmaking projection. Onboarding is not the failure mode.** The skipped
avatar step in Section 3 row 3 is cosmetic — the avatar column defaults
to `shadow_oni` server-side (`players.characters` DDL: `DEFAULT
'shadow_oni'`) and matchmaking doesn't consult it.

---

## Section 5 — Real root cause: missing queue heartbeat

The QueueTimeout count (21/167 ≈ 13 %) is explained by a server-side
sweep that VirtualPlayer doesn't compensate for:

1. **`POST /api/v1/queue/join` registers a presence ref with TTL 15 s** —
   `mm:queue:presence:refs:{identityId}` SET in Redis DB 1
   (`src/Kombats.Matchmaking/Kombats.Matchmaking.Infrastructure/Redis/RedisQueuePresenceStore.cs:68`,
   TTL from `Matchmaking.appsettings.json:35` `PresenceTtlSeconds: 15`).
2. **The frontend keeps the ref alive by pinging
   `POST /api/v1/queue/heartbeat` every 10 s**
   (`src/Kombats.Client/src/modules/matchmaking/hooks.ts:259-289`,
   constant on line 20 `HEARTBEAT_INTERVAL_MS = 10_000`).
3. **`QueuePresenceSweepWorker` runs every 20 s**
   (`Matchmaking.appsettings.json:37` `SweepIntervalSeconds: 20`). For
   each identity whose presence has aged past `StaleAfterSeconds=15`
   (`appsettings.json:36`), the sweep
   (a) ZREMs them from `mm:queue:{variant}`, then
   (b) deletes their `mm:player:{playerId}` status key
   (`QueuePresenceSweepWorker.cs:64-117`).
4. **VirtualPlayer never sends a heartbeat.** Grep confirms:
   ```
   grep -nE 'heartbeat|Heartbeat' tests/Kombats.LoadTests/...  →  no matches
   ```

Result: a bot that doesn't get paired in the first ~15 s gets ejected
from the queue by the sweep, then continues polling `/queue/status`
(which now returns NotInQueue) until its 60 s `QueueTimeoutSeconds`
expires (`appsettings.json:14`). The harness logs this as
`BattleOutcome.QueueTimeout`. The 305 successful battles in `battle.battles`
came from the bots that DID get paired inside the 15-second window — and
under `KeepConstant(25)` ramp, most do, but ~13 % drift.

This also explains the active_battles Grafana peak of 3: with bots
constantly cycling in and out under sweep pressure, sustained concurrent
battles are throttled, and the OTel 5 s gauge sample rarely catches more
than a few mid-flight.

The "Battle not found" count (14) is a different mechanism — the same
race the SPA's
`battle-hub.ts:32-36, 138-152` retries against, where Matchmaking has
published `CreateBattle` and updated `mm:player:*` to `Matched` but
Battle's `CreateBattleConsumer` hasn't yet initialized state in Redis.
The harness mirrors the SPA's 8-attempt / ~8 s budget
(`BattleHubClient.cs:18, 79-100`). Under load the
RabbitMQ→consumer chain occasionally exceeds 8 s and the retry exhausts.
This is **secondary** to the heartbeat issue and is a system-capacity
finding, not a harness bug.

The "10 s plateau" in HTTP p95 corresponds to
`AttemptTimeoutSeconds: 10` in
`src/Kombats.Bff/Kombats.Bff.Bootstrap/appsettings.json:21` — Polly's
per-attempt timeout for BFF→downstream service calls. When the downstream
hangs that long Polly cancels the attempt. Also secondary.

---

## Section 6 — Proposed fix

### 6.1 — Primary: send queue heartbeat

Add a `POST /api/v1/queue/heartbeat` ping every **10 s** for the duration
of the time the bot is waiting in the queue. Stop pinging as soon as
`PollUntilMatchedAsync` returns a `battleId`.

Concrete changes:

- **`Transport/BffHttpClient.cs`**: add a new method
  ```csharp
  public Task HeartbeatAsync(string connectionRef, CancellationToken ct);
  ```
  Body: `POST /api/v1/queue/heartbeat` with `{connectionRef}`. Swallow
  non-2xx + log debug (matching the frontend's
  `queueApi.heartbeat(connectionRef).catch(() => {})` at
  `hooks.ts:272-275`).
- **`VirtualPlayer/VirtualPlayer.cs`**: between the `JoinQueueAsync` call
  (line 85) and `PollUntilMatchedAsync` (line 95), start a fire-and-forget
  heartbeat loop. Cancel via a CTS the moment we know we've been Matched
  or the queue-wait CTS expires. Implementation sketch:
  ```csharp
  using var hbCts = CancellationTokenSource.CreateLinkedTokenSource(queueLinked.Token);
  var heartbeatTask = Task.Run(async () =>
  {
      while (!hbCts.Token.IsCancellationRequested)
      {
          try { await _bff.HeartbeatAsync(connectionRef, hbCts.Token); }
          catch { /* best-effort */ }
          try { await Task.Delay(TimeSpan.FromSeconds(10), hbCts.Token); }
          catch (OperationCanceledException) { break; }
      }
  }, hbCts.Token);
  try { battleId = await PollUntilMatchedAsync(_bff, queueLinked.Token); }
  finally { hbCts.Cancel(); await heartbeatTask.ConfigureAwait(false); }
  ```
  10 s interval is below the server's 15 s ref TTL with comfortable
  margin and matches the frontend exactly.

**Idempotency**: heartbeat is safe to fire repeatedly — it just refreshes
the Redis ref TTL (`RedisQueuePresenceStore.RegisterAsync`). No state
machine.

**Failure mode if heartbeat fails**: swallow the error and keep looping.
A single missed heartbeat at t=N is recoverable as long as the next
succeeds before t=N+15. If all heartbeats fail, the bot will be swept
and the existing `QueueTimeout` outcome captures it correctly — no new
abort path needed.

### 6.2 — Secondary: send avatar (low priority)

Add `POST /api/v1/character/avatar` between the SetName and AllocateStats
steps. Avatar id `default` or one of the SELECTABLE_AVATARS set
(`src/Kombats.Client/src/modules/player/avatar-assets.ts`).
Pass `expectedRevision = revision + 1` then bump my own AllocateStats
expected to `revision + 2`. **Cosmetic** — matchmaking does not consume
avatar id, the DB default keeps it populated, and no observability
metric depends on it.

I would skip this in the initial fix and only revisit if the user wants
the bot's traffic to match the SPA byte-for-byte for a portfolio screenshot.

### 6.3 — Tertiary: delete the dead-code loop in `EnsureReadyAsync`

Lines 217-236 of `VirtualPlayer.cs` are a 10-iteration "wait for
projection" loop whose first iteration `return`s unconditionally
(`VirtualPlayer.cs:230`). Replace with a comment explaining that
`BffHttpClient.JoinQueueAsync` already retries on the relevant 400
codes, or just delete.

### 6.4 — Order of operations after fix

```
auth → onboard → setName (if Draft) → allocateStats (if !Ready)
     → connect SignalR → joinQueue
     → start heartbeat loop (10 s interval)
     → pollUntilMatched
     → stop heartbeat
     → joinBattle (with 8-step retry, unchanged)
     → turn loop → battleEnded → cleanup
```

The heartbeat loop is the only addition. Everything else stays.

### 6.5 — Bot-aborts-on-partial-failure

A `HeartbeatAsync` failure should NOT abort the bot. The presence sweep
takes 20 s + 15 s TTL = up to ~35 s to actually evict the bot, so a few
consecutive heartbeat failures are recoverable. The bot already aborts
correctly on `QueueTimeout` (60 s wait without a match) — which is what
will happen if heartbeats genuinely can't keep the bot alive.

`OnboardAsync` / `SetNameAsync` / `AllocateStatsAsync` failures already
propagate out of `EnsureReadyAsync` and turn into `BattleOutcome.Error`
results in `RunOneBattleAsync`'s catch — no change needed there.
