"""
Keycloak ROPC token client with thread-safe in-process cache.

Port of tests/Kombats.LoadTests/Authentication/KeycloakTokenClient.cs.

Differences from C#:
- Python dict is NOT thread-safe like ConcurrentDictionary; we wrap reads
  and writes in a threading.Lock (Chapter 4 risk register H6, B0 caveat).
- Network call (POST /token) is performed OUTSIDE the lock so concurrent
  cache misses for different users do not serialize.
- Hardcoded localhost / kombats-loadtest per Phase B1a constraints; B1b
  generalizes if the architect needs it.
"""

from __future__ import annotations

import base64
import json
import threading
import time
from dataclasses import dataclass

import httpx
import jwt

KEYCLOAK_BASE_URL = "http://localhost:8080"
REALM = "kombats"
CLIENT_ID = "kombats-loadtest"
CLIENT_SECRET = "loadtest-secret-do-not-use-in-prod"

# Same as C# KeycloakTokenClient.RefreshMargin = TimeSpan.FromMinutes(5).
REFRESH_MARGIN_SEC = 5 * 60


@dataclass(frozen=True)
class _CachedToken:
    access_token: str
    # Wall-clock seconds (time.time()) at which we treat the token as stale.
    # Already includes the refresh margin baked in.
    expires_at: float

    def is_fresh(self, now: float) -> bool:
        return now < self.expires_at


class TokenClient:
    def __init__(self) -> None:
        self._cache: dict[str, _CachedToken] = {}
        self._lock = threading.Lock()
        self._http = httpx.Client(base_url=KEYCLOAK_BASE_URL, timeout=10.0)

    def get_access_token(self, username: str, password: str) -> str:
        now = time.time()
        with self._lock:
            entry = self._cache.get(username)
            if entry is not None and entry.is_fresh(now):
                return entry.access_token

        # Network call OUTSIDE lock so other threads can hit the cache.
        new_token = self._fetch(username, password)

        with self._lock:
            self._cache[username] = new_token
        return new_token.access_token

    def _fetch(self, username: str, password: str) -> _CachedToken:
        resp = self._http.post(
            f"/realms/{REALM}/protocol/openid-connect/token",
            data={
                "grant_type": "password",
                "client_id": CLIENT_ID,
                "client_secret": CLIENT_SECRET,
                "username": username,
                "password": password,
            },
        )
        if resp.status_code != 200:
            raise RuntimeError(
                f"ROPC failed for {username}: HTTP {resp.status_code}. Body: {resp.text}"
            )
        body = resp.json()
        access_token = body.get("access_token")
        if not access_token:
            raise RuntimeError(f"ROPC for {username}: access_token missing in response")
        expires_at = self._extract_expiry(access_token) - REFRESH_MARGIN_SEC
        return _CachedToken(access_token=access_token, expires_at=expires_at)

    @staticmethod
    def _extract_expiry(access_token: str) -> float:
        # Trust the token; we just got it from a successful ROPC. No verify.
        try:
            claims = jwt.decode(access_token, options={"verify_signature": False})
            exp = claims.get("exp")
            if isinstance(exp, (int, float)):
                return float(exp)
        except Exception:
            pass
        # Fallback: assume 30 minutes (mirrors C# fallback).
        return time.time() + 30 * 60

    def get_sub(self, access_token: str) -> str:
        claims = jwt.decode(access_token, options={"verify_signature": False})
        sub = claims.get("sub")
        if not sub:
            raise RuntimeError("token has no sub claim")
        return sub

    def close(self) -> None:
        self._http.close()
