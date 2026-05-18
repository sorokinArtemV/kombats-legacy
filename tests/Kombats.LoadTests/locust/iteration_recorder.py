"""
Thread-safe JSONL writer — port of Reporting/IterationRecorder.cs.

Schema MUST be byte-identical so scripts/aggregate-phases.sh works unchanged.

Field order (snake_case):
  ts, username, battle_id, outcome, error, turns_played,
  auth_ms, onboard_ms, connect_ms, queue_wait_ms,
  join_battle_ms, battle_ms, total_ms

null fields (battle_id, error) are OMITTED from the JSON line — same as the
C# JsonIgnoreCondition.WhenWritingNull behavior.
"""

from __future__ import annotations

import json
import os
import threading
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Optional


@dataclass
class IterationResult:
    username: str
    battle_id: Optional[str]
    outcome: str  # Won / Lost / Draw / Error / QueueTimeout / BattleTimeout
    error: Optional[str]
    turns_played: int
    auth_ms: float
    onboard_ms: float
    connect_ms: float
    queue_wait_ms: float
    join_battle_ms: float
    battle_ms: float
    total_ms: float


class IterationRecorder:
    def __init__(self, file_path: str) -> None:
        self.file_path = file_path
        Path(os.path.dirname(file_path)).mkdir(parents=True, exist_ok=True)
        # newline='\n' is explicit (not platform-dependent): aggregate-phases.sh
        # parses LF-delimited JSONL and the historical baseline files are LF-only.
        self._fp = open(file_path, "w", buffering=1, newline="\n")
        self._lock = threading.Lock()

    def record(self, r: IterationResult) -> None:
        # ISO 8601 with timezone offset, e.g. "2026-05-15T10:30:00.123456+00:00".
        ts = datetime.now(timezone.utc).astimezone().isoformat()
        # Build the dict in the SAME order as the C# IterationRecord constructor
        # so the on-disk field order matches the historical baseline.
        record: dict = {"ts": ts, "username": r.username}
        if r.battle_id is not None:
            record["battle_id"] = r.battle_id
        record["outcome"] = r.outcome
        if r.error is not None:
            record["error"] = r.error
        # Task spec says int (rounded) — aggregate-phases.sh treats both via
        # `tonumber`. Round half-to-even is the Python default; close enough.
        record["turns_played"] = int(r.turns_played)
        record["auth_ms"] = int(round(r.auth_ms))
        record["onboard_ms"] = int(round(r.onboard_ms))
        record["connect_ms"] = int(round(r.connect_ms))
        record["queue_wait_ms"] = int(round(r.queue_wait_ms))
        record["join_battle_ms"] = int(round(r.join_battle_ms))
        record["battle_ms"] = int(round(r.battle_ms))
        record["total_ms"] = int(round(r.total_ms))

        line = json.dumps(record, separators=(",", ":"))
        with self._lock:
            self._fp.write(line + "\n")
            self._fp.flush()

    def close(self) -> None:
        with self._lock:
            try:
                self._fp.flush()
                self._fp.close()
            except Exception:
                pass


def default_log_path(base_dir: str) -> str:
    """tests/Kombats.LoadTests/iteration-logs/iterations-YYYY-MM-DD--HH-MM-SS.jsonl"""
    stamp = datetime.now().strftime("%Y-%m-%d--%H-%M-%S")
    return os.path.join(base_dir, f"iterations-{stamp}.jsonl")
