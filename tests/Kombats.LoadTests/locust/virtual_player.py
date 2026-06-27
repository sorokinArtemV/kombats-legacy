"""
Virtual player orchestrator. Port of VirtualPlayer/VirtualPlayer.cs.

One bot:
  auth -> onboarding -> SignalR connect -> join queue -> wait for matched
       -> JoinBattle -> action loop -> BattleEnded -> disconnect

Critical port items (Chapter 4 plan §B1, B0 findings):
  - TurnOpened-before-snapshot race: seed turn-ready FROM THE SNAPSHOT for
    the opening turn — the broadcast on the battle:{battleId} group fires
    before our JoinBattle invoke completes (B0 finding #3).
  - 7-step queue retry + 8-step JoinBattle retry are inside the clients.
  - Heartbeat thread is killed in finally to prevent leaks.
  - Snapshot field names are camelCase from the wire — we apply ONE boundary
    normalize step (lower-case key map) per B0 finding #4 instead of
    sprinkling .lower() throughout the orchestrator.
"""

from __future__ import annotations

import logging
import random
import threading
import time
import uuid
from dataclasses import dataclass
from typing import Optional

from bff_client import BffClient, BffError
from behavior import pick_action_payload
from hub_client import BattleHubClient, HubError, HubTimeout
from token_client import TokenClient

# Outcomes mirror BattleOutcome enum (VirtualPlayerResult.cs).
OUTCOME_WON = "Won"
OUTCOME_LOST = "Lost"
OUTCOME_DRAW = "Draw"
OUTCOME_ERROR = "Error"
OUTCOME_QUEUE_TIMEOUT = "QueueTimeout"
OUTCOME_BATTLE_TIMEOUT = "BattleTimeout"

# Same as VirtualPlayerOptions defaults from the C# harness.
PER_BOT_TIMEOUT_SEC = 90
QUEUE_TIMEOUT_SEC = 60
HEARTBEAT_INTERVAL_SEC = 10


def _normalize(d):
    """Single boundary helper: camelCase -> lower-case key dict (B0 finding #4)."""
    if not isinstance(d, dict):
        return d
    return {k.lower(): v for k, v in d.items()}


@dataclass
class BattleResult:
    username: str
    sub: str
    battle_id: Optional[str]
    outcome: str
    error: Optional[str]
    turns_played: int
    auth_ms: float
    onboard_ms: float
    connect_ms: float
    queue_wait_ms: float
    join_battle_ms: float
    battle_ms: float
    total_ms: float


class VirtualPlayer:
    def __init__(
        self,
        username: str,
        password: str,
        sub: str,
        token_client: TokenClient,
        logger: Optional[logging.Logger] = None,
        random_seed: Optional[int] = None,
    ) -> None:
        self._username = username
        self._password = password
        self._sub = sub
        self._tokens = token_client
        self._logger = logger or logging.getLogger(f"vp[{username}]")
        self._rng = random.Random(random_seed)

        self._bff: Optional[BffClient] = None
        self._hub: Optional[BattleHubClient] = None

        # Battle state (mirrors C# VirtualPlayer fields).
        self._my_id = sub  # token sub == player identity id (Guid string)
        self._current_turn_index = 0
        self._turn_ready = threading.Event()
        self._battle_done = threading.Event()
        self._end_event: Optional[dict] = None  # normalized BattleEnded payload

    def run_one_battle(self) -> BattleResult:
        total_start = time.monotonic()
        step_start = time.monotonic()
        auth_ms = onboard_ms = connect_ms = queue_wait_ms = 0.0
        join_battle_ms = battle_ms = 0.0
        battle_id: Optional[str] = None
        connection_ref = f"loadtest-{self._username}-{uuid.uuid4().hex}"
        per_bot_deadline = total_start + PER_BOT_TIMEOUT_SEC

        try:
            # ---- 1. Token (warm cache; subsequent calls hit it) -----------------
            step_start = time.monotonic()
            token = self._tokens.get_access_token(self._username, self._password)
            auth_ms = (time.monotonic() - step_start) * 1000

            # The BFF + hub each pull a token via the cache on every send.
            token_factory = lambda: self._tokens.get_access_token(
                self._username, self._password
            )
            self._bff = BffClient(token_factory, logger=self._logger)

            # ---- 2. Onboarding (idempotent) -------------------------------------
            step_start = time.monotonic()
            self._ensure_ready()
            onboard_ms = (time.monotonic() - step_start) * 1000

            # ---- 3. Connect SignalR --------------------------------------------
            step_start = time.monotonic()
            self._hub = BattleHubClient(token_factory, logger=self._logger)
            self._wire_hub_events(self._hub)
            self._hub.connect(timeout_sec=10.0)
            connect_ms = (time.monotonic() - step_start) * 1000

            # ---- 4. Join queue --------------------------------------------------
            step_start = time.monotonic()
            join_result = _normalize(self._bff.join_queue(connection_ref))
            if (
                join_result.get("status", "").lower() == "matched"
                and join_result.get("battleid")
            ):
                battle_id = join_result["battleid"]
            else:
                # ---- 5. Poll until matched, with parallel heartbeat ------------
                queue_deadline = min(
                    time.monotonic() + QUEUE_TIMEOUT_SEC, per_bot_deadline
                )
                stop_hb = threading.Event()
                hb_thread = threading.Thread(
                    target=self._run_heartbeat,
                    args=(connection_ref, stop_hb, queue_deadline),
                    daemon=True,
                )
                hb_thread.start()
                try:
                    battle_id = self._poll_until_matched(queue_deadline)
                finally:
                    stop_hb.set()
                    hb_thread.join(timeout=2.0)
            queue_wait_ms = (time.monotonic() - step_start) * 1000

            if battle_id is None:
                return self._make_result(
                    OUTCOME_QUEUE_TIMEOUT,
                    "Queue timed out before pairing.",
                    None,
                    auth_ms, onboard_ms, connect_ms, queue_wait_ms,
                    join_battle_ms, battle_ms,
                    turns_played=0,
                    total_start=total_start,
                )

            # ---- 6. JoinBattle (8-step retry loop in HubClient) ----------------
            step_start = time.monotonic()
            snapshot = _normalize(
                self._hub.join_battle(battle_id, timeout_sec=10.0)
            )
            join_battle_ms = (time.monotonic() - step_start) * 1000
            if not isinstance(snapshot, dict):
                raise RuntimeError(
                    f"snapshot is not a dict: {type(snapshot).__name__}"
                )

            # ---- 7. Turn loop until BattleEnded --------------------------------
            step_start = time.monotonic()

            # B0 finding #3: turn 0 from snapshot, NOT from event. The first
            # TurnOpened may have been broadcast to the battle group before
            # JoinBattle put us into it.
            self._current_turn_index = int(snapshot.get("turnindex", 0))
            self._battle_done.clear()
            self._end_event = None
            self._turn_ready.set()  # turn 0 ready immediately

            turns_played = 0
            battle_loop_deadline = per_bot_deadline
            while True:
                # Wait for either the next turn to open or BattleEnded.
                turn_ready = self._turn_ready.wait(
                    timeout=max(0.05, battle_loop_deadline - time.monotonic())
                )
                if self._battle_done.is_set():
                    break
                if not turn_ready:
                    # Per-bot timeout — re-raised to the outer except clause.
                    raise TimeoutError(
                        "battle loop timed out waiting for next TurnOpened"
                    )

                self._turn_ready.clear()
                turn_index = self._current_turn_index
                payload = pick_action_payload(self._rng)
                try:
                    self._hub.submit_turn_action(battle_id, turn_index, payload)
                    turns_played += 1
                except (HubError, HubTimeout):
                    # Battle-ending turn race: BattleEnded handler fires
                    # before the submit completion frame would have.
                    if self._battle_done.is_set():
                        break
                    raise

            battle_ms = (time.monotonic() - step_start) * 1000
            outcome = self._resolve_outcome(self._end_event)
            return self._make_result(
                outcome, None, battle_id,
                auth_ms, onboard_ms, connect_ms, queue_wait_ms,
                join_battle_ms, battle_ms,
                turns_played=turns_played,
                total_start=total_start,
            )

        except TimeoutError as ex:
            return self._make_result(
                OUTCOME_BATTLE_TIMEOUT, str(ex), battle_id,
                auth_ms, onboard_ms, connect_ms, queue_wait_ms,
                join_battle_ms, battle_ms,
                turns_played=0,
                total_start=total_start,
            )
        except Exception as ex:
            self._logger.warning(
                "VirtualPlayer %s failed: %s: %s",
                self._username, type(ex).__name__, ex,
            )
            return self._make_result(
                OUTCOME_ERROR, f"{type(ex).__name__}: {ex}", battle_id,
                auth_ms, onboard_ms, connect_ms, queue_wait_ms,
                join_battle_ms, battle_ms,
                turns_played=0,
                total_start=total_start,
            )
        finally:
            # Best-effort leave queue.
            if self._bff is not None:
                try:
                    self._bff.leave_queue(connection_ref)
                except Exception:
                    pass
                try:
                    self._bff.close()
                except Exception:
                    pass
            if self._hub is not None:
                try:
                    self._hub.dispose()
                except Exception:
                    pass

    # ---- onboarding (port of EnsureReadyAsync) -----------------------------------

    def _ensure_ready(self) -> None:
        # 1. onboard (idempotent)
        onboard = self._bff.onboard()
        state = onboard.get("onboardingState")
        revision = onboard.get("revision", 1)

        # 2. set name + avatar if Draft
        if state and state.lower() == "draft":
            self._bff.set_name(self._username)
            # "ronin" is non-default (default = "shadow_oni") so revision bumps.
            self._bff.change_avatar(
                expected_revision=revision + 1,
                avatar_id="ronin",
            )

        # 3. allocate stats if not Ready
        if not state or state.lower() != "ready":
            expected_rev = (
                revision if state and state.lower() == "named"
                else revision + 2
            )
            try:
                self._bff.allocate_stats(
                    strength=1, agility=1, intuition=1, vitality=0,
                    expected_revision=expected_rev,
                )
            except BffError:
                # Revision drift: refetch and retry once with the live revision.
                fresh = self._bff.get_game_state()
                character = fresh.get("character") or {}
                if (character.get("onboardingState") or "").lower() != "ready":
                    self._bff.allocate_stats(
                        strength=1, agility=1, intuition=1, vitality=0,
                        expected_revision=character.get("revision"),
                    )

        # 4. matchmaking projection lands asynchronously; queue/join retries
        # cover the race — see BffClient.join_queue.

    # ---- queue polling -----------------------------------------------------------

    def _run_heartbeat(
        self,
        connection_ref: str,
        stop_event: threading.Event,
        deadline: float,
    ) -> None:
        """10s cadence matches the SPA. The 15s presence-ref TTL is comfortably
        refreshed before the QueuePresenceSweepWorker (every 20s) can evict."""
        while not stop_event.wait(timeout=HEARTBEAT_INTERVAL_SEC):
            if time.monotonic() > deadline:
                return
            try:
                self._bff.heartbeat(connection_ref)
            except Exception as ex:
                self._logger.warning("heartbeat failed: %s", ex)

    def _poll_until_matched(self, deadline: float) -> Optional[str]:
        while time.monotonic() < deadline:
            status = _normalize(self._bff.get_queue_status())
            if (
                status.get("status", "").lower() == "matched"
                and status.get("battleid")
            ):
                return status["battleid"]
            time.sleep(0.5)
        return None

    # ---- hub event wiring --------------------------------------------------------

    def _wire_hub_events(self, hub: BattleHubClient) -> None:
        # Only the events VirtualPlayer.cs cares about for the loop:
        # TurnOpened (advances the loop) and BattleEnded (terminates it).
        # The others are silently consumed in C# by HubEventTracker; we don't
        # need an equivalent for B1a.
        hub.on("TurnOpened", self._on_turn_opened)
        hub.on("BattleEnded", self._on_battle_ended)

    def _on_turn_opened(self, payload) -> None:
        p = _normalize(payload)
        try:
            self._current_turn_index = int(p.get("turnindex", 0))
        except (TypeError, ValueError):
            return
        self._turn_ready.set()

    def _on_battle_ended(self, payload) -> None:
        self._end_event = _normalize(payload)
        self._battle_done.set()
        # Wake the loop in case it's blocked on _turn_ready.
        self._turn_ready.set()

    # ---- result --------------------------------------------------------------------

    def _resolve_outcome(self, end_event) -> str:
        if not end_event:
            return OUTCOME_ERROR
        winner = end_event.get("winnerplayerid")
        if winner is None:
            return OUTCOME_DRAW
        return OUTCOME_WON if str(winner).lower() == self._my_id.lower() else OUTCOME_LOST

    def _make_result(
        self,
        outcome: str,
        error: Optional[str],
        battle_id: Optional[str],
        auth_ms: float,
        onboard_ms: float,
        connect_ms: float,
        queue_wait_ms: float,
        join_battle_ms: float,
        battle_ms: float,
        turns_played: int,
        total_start: float,
    ) -> BattleResult:
        total_ms = (time.monotonic() - total_start) * 1000
        return BattleResult(
            username=self._username,
            sub=self._sub,
            battle_id=battle_id,
            outcome=outcome,
            error=error,
            turns_played=turns_played,
            auth_ms=auth_ms,
            onboard_ms=onboard_ms,
            connect_ms=connect_ms,
            queue_wait_ms=queue_wait_ms,
            join_battle_ms=join_battle_ms,
            battle_ms=battle_ms,
            total_ms=total_ms,
        )
