"""
Phase B1a entry point. Runs ONE bot through ONE complete battle and
emits one JSONL row to ../iteration-logs/iterations-{ts}.jsonl.

Usage:
    python run_one_battle.py loadbot-0001
    python run_one_battle.py loadbot-0002

Two terminals running this within ~10s of each other should produce a
matched battle. Exit code 0 on Won/Lost/Draw, 1 otherwise.

NOT a Locust file. NO @task decorator. That's Phase B1b's job.
"""

from __future__ import annotations

import json
import logging
import os
import sys
import time
from pathlib import Path

# Ensure sibling modules import cleanly when run as a script.
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from iteration_recorder import IterationRecorder, IterationResult, default_log_path
from token_client import TokenClient
from virtual_player import (
    OUTCOME_DRAW,
    OUTCOME_LOST,
    OUTCOME_WON,
    VirtualPlayer,
)

# tests/Kombats.LoadTests/locust/ -> tests/Kombats.LoadTests/
PROJECT_ROOT = Path(__file__).resolve().parent.parent
USERS_MANIFEST = PROJECT_ROOT / "users-manifest.json"
ITERATION_LOG_DIR = PROJECT_ROOT / "iteration-logs"


def _setup_logging(username: str) -> logging.Logger:
    logging.basicConfig(
        level=logging.INFO,
        format=f"[%(asctime)s] {username:14s} %(name)-10s %(message)s",
        datefmt="%H:%M:%S",
    )
    # signalrcore is chatty at INFO; mute it unless something goes wrong.
    logging.getLogger("SignalRCoreClient").setLevel(logging.WARNING)
    return logging.getLogger("main")


def _load_user(username: str) -> dict:
    if not USERS_MANIFEST.exists():
        sys.exit(
            f"users-manifest.json not found at {USERS_MANIFEST}. "
            f"Run RUNBOOK step 6: ./tests/Kombats.LoadTests/scripts/seed-users.sh --count 50"
        )
    manifest = json.loads(USERS_MANIFEST.read_text())
    for u in manifest:
        if u.get("username") == username:
            return u
    sys.exit(f"username '{username}' not found in users-manifest.json")


def main() -> int:
    if len(sys.argv) != 2:
        print(__doc__, file=sys.stderr)
        return 2
    username = sys.argv[1]
    log = _setup_logging(username)

    user = _load_user(username)
    sub = user.get("sub")
    password = user.get("password", "loadtest")
    log.info("loaded user: username=%s sub=%s", username, sub)

    token_client = TokenClient()
    log_path = default_log_path(str(ITERATION_LOG_DIR))
    recorder = IterationRecorder(log_path)
    log.info("writing iteration log to %s", log_path)

    player = VirtualPlayer(
        username=username,
        password=password,
        sub=sub,
        token_client=token_client,
        random_seed=hash(username) & 0xFFFF,
    )

    log.info("step 0: starting bot lifecycle")
    t0 = time.monotonic()
    result = player.run_one_battle()
    elapsed = (time.monotonic() - t0)
    log.info(
        "step done: outcome=%s turns=%d battle=%s elapsed=%.2fs",
        result.outcome,
        result.turns_played,
        (result.battle_id or "(none)"),
        elapsed,
    )
    if result.error:
        log.warning("error: %s", result.error)

    recorder.record(IterationResult(
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
    recorder.close()
    token_client.close()

    if result.outcome in (OUTCOME_WON, OUTCOME_LOST, OUTCOME_DRAW):
        log.info("✅ acceptance outcome: %s", result.outcome)
        return 0
    log.warning("❌ non-success outcome: %s", result.outcome)
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
