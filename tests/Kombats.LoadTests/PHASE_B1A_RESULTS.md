# Phase B1a — Python Bot Port Results

## Acceptance

**PASS.** Two parallel `python run_one_battle.py loadbot-XXXX` invocations
match into the same battle, play 6 turns each, exit 0, and emit two
JSONL rows with all 13 fields populated. `aggregate-phases.sh` runs
unchanged on the output.

## Environment

- Python 3.14.5 (existing `.venv` from Phase B0; will pin to 3.11+ in B1b)
- `signalrcore==1.0.2`, `httpx==0.28.1`, `PyJWT==2.12.1`
- Stack: Mode A from RUNBOOK (14 long-running + bootstrap Exited 0).
  Pre-existing volumes; `users-manifest.json` already had 50 entries
  (`existed=50` on re-seed).
- Wall clock for the port + acceptance: ~2.0 h (well inside 4–6h budget).

## What worked

- **Drop-in port of B0 spike helpers.** `invoke_with_result` lifted
  almost verbatim from `spike_signalr.py`; only addition was the
  pending-invocations registry + global error routing (B0 finding #2).
- **Snapshot-as-source-of-truth for turn 0.** `virtual_player.py:170-176`
  seeds `_turn_ready` from `snapshot["turnindex"]` and only awaits live
  `TurnOpened` for turns 1+. Confirmed in two acceptance runs — no
  race-induced wait, both bots submit turn 0 immediately on join.
- **Single normalize() boundary helper.** All camelCase → lower-case
  key flattening lives in `virtual_player._normalize` and is applied at
  exactly three boundary points (snapshot, queue status, hub event
  payloads). No `.lower()` scattered through the orchestrator.
- **Verbatim retry tables.** `BffClient.join_queue` and
  `BattleHubClient.join_battle` use the same `(250, 500, 750, 1000,
  1500, 2000, 2000)` ms table as the C# originals.
- **Thread-safe token cache.** `TokenClient` wraps the cache dict in a
  `threading.Lock` and performs the network fetch outside the lock so
  concurrent first-use for different bots does not serialize. Resolves
  Chapter 4 risk H6.
- **JSONL schema is byte-equivalent for `aggregate-phases.sh`.** Same
  field names, same order, same omit-on-null for `battle_id` / `error`.

## What was tricky / decisions made

- **B0 finding #2 — error routing.** signalrcore 1.0.2's
  `BaseHubConnection.__on_completion_message` (line 346) routes
  `CompletionMessage.error` to the global `_callbacks.on_error`, NOT to
  the per-invocation `on_invocation` callback. **Decision:** generate
  our own UUID `invocation_id` per call (signalrcore exposes the
  `invocation_id` parameter on `connection.send`), keep a
  `dict[invocation_id -> (Event, container)]` registry, and have
  `_on_global_error` do an exact lookup via `message.invocation_id`.
  No fallback path needed — the CompletionMessage object delivered to
  on_error has both `.invocation_id` and `.error` attributes (verified
  in `signalrcore/messages/completion_message.py`), so routing is
  deterministic. The "broadcast-to-all-pending" fallback mentioned in
  the brief was not needed.
- **B0 finding #3 — turn 0 from snapshot.** Implemented as instructed.
  `_turn_ready.set()` is called immediately after parsing the snapshot;
  the `TurnOpened` handler resets via `_turn_ready.set()` on every live
  event for turn N+1, with `clear()` happening just before sending the
  action so we wait for the next opener. `BattleEnded` also calls
  `_turn_ready.set()` so the loop wakes and exits via the `_battle_done`
  check, even if the end-event arrives during a wait.
- **B0 finding #4 — single normalize.** `_normalize()` is one helper at
  the top of `virtual_player.py`. The choice was simpler than a
  `TypedDict` — fewer files, no schema duplication, and the dict is
  treated as opaque state inside the orchestrator (we only read 4 keys:
  `turnindex`, `winnerplayerid`, `status`, `battleid`).
- **Heartbeat threading.** Used `threading.Thread(daemon=True)` plus a
  `threading.Event` stop-signal in `finally`. Joined with timeout 2s.
  `HEARTBEAT_INTERVAL_SEC=10`, deadline-aware so it exits at queue
  timeout even if `stop_event.wait` happens to align with the interval.
  Locust gevent migration is B1b's problem.
- **Outcome resolution.** Compared `winnerplayerid` (lowercased) to
  `self._sub` (lowercased). The C# version compares Guids; Python's
  string compare on a normalized form is equivalent and avoids parsing.

## Test run output

```
[13:30:52] loadbot-0001   main       loaded user: username=loadbot-0001 sub=c07354d3-...
[13:30:52] loadbot-0001   main       step 0: starting bot lifecycle
[13:30:52] loadbot-0001   httpx      POST /token        200
[13:30:52] loadbot-0001   httpx      POST /onboard      200
[13:30:52] loadbot-0001   httpx      POST /queue/join   200
[13:30:52] loadbot-0001   httpx      GET  /queue/status 200  (x6 polls — waiting for bot-2)
[13:30:54] loadbot-0001   httpx      POST /queue/leave  200
[13:30:54] loadbot-0001   main       step done: outcome=Won turns=6 battle=bc3ab01b-... elapsed=2.75s
[13:30:54] loadbot-0001   main       ✅ acceptance outcome: Won

[13:30:54] loadbot-0002   main       loaded user: username=loadbot-0002 sub=412fd549-...
[13:30:54] loadbot-0002   httpx      POST /token        200
[13:30:54] loadbot-0002   httpx      POST /onboard      200
[13:30:54] loadbot-0002   httpx      POST /queue/join   200  (matched immediately on join)
[13:30:54] loadbot-0002   httpx      GET  /queue/status 200  (x2)
[13:30:54] loadbot-0002   httpx      POST /queue/leave  200
[13:30:54] loadbot-0002   main       step done: outcome=Lost turns=6 battle=bc3ab01b-... elapsed=0.79s
[13:30:54] loadbot-0002   main       ✅ acceptance outcome: Lost
```

Repro #2 (loadbot-0003 + loadbot-0004, 5s apart): same pattern, Won/Lost,
6 turns, exit 0, same battle_id `094cc8b0-3591-467c-985d-d53c1e8d960a`.

## JSONL sample

```json
{"ts":"2026-05-16T13:30:54.869268+05:00","username":"loadbot-0001","battle_id":"bc3ab01b-0150-4132-8200-a317a7f083ab","outcome":"Won","turns_played":6,"auth_ms":45,"onboard_ms":8,"connect_ms":11,"queue_wait_ms":2564,"join_battle_ms":8,"battle_ms":66,"total_ms":2705}
```

`aggregate-phases.sh` on the combined two-row file produces the
expected 7-phase × 4-stat table for Overall, per-Outcome (Won/Lost),
and Successful/QueueTimeout slices — schema verified working.

## Diff from C# reference

Intentional differences vs `VirtualPlayer.cs`:

- **No `HubEventTracker` equivalent.** The C# version subscribes to all
  8 hub events for telemetry. B1a only subscribes to `TurnOpened` and
  `BattleEnded` — those are the only two the loop logic depends on.
  B1b will add the others when Locust needs them for metrics.
- **`*_ms` fields are integers.** C# emits `double` (e.g. `48.9848`),
  Python emits `int` (rounded). Per task brief; aggregate-phases.sh
  uses `tonumber` so both work, and integer values are easier to scan
  by eye in raw JSONL.
- **Outcome compare is string-based.** C# parses `_options.User.Sub`
  to a Guid and compares `Guid == Guid`. Python lowercases both sides
  and compares strings. Equivalent on Keycloak-generated UUIDs (always
  lowercase 8-4-4-4-12 hex), simpler than parsing.
- **No ChangeAvatar 4xx-other-than-409 distinction.** C# treats only
  409 as no-op; B1a tolerates 409 only and raises on everything else.
  Same shape, just expressed via early-return rather than nested
  conditional.

## Readiness for B1b (Locust integration)

In place:
- All four boundary clients (`token_client`, `bff_client`, `hub_client`,
  `iteration_recorder`) are independently importable and have no
  module-level state besides the `TokenClient` cache lock.
- `VirtualPlayer.run_one_battle()` returns a flat dataclass — easy to
  hand to a Locust `events.request.fire()` once per phase.
- `IterationRecorder` is thread-safe (lock around the `_fp.write` +
  flush) and tolerates concurrent `record()` from many greenlets.

What B1b will add:
- `locustfile.py` — `User` subclass with one `@task` that calls
  `VirtualPlayer.run_one_battle()` in a loop, plus `events` wiring to
  surface per-phase ms as Locust requests.
- gevent compatibility audit. `threading.Thread`, `threading.Event`,
  `threading.Lock` are all monkey-patchable; `signalrcore`'s underlying
  `websocket-client` may need `gevent.monkey.patch_all()` early.
- Argument parsing for ramp stages (concurrency, duration), JSONL
  output path injection.

## How to reproduce

```bash
# Stack must be Mode A per RUNBOOK; users-manifest.json must have ≥4 entries.
cd tests/Kombats.LoadTests/locust
.venv/bin/python run_one_battle.py loadbot-0001 &
.venv/bin/python run_one_battle.py loadbot-0002 &
wait
# Inspect: tests/Kombats.LoadTests/iteration-logs/iterations-*.jsonl  (jq .)
# Aggregate: ../scripts/aggregate-phases.sh /tmp/combined.jsonl
```

Wall clock 1–6s on a warm stack; the late-joining bot waits ~0.5s, the
first one waits 2–6s for its partner. Both exit 0.
