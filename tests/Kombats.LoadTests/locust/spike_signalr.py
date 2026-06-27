"""
Phase B0 spike — prove signalrcore can drive a Kombats battle end-to-end.

Steps (per Chapter 4 plan §2):
  1. Keycloak ROPC token.
  2. Onboarding via BFF REST (idempotent).
  3. SignalR connect to BFF /battlehub.
  4. Join queue + 500ms poll + 10s heartbeat.
  5. JoinBattle invoke-with-result (THE gating test).
  6. TurnOpened observed (snapshot counts — turn-1 race per VirtualPlayer.cs:130-134).
  7. SubmitTurnAction + TurnResolved on the same turn.

Both bots run the full flow in parallel threads so the server can resolve
turn 1 from real player input rather than the 30s TurnDeadlineWorker fallback.

Single-file spike, hardcoded for localhost. Not production code.
"""

import json
import random
import threading
import time
import uuid

import httpx
import jwt
from signalrcore.hub_connection_builder import HubConnectionBuilder

KEYCLOAK = "http://localhost:8080"
BFF = "http://localhost:5000"
REALM = "kombats"
CLIENT_ID = "kombats-loadtest"
CLIENT_SECRET = "loadtest-secret-do-not-use-in-prod"
HUB_PATH = "/battlehub"

ZONES = ["Head", "Chest", "Belly", "Waist", "Legs"]
BLOCK_PAIRS = [("Head", "Chest"), ("Chest", "Belly"), ("Belly", "Waist"),
               ("Waist", "Legs"), ("Legs", "Head")]


def log(tag, msg):
    print(f"[{time.strftime('%H:%M:%S')}] {tag:14s} {msg}", flush=True)


def get_token(username, password):
    r = httpx.post(
        f"{KEYCLOAK}/realms/{REALM}/protocol/openid-connect/token",
        data={"grant_type": "password", "client_id": CLIENT_ID,
              "client_secret": CLIENT_SECRET, "username": username,
              "password": password, "scope": "openid"},
        timeout=10.0,
    )
    r.raise_for_status()
    token = r.json()["access_token"]
    claims = jwt.decode(token, options={"verify_signature": False})
    return token, claims["sub"]


def ensure_ready(token):
    """onboard → set-name → avatar → allocate-stats; mirrors EnsureReadyAsync."""
    h = {"Authorization": f"Bearer {token}"}
    with httpx.Client(base_url=BFF, headers=h, timeout=10.0) as c:
        ob = c.post("/api/v1/game/onboard")
        ob.raise_for_status()
        body = ob.json()
        state = body.get("onboardingState")
        revision = body.get("revision", 1)

        if state == "Draft":
            r = c.post("/api/v1/character/name", json={"name": "spike-bot"})
            if r.status_code not in (200, 204, 409):
                r.raise_for_status()
            r = c.post("/api/v1/character/avatar",
                       json={"expectedRevision": revision + 1, "avatarId": "ronin"})
            if r.status_code not in (200, 204, 409):
                r.raise_for_status()

        if state != "Ready":
            expected_rev = revision if state == "Named" else revision + 2
            r = c.post("/api/v1/character/stats", json={
                "strength": 1, "agility": 1, "intuition": 1, "vitality": 0,
                "expectedRevision": expected_rev})
            if r.status_code == 409:
                gs = c.get("/api/v1/game/state").json()
                ch = gs.get("character") or {}
                if ch.get("onboardingState") != "Ready":
                    r = c.post("/api/v1/character/stats", json={
                        "strength": 1, "agility": 1, "intuition": 1, "vitality": 0,
                        "expectedRevision": ch.get("revision")})
                    r.raise_for_status()
            elif r.status_code not in (200, 204):
                r.raise_for_status()


def join_queue(token, connection_ref):
    """Retries on Queue.NotReady transients per BffHttpClient.cs:86-125."""
    h = {"Authorization": f"Bearer {token}"}
    delays = [0.25, 0.5, 0.75, 1.0, 1.5, 2.0, 2.0]
    with httpx.Client(base_url=BFF, headers=h, timeout=10.0) as c:
        for attempt in range(len(delays) + 1):
            r = c.post("/api/v1/queue/join", json={"connectionRef": connection_ref})
            if r.status_code in (200, 409):
                return r.json()
            body = r.text
            transient = r.status_code == 400 and (
                "Queue.NotReady" in body or "Queue.NoCombatProfile" in body
                or "Invalid request to Matchmaking" in body)
            if not transient or attempt == len(delays):
                raise RuntimeError(f"queue/join HTTP {r.status_code}: {body}")
            time.sleep(delays[attempt])


def poll_until_matched(token, connection_ref, timeout=60.0):
    h = {"Authorization": f"Bearer {token}"}
    deadline = time.monotonic() + timeout
    last_hb = 0.0
    with httpx.Client(base_url=BFF, headers=h, timeout=10.0) as c:
        while time.monotonic() < deadline:
            now = time.monotonic()
            if now - last_hb >= 10.0:
                try:
                    c.post("/api/v1/queue/heartbeat", json={"connectionRef": connection_ref})
                except Exception as e:
                    log("WARN", f"heartbeat failed: {e}")
                last_hb = now
            s = c.get("/api/v1/queue/status").json()
            if s.get("status") == "Matched" and s.get("battleId"):
                return s["battleId"]
            time.sleep(0.5)
    return None


def build_hub(token):
    return (HubConnectionBuilder()
            .with_url(f"{BFF}{HUB_PATH}", options={
                "access_token_factory": lambda: token,
                "verify_ssl": False})
            .with_automatic_reconnect({"type": "raw", "keep_alive_interval": 10,
                                       "reconnect_interval": 5, "max_attempts": 5})
            .build())


def invoke_with_result(hub, method, args, timeout_sec=10.0):
    """The gating helper. signalrcore has no awaitable invoke<T>; we bridge
    .send(method, args, on_invocation=cb) → threading.Event → result dict.

    Caveat: signalrcore's __on_completion_message routes ERRORS to the global
    hub.on_error handler, NOT our on_invocation callback. Hub-side failures
    therefore surface as a timeout here unless we also wire on_error. For the
    spike that's acceptable; B1's HubClient will mirror error frames into the
    same result holder via a global error handler keyed on invocation_id."""
    done = threading.Event()
    holder = {"result": None}

    def on_completion(msg):
        if isinstance(msg, list) and msg:
            msg = msg[0]
        holder["result"] = getattr(msg, "result", None)
        done.set()

    hub.send(method, args, on_invocation=on_completion)
    if not done.wait(timeout=timeout_sec):
        raise TimeoutError(f"{method} did not complete within {timeout_sec}s")
    return holder["result"]


def pick_action_payload():
    primary, secondary = random.choice(BLOCK_PAIRS)
    return json.dumps({"attackZone": random.choice(ZONES),
                       "blockZonePrimary": primary,
                       "blockZoneSecondary": secondary})


def run_bot(username, password, role, result_holder):
    """Run the full 1–5+7 flow. role ∈ {'primary', 'secondary'} controls logging
    verbosity and which bot's events we use for the verdict (primary)."""
    tag = username
    try:
        # ---- Step 1
        t = time.monotonic()
        token, sub = get_token(username, password)
        log(tag, f"step 1 ✓ token sub={sub[:8]}... in {int((time.monotonic()-t)*1000)}ms")

        # ---- Step 2
        t = time.monotonic()
        ensure_ready(token)
        log(tag, f"step 2 ✓ onboarded in {int((time.monotonic()-t)*1000)}ms")

        # ---- Step 3 — SignalR connect with event handlers
        t = time.monotonic()
        hub = build_hub(token)
        events = {n: threading.Event() for n in
                  ("TurnOpened", "TurnResolved", "BattleStateUpdated",
                   "BattleEnded", "BattleConnectionLost")}
        payloads = {n: [] for n in events}

        def mk_handler(name):
            def _h(args):
                p = args[0] if isinstance(args, list) and args else args
                payloads[name].append(p)
                events[name].set()
                if role == "primary":
                    log("EVENT", f"{name}: {json.dumps(p)[:140] if isinstance(p, dict) else p}")
            return _h
        for n in events:
            hub.on(n, mk_handler(n))
        open_evt = threading.Event()
        hub.on_open(lambda: open_evt.set())
        hub.on_error(lambda e: log(f"{tag}/HUB_ERR", str(e)))
        hub.start()
        if not open_evt.wait(timeout=10.0):
            raise RuntimeError("SignalR did not connect within 10s")
        log(tag, f"step 3 ✓ SignalR connected in {int((time.monotonic()-t)*1000)}ms")

        # ---- Step 4 — queue + match
        t = time.monotonic()
        connection_ref = f"spike-{uuid.uuid4().hex}"
        jr = join_queue(token, connection_ref)
        if jr.get("status") == "Matched" and jr.get("battleId"):
            battle_id = jr["battleId"]
        else:
            battle_id = poll_until_matched(token, connection_ref)
        if not battle_id:
            raise RuntimeError("queue timed out before pairing")
        log(tag, f"step 4 ✓ matched battle={battle_id[:8]}... in {int((time.monotonic()-t)*1000)}ms")

        # ---- Step 5 — JoinBattle invoke-with-result
        t = time.monotonic()
        snapshot = invoke_with_result(hub, "JoinBattle", [battle_id], timeout_sec=10.0)
        if not isinstance(snapshot, dict):
            raise RuntimeError(f"snapshot is not a dict: {type(snapshot).__name__}")
        snap = {k.lower(): v for k, v in snapshot.items()}
        turn_index = snap.get("turnindex")
        log(tag, f"step 5 ✓ JoinBattle returned in {int((time.monotonic()-t)*1000)}ms; "
                 f"turnIndex={turn_index} hpA={snap.get('playerahp')} hpB={snap.get('playerbhp')}")

        # ---- Step 6 — TurnOpened (race-aware: snapshot turnIndex is authoritative
        # for turn 1, per VirtualPlayer.cs:130-134. Don't gate on the event for
        # turn 1; just record whether it arrived live too.)
        live = events["TurnOpened"].wait(timeout=0.5)
        log(tag, f"step 6 ✓ TurnOpened — snapshot turnIndex={turn_index} "
                 f"(live event fired={live})")

        # ---- Step 7 — SubmitTurnAction + TurnResolved
        payload_json = pick_action_payload()
        log(tag, f"step 7 send SubmitTurnAction turn={turn_index} payload={payload_json}")
        hub.send("SubmitTurnAction", [battle_id, turn_index, payload_json])
        if not events["TurnResolved"].wait(timeout=10.0):
            raise RuntimeError("TurnResolved did not fire within 10s of SubmitTurnAction")
        tr = payloads["TurnResolved"][-1]
        tr_norm = {k.lower(): v for k, v in tr.items()} if isinstance(tr, dict) else {}
        log(tag, f"step 7 ✓ TurnResolved (turn_index={tr_norm.get('turnindex')})")

        try:
            hub.stop()
        except Exception:
            pass
        result_holder[tag] = {"ok": True, "battle_id": battle_id, "turn_index": turn_index}
    except Exception as e:
        result_holder[tag] = {"ok": False, "error": f"{type(e).__name__}: {e}"}
        log(tag, f"❌ FAILED: {e}")
        import traceback
        traceback.print_exc()


def main():
    print("=" * 70)
    print("Phase B0 spike — signalrcore against Kombats")
    print("=" * 70)
    results = {}
    t1 = threading.Thread(target=run_bot, args=("loadbot-0001", "loadtest", "primary", results), daemon=True)
    t2 = threading.Thread(target=run_bot, args=("loadbot-0002", "loadtest", "secondary", results), daemon=True)
    t1.start(); t2.start()
    t1.join(timeout=120); t2.join(timeout=120)

    print()
    print("=" * 70)
    primary = results.get("loadbot-0001", {})
    secondary = results.get("loadbot-0002", {})
    if primary.get("ok") and secondary.get("ok"):
        print("FINAL VERDICT: ✅ GREEN — all 7 steps passed on both bots.")
        print(f"  primary  : battle={primary['battle_id']} turn={primary['turn_index']}")
        print(f"  secondary: battle={secondary['battle_id']} turn={secondary['turn_index']}")
        rc = 0
    elif primary.get("ok") and not secondary.get("ok"):
        print(f"FINAL VERDICT: ⚠️ YELLOW — primary passed, secondary failed.")
        print(f"  secondary error: {secondary.get('error')}")
        rc = 0
    else:
        print(f"FINAL VERDICT: ❌ RED")
        print(f"  primary  : {primary.get('error', 'no result')}")
        print(f"  secondary: {secondary.get('error', 'no result')}")
        rc = 1
    print("=" * 70)
    return rc


if __name__ == "__main__":
    raise SystemExit(main())
