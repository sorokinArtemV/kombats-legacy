# Phase B0 — signalrcore Spike Results

## Verdict

**✅ GREEN** — Phase B1 (full Locust migration) is technically valid. `signalrcore` drives the Kombats SignalR surface end-to-end with one ~15-line helper and no protocol hacks.

## Environment

- Python: 3.14.5 (project will pin to 3.11+ for B1)
- `signalrcore==1.0.2`, `httpx==0.28.1`, `PyJWT==2.12.1`, `locust==2.44.0`
- Stack: Mode A (`docker-compose.yml` + observability + override), fresh `down -v`, 14 containers Up + `kombats-keycloak-bootstrap` Exited 0, Redis baseline = 0 keys, observability WARN audit = 0 for all 5 services.
- C# baseline (`dotnet run -- single-bot`): ✓ passed (~13s). `dotnet run -- smoke`: ✓ passed — full bot pair battle, 7 turns, Won/Lost in 948 ms wall clock.
- Spike run wall clock: ~1.0 s (cached identities, mid-battle resume); both bots passed all 7 steps on two consecutive runs.

## What worked

- **Step 1** — Keycloak ROPC (`/realms/kombats/protocol/openid-connect/token`, `client_id=kombats-loadtest`, `client_secret=loadtest-secret-do-not-use-in-prod`, password=`loadtest`). ~80 ms cold. JWT `sub` extracted with `pyjwt`, no signature verify.
- **Step 2** — BFF onboarding (`POST /api/v1/game/onboard` → check `onboardingState` → name/avatar/stats as needed). Idempotent; ~10 ms when already Ready.
- **Step 3** — `HubConnectionBuilder().with_url(..., options={"access_token_factory": lambda: token})` against `http://localhost:5000/battlehub`. Connect ~15 ms.
- **Step 4** — Queue join with `BffHttpClient.cs:86-125` 7-step backoff on `Queue.NotReady`; parallel heartbeat at 10 s (matches `VirtualPlayer.cs:249-281`). Both bots matched the same `battleId` within ~10 ms of join (cached match).
- **Step 5** — `JoinBattle` invoke-with-result returned a full `BattleSnapshotRealtime` dict (`battleId, playerAId, playerBId, ruleset, phase, turnIndex, deadlineUtc, noActionStreakBoth, lastResolvedTurnIndex, endedReason, version, playerAHp, playerBHp, playerAName, playerBName, playerAMaxHp, playerBMaxHp`) in ~5 ms. **This is the gating proof.** Server sends camelCase property names — the dict layer works without any custom converter.
- **Step 6** — TurnOpened: handled via snapshot (see "race" below). Live event arrives reliably for turn N+1 once both players submit on turn N.
- **Step 7** — `hub.send("SubmitTurnAction", [battle_id, turn_index, payload_json])` is fire-and-forget. Once both bots submitted, `TurnResolved` arrived in <100 ms, followed by `TurnOpened` for the next turn and `BattleStateUpdated` — all event subscriptions wired cleanly via `hub.on(name, callback)`.

## What didn't / hacks required

- **Turn-1 `TurnOpened` is broadcast before the SignalR group is joined** — exactly the race documented at `VirtualPlayer.cs:130-134`. `BattleReady` + initial `TurnOpened` fire from `CreateBattleConsumer` to the `battle:{battleId}` group at battle-creation time, but neither bot is in the group until its `JoinBattle` invoke lands. Verified: my first attempt gated on `TurnOpened` after `JoinBattle` and timed out. Fix is the same as C#: seed turn state from the snapshot. **The spike now treats the snapshot's `turnIndex` as authoritative for the opening turn and only awaits live `TurnOpened` for subsequent turns.** Not a hack — this is the intended contract.
- **`signalrcore` error frames bypass `on_invocation`** — the only real protocol gotcha. `BaseHubConnection.__on_completion_message` (signalrcore 1.0.2) routes `CompletionMessage.error` to a *global* `self._callbacks.on_error(message)`, not to the per-invocation handler registered via `on_invocation`. Consequence: a hub-side failure on `JoinBattle` (e.g. transient "battle not found") would not surface through the helper — it would just time out at `done.wait()`. The spike does not hit this because `JoinBattle` succeeded, but **B1's `HubClient.invoke_with_result` must register a global `hub.on_error` that keys on `CompletionMessage.invocation_id` and forwards the error into the same result holder.** That's a ~10-line addition over the spike helper.
- **`stream_handlers` is used for completion routing** — minor architectural quirk in signalrcore 1.0.2. The library reuses `self.stream_handlers[invocation_id]` (a `defaultdict(list)`) to attach the `on_invocation` callback and then deletes the entry on completion. Works, but the naming is misleading — these aren't stream handlers, they're invocation completion handlers. No action needed; flagging for B1 maintainability.

## `invoke_with_result` implementation

This is the foundation for B1's `HubClient.JoinBattleAsync` and any future `invoke<T>`-shaped call:

```python
def invoke_with_result(hub, method, args, timeout_sec=10.0):
    done = threading.Event()
    holder = {"result": None}

    def on_completion(msg):
        # signalrcore sometimes passes [CompletionMessage]; sometimes the
        # CompletionMessage directly. Normalize.
        if isinstance(msg, list) and msg:
            msg = msg[0]
        holder["result"] = getattr(msg, "result", None)
        done.set()

    hub.send(method, args, on_invocation=on_completion)
    if not done.wait(timeout=timeout_sec):
        raise TimeoutError(f"{method} did not complete within {timeout_sec}s")
    return holder["result"]
```

B1 must additionally wire `hub.on_error` to dispatch errors into the same `holder` keyed by `invocation_id`. With that, this helper is a drop-in replacement for the C# `await connection.InvokeAsync<T>(...)`. Plus the 8-step retry loop on transient "battle not found" from `BattleHubClient.cs:79-99`.

## Risks for Phase B1

- **Threading model.** Spike uses `threading.Thread` for parallel bots. Locust expects gevent greenlets — `signalrcore` uses `websocket-client` (1.9.0) which is socket-blocking. Verify it cooperates with gevent's monkey-patch; if not, fall back to a process-level pool or use the dedicated locust worker process. Flag for Phase B1 H6 in the risk register.
- **Error-frame routing.** Already covered above. Without the global `on_error` wiring, transient `JoinBattle` failures become timeouts and degrade the retry loop.
- **Heartbeat-while-polling concurrency.** Spike runs heartbeat inline in the poll loop (single-thread, time-checked). B1 in Locust will need a per-bot greenlet that's explicitly killed in `finally` — this is item 4 of the "Critical port items" in the chapter plan.
- **Reconnect behavior.** Spike enables `with_automatic_reconnect` but never tests it. Chapter 4 capacity scan will exercise reconnects at high concurrency; check that signalrcore's auto-reconnect re-acquires the token via `access_token_factory` rather than the cached one (it should — `access_token_factory` is called per WebSocket open).
- **JSON property casing.** Server sends camelCase (`turnIndex`, `playerAHp`). Spike normalizes via `{k.lower(): v}`. B1 should adopt a typed `TypedDict` per realtime contract or a single helper that maps camelCase → snake_case rather than scattering `.lower()` calls.

## How to reproduce

```bash
# Stack already up per RUNBOOK Mode A; users seeded (loadbot-0001/0002 in
# tests/Kombats.LoadTests/users-manifest.json with password=loadtest).
cd tests/Kombats.LoadTests/locust
.venv/bin/python spike_signalr.py
```

Expected output: 7 step lines per bot, two `EVENT` lines (`TurnResolved`, next `TurnOpened`), `FINAL VERDICT: ✅ GREEN`. Wall clock 0.8–1.5 s on a warm stack. Exit code 0.

If you see `❌ RED — TurnOpened did not fire within 5s of JoinBattle`: this means the spike was run with the old single-bot-driver code. The current version handles the snapshot race correctly — confirm `spike_signalr.py` matches what's checked in.
