"""
BFF HTTP client — port of tests/Kombats.LoadTests/Transport/BffHttpClient.cs.

Eight methods: onboard, set_name, change_avatar, allocate_stats, get_game_state,
join_queue (7-step retry on Queue.NotReady), get_queue_status, heartbeat, leave_queue.

Retry tables are copied verbatim from C#. DO NOT alter intervals — they are
load-bearing for the first-onboarding race condition (matchmaking projection of
PlayerCombatProfileChanged is async via RabbitMQ + MassTransit).

Hardcoded localhost per Phase B1a constraints.
"""

from __future__ import annotations

import logging
import time
from typing import Any, Callable, Optional

import httpx

BFF_BASE_URL = "http://localhost:5000"

# Same as BffHttpClient.cs:94 and BattleHubClient.cs:18.
QUEUE_JOIN_RETRY_DELAYS_MS = (250, 500, 750, 1000, 1500, 2000, 2000)


class BffError(RuntimeError):
    """Raised on non-success HTTP from the BFF (after retry budget exhausted)."""


class BffClient:
    """One instance per virtual player so the bearer token is bound to one identity."""

    def __init__(
        self,
        token_factory: Callable[[], str],
        logger: Optional[logging.Logger] = None,
    ) -> None:
        self._token_factory = token_factory
        self._logger = logger or logging.getLogger("bff")
        self._http = httpx.Client(base_url=BFF_BASE_URL, timeout=10.0)

    # ---- onboarding / character ----------------------------------------------------

    def onboard(self) -> dict:
        """POST /api/v1/game/onboard — idempotent."""
        resp = self._http.post("/api/v1/game/onboard", headers=self._auth())
        if resp.status_code >= 400:
            raise BffError(f"onboard: HTTP {resp.status_code}. Body: {resp.text}")
        return resp.json()

    def set_name(self, name: str) -> None:
        """POST /api/v1/character/name — 409 is idempotent (already named)."""
        resp = self._http.post(
            "/api/v1/character/name",
            json={"name": name},
            headers=self._auth(),
        )
        if resp.status_code == 409:
            return
        if resp.status_code >= 400:
            raise BffError(f"set-name: HTTP {resp.status_code}. Body: {resp.text}")

    def change_avatar(self, expected_revision: int, avatar_id: str) -> None:
        """POST /api/v1/character/avatar — 409 is tolerated (mirrors C#)."""
        resp = self._http.post(
            "/api/v1/character/avatar",
            json={"expectedRevision": expected_revision, "avatarId": avatar_id},
            headers=self._auth(),
        )
        if resp.status_code == 409:
            return
        if resp.status_code >= 400:
            raise BffError(f"change-avatar: HTTP {resp.status_code}. Body: {resp.text}")

    def allocate_stats(
        self,
        strength: int,
        agility: int,
        intuition: int,
        vitality: int,
        expected_revision: int,
    ) -> Optional[dict]:
        """POST /api/v1/character/stats — 409 returns None (already allocated)."""
        resp = self._http.post(
            "/api/v1/character/stats",
            json={
                "strength": strength,
                "agility": agility,
                "intuition": intuition,
                "vitality": vitality,
                "expectedRevision": expected_revision,
            },
            headers=self._auth(),
        )
        if resp.status_code == 409:
            return None
        if resp.status_code >= 400:
            raise BffError(
                f"allocate-stats: HTTP {resp.status_code}. Body: {resp.text}"
            )
        return resp.json()

    def get_game_state(self) -> dict:
        resp = self._http.get("/api/v1/game/state", headers=self._auth())
        if resp.status_code >= 400:
            raise BffError(f"game-state: HTTP {resp.status_code}. Body: {resp.text}")
        return resp.json()

    # ---- queue ---------------------------------------------------------------------

    def join_queue(self, connection_ref: str) -> dict:
        """POST /api/v1/queue/join — 7-step retry on Queue.NotReady transients.

        Mirrors BffHttpClient.cs:86-125 verbatim. The matchmaking-side projection
        of PlayerCombatProfileChanged is async (RabbitMQ + MassTransit); a
        freshly-onboarded bot can outrun propagation, returning HTTP 400 with
        body containing 'Queue.NotReady' / 'Queue.NoCombatProfile' / 'Invalid
        request to Matchmaking'. All transient. Total budget ~8s.
        """
        delays = QUEUE_JOIN_RETRY_DELAYS_MS
        last_body = ""
        last_status = 0
        for attempt in range(len(delays) + 1):
            resp = self._http.post(
                "/api/v1/queue/join",
                json={"connectionRef": connection_ref},
                headers=self._auth(),
            )
            if resp.status_code in (200, 409):
                return resp.json()
            last_body = resp.text
            last_status = resp.status_code
            transient = resp.status_code == 400 and (
                "Queue.NotReady" in last_body
                or "Queue.NoCombatProfile" in last_body
                or "Invalid request to Matchmaking" in last_body
            )
            if not transient or attempt == len(delays):
                break
            self._logger.debug(
                "queue-join transient (attempt %d); waiting %dms",
                attempt + 1,
                delays[attempt],
            )
            time.sleep(delays[attempt] / 1000.0)
        raise BffError(f"queue-join: HTTP {last_status}. Body: {last_body}")

    def get_queue_status(self) -> dict:
        resp = self._http.get("/api/v1/queue/status", headers=self._auth())
        if resp.status_code >= 400:
            raise BffError(f"queue-status: HTTP {resp.status_code}. Body: {resp.text}")
        return resp.json()

    def heartbeat(self, connection_ref: str) -> None:
        """POST /api/v1/queue/heartbeat — best-effort, log-and-swallow on failure.

        SPA pings every 10s while searching to refresh the 15s presence-ref TTL.
        Single-miss is fine; the sweep worker is the safety net.
        """
        try:
            resp = self._http.post(
                "/api/v1/queue/heartbeat",
                json={"connectionRef": connection_ref},
                headers=self._auth(),
            )
            if resp.status_code >= 400:
                self._logger.warning(
                    "queue-heartbeat: HTTP %d. Body: %s", resp.status_code, resp.text
                )
        except Exception as ex:
            self._logger.warning("queue-heartbeat raised: %s", ex)

    def leave_queue(self, connection_ref: str) -> None:
        """POST /api/v1/queue/leave — 200 and 404 both fine."""
        try:
            resp = self._http.post(
                "/api/v1/queue/leave",
                json={"connectionRef": connection_ref},
                headers=self._auth(),
            )
            if resp.status_code >= 400 and resp.status_code != 404:
                self._logger.warning(
                    "queue-leave: HTTP %d. Body: %s", resp.status_code, resp.text
                )
        except Exception as ex:
            self._logger.warning("queue-leave raised: %s", ex)

    # ---- internals -----------------------------------------------------------------

    def _auth(self) -> dict[str, str]:
        return {"Authorization": f"Bearer {self._token_factory()}"}

    def close(self) -> None:
        self._http.close()
