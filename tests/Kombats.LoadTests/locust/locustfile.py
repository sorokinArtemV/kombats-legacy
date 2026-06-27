"""
Phase B1b — Locust integration replacing NBomber ConcurrentBattlesScenario.

Each Locust User = one bot. Users hold one username from the seeded
loadbot-* pool for their entire lifetime. The @task runs one full battle
via VirtualPlayer.run_one_battle() and records the result to a shared
JSONL whose schema is byte-identical to the C# NBomber harness — so
scripts/aggregate-phases.sh runs unchanged.

Load shape mirrors Run 0's RampingConstant(25, 30s) + KeepConstant(25, 90s)
from Scenarios/ConcurrentBattlesScenario.cs:101-106.

CRITICAL: gevent.monkey.patch_all() must be the FIRST statement in this
file before any threading / socket / signalrcore import. The B1a modules
use threading.Thread for the heartbeat loop and threading.Event for
turn-ready / battle-done signals; signalrcore uses websocket-client which
spins a receive thread; httpx uses sockets. Monkey-patching converts all
of these to gevent-cooperative primitives so the User greenlets cooperate
cleanly under one process.
"""

from gevent import monkey
monkey.patch_all()  # MUST be the first statement before any other import

import json
import logging
import os
import sys
import threading
from pathlib import Path
from typing import Optional

from locust import LoadTestShape, User, constant, events, task

# Sibling-module import (token_client, virtual_player, ...) from this file's dir.
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from iteration_recorder import (  # noqa: E402
    IterationRecorder,
    IterationResult,
    default_log_path,
)
from token_client import TokenClient  # noqa: E402
from virtual_player import OUTCOME_ERROR, VirtualPlayer  # noqa: E402

# tests/Kombats.LoadTests/locust/ -> tests/Kombats.LoadTests/
PROJECT_ROOT = Path(__file__).resolve().parent.parent
USERS_MANIFEST = PROJECT_ROOT / "users-manifest.json"
ITERATION_LOG_DIR = PROJECT_ROOT / "iteration-logs"

_log = logging.getLogger("locust-b1b")


# ---- User pool (process-wide, round-robin with in-use guard) -----------------


class UserPool:
    """Round-robin assignment of seeded loadbot-* users to Locust Users.

    Mirrors Configuration/UserPool.cs (cursor-based round robin). The
    explicit in-use set guards against double-assignment if a User were
    destroyed and a new one spawned concurrently — at 25 concurrent / 50
    pool size this never triggers, but the guard documents the invariant.
    """

    def __init__(self, users: list[dict]) -> None:
        if not users:
            raise RuntimeError("users-manifest.json is empty")
        self._users = users
        self._cursor = -1
        self._in_use: set[str] = set()
        self._lock = threading.Lock()

    def take(self) -> dict:
        with self._lock:
            for _ in range(len(self._users)):
                self._cursor = (self._cursor + 1) % len(self._users)
                u = self._users[self._cursor]
                if u["username"] not in self._in_use:
                    self._in_use.add(u["username"])
                    return u
            raise RuntimeError(
                f"user pool exhausted: all {len(self._users)} usernames are in use"
            )

    def release(self, username: str) -> None:
        with self._lock:
            self._in_use.discard(username)


# ---- Process-wide singletons (initialized in events.init) --------------------

_pool: Optional[UserPool] = None
_recorder: Optional[IterationRecorder] = None
_tokens: Optional[TokenClient] = None


@events.init.add_listener
def _on_init(environment, **kwargs):
    global _pool, _recorder, _tokens
    logging.basicConfig(
        level=logging.INFO,
        format="[%(asctime)s] %(levelname)-5s %(name)s %(message)s",
        datefmt="%H:%M:%S",
    )
    # signalrcore is INFO-chatty at battle pace; raise its bar.
    logging.getLogger("SignalRCoreClient").setLevel(logging.WARNING)
    # httpx logs every request at INFO; not useful at 25 concurrent.
    logging.getLogger("httpx").setLevel(logging.WARNING)

    if not USERS_MANIFEST.exists():
        raise SystemExit(
            f"users-manifest.json not found at {USERS_MANIFEST}. "
            f"Run: ./tests/Kombats.LoadTests/scripts/seed-users.sh --count 50"
        )
    users = json.loads(USERS_MANIFEST.read_text())
    _pool = UserPool(users)

    log_path = default_log_path(str(ITERATION_LOG_DIR))
    _recorder = IterationRecorder(log_path)
    _tokens = TokenClient()

    _log.info("Locust init: pool=%d log=%s", len(users), log_path)


@events.quitting.add_listener
def _on_quitting(environment, **kwargs):
    if _recorder is not None:
        try:
            _recorder.close()
        except Exception:
            pass
    if _tokens is not None:
        try:
            _tokens.close()
        except Exception:
            pass


# ---- Locust User -------------------------------------------------------------


class BotUser(User):
    """One Locust User = one bot, persistent for the run, looping battles."""

    # Capacity test: bots re-queue immediately. No think-time.
    wait_time = constant(0)

    # Locust's HTTP client is unused (our transport is httpx + signalrcore via
    # the B1a clients). Setting host silences Locust's "no host" warning only.
    host = "http://localhost:5000"

    def on_start(self) -> None:
        self.username: Optional[str] = None
        self.password: Optional[str] = None
        self.sub: Optional[str] = None
        try:
            creds = _pool.take()
        except Exception as ex:
            _log.error("user pool take() failed: %s", ex)
            return
        self.username = creds["username"]
        self.password = creds.get("password", "loadtest")
        self.sub = creds["sub"]

    def on_stop(self) -> None:
        if self.username:
            _pool.release(self.username)
            self.username = None

    @task
    def play_battle(self) -> None:
        if not self.username:
            # on_start failed — let the User idle until the run ends.
            return
        try:
            player = VirtualPlayer(
                username=self.username,
                password=self.password,
                sub=self.sub,
                token_client=_tokens,
                random_seed=hash(self.username) & 0xFFFF,
            )
            result = player.run_one_battle()
        except Exception as ex:
            # VirtualPlayer.run_one_battle catches Exception internally and
            # returns OUTCOME_ERROR — this branch covers anything that escapes
            # (e.g. an unexpected greenlet-level fault). Record as Error and
            # keep the User alive so the run does not lose a concurrency slot.
            _log.warning(
                "play_battle: unexpected exception for %s: %s: %s",
                self.username, type(ex).__name__, ex,
            )
            _recorder.record(IterationResult(
                username=self.username,
                battle_id=None,
                outcome=OUTCOME_ERROR,
                error=f"{type(ex).__name__}: {ex}",
                turns_played=0,
                auth_ms=0, onboard_ms=0, connect_ms=0,
                queue_wait_ms=0, join_battle_ms=0,
                battle_ms=0, total_ms=0,
            ))
            return

        _recorder.record(IterationResult(
            username=result.username,
            battle_id=result.battle_id,
            outcome=result.outcome,
            error=result.error,
            turns_played=result.turns_played,
            auth_ms=result.auth_ms,
            onboard_ms=result.onboard_ms,
            connect_ms=result.connect_ms,
            queue_wait_ms=result.queue_wait_ms,
            join_battle_ms=result.join_battle_ms,
            battle_ms=result.battle_ms,
            total_ms=result.total_ms,
        ))


# ---- Load shape: ramp 0->RAMP_USERS, hold, stop --------------------------------


def _env_int(name: str, default: int) -> int:
    raw = os.environ.get(name)
    if raw is None or raw == "":
        return default
    try:
        value = int(raw)
    except ValueError:
        raise SystemExit(f"{name} must be an integer, got {raw!r}")
    if value <= 0:
        raise SystemExit(f"{name} must be > 0, got {value}")
    return value


class Run0Shape(LoadTestShape):
    """Parametric ramp-and-hold shape.

    Defaults (RAMP_USERS=25, RAMP_SECONDS=30, HOLD_SECONDS=90) reproduce the
    Run 0 calibration shape (ConcurrentBattlesScenario.cs:101-106). Override
    any of them via env vars to drive Phase D capacity stages without code
    changes — e.g. Stage 2 = `RAMP_USERS=50 HOLD_SECONDS=300 locust ...`.

    NBomber "copies" and Locust "users" differ in one detail: NBomber
    iterations are independent (each iteration = one bot session = one
    half-battle, finite lifetime), Locust users are persistent and loop.
    With wait_time = constant(0) and N persistent users running battles
    in a tight loop, the effective concurrency is the same: N concurrent
    half-battles at any moment in the hold phase.
    """

    RAMP_USERS = _env_int("RAMP_USERS", 25)
    RAMP_SECONDS = _env_int("RAMP_SECONDS", 30)
    HOLD_SECONDS = _env_int("HOLD_SECONDS", 90)
    TOTAL_SECONDS = RAMP_SECONDS + HOLD_SECONDS

    def tick(self):
        run_time = self.get_run_time()
        if run_time >= self.TOTAL_SECONDS:
            return None
        if run_time < self.RAMP_SECONDS:
            # Linear ramp 0 -> RAMP_USERS. Spawn rate = RAMP_USERS so Locust
            # never lags behind the requested per-tick user count.
            users = max(1, int(self.RAMP_USERS * run_time / self.RAMP_SECONDS))
            return (users, float(self.RAMP_USERS))
        return (self.RAMP_USERS, float(self.RAMP_USERS))
