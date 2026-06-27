"""
SignalR client for the BFF /battlehub. Port of SignalR/BattleHubClient.cs.

Built on signalrcore 1.0.2. Two ports of note:

1. invoke_with_result + on_error routing (B0 finding #2)
   ------------------------------------------------------
   signalrcore's BaseHubConnection.__on_completion_message routes
   CompletionMessage.error to the GLOBAL hub.on_error handler — NOT to
   the per-invocation on_invocation callback.

   Workaround: we generate our own invocation_id UUID per call, pass it
   through `connection.send(method, args, on_invocation=cb, invocation_id=id)`,
   keep a dict {invocation_id -> (Event, container)}, and have the global
   on_error look up the entry by invocation_id and surface the error. The
   CompletionMessage object delivered to on_error has both .invocation_id
   and .error, so the lookup works deterministically with no fallback.

2. JoinBattle 8-step retry on transient "battle not found" (mirrors
   BattleHubClient.cs:18 + 71-101 verbatim).
"""

from __future__ import annotations

import logging
import threading
import uuid
from typing import Any, Callable, Dict, List, Optional, Tuple

from signalrcore.hub_connection_builder import HubConnectionBuilder

BFF_HUB_URL = "http://localhost:5000/battlehub"

# Verbatim from BattleHubClient.cs:18.
JOIN_BATTLE_RETRY_DELAYS_MS = (250, 500, 750, 1000, 1500, 2000, 2000)

# Same regex shape as battle-hub.ts:32. Substring match on lowercased message.
TRANSIENT_BATTLE_NOT_FOUND_FRAGMENT = "not found"


class HubError(RuntimeError):
    pass


class HubTimeout(RuntimeError):
    pass


class BattleHubClient:
    """One instance per virtual player.

    Lifecycle:
        client = BattleHubClient(token_factory)
        client.on('TurnOpened', handler)   # before connect
        ...
        client.connect()                   # blocks until open
        snapshot = client.join_battle(battle_id)
        client.submit_turn_action(battle_id, turn_index, payload)
        client.dispose()                   # best-effort stop
    """

    def __init__(
        self,
        token_factory: Callable[[], str],
        logger: Optional[logging.Logger] = None,
    ) -> None:
        self._token_factory = token_factory
        self._logger = logger or logging.getLogger("hub")
        self._connection = None
        self._handlers: Dict[str, Callable[[Any], None]] = {}
        self._opened = threading.Event()

        # Pending invocations registry: invocation_id -> (event, container)
        # container = {"value": Any | None, "error": str | None}
        self._pending: Dict[str, Tuple[threading.Event, Dict[str, Any]]] = {}
        self._pending_lock = threading.Lock()

    # ---- subscription API ----------------------------------------------------------

    def on(self, event: str, handler: Callable[[Any], None]) -> None:
        """Register a handler for a server-pushed event. Must be called BEFORE
        connect() so the underlying signalrcore connection sees them at start."""
        self._handlers[event] = handler

    # ---- lifecycle -----------------------------------------------------------------

    def connect(self, timeout_sec: float = 10.0) -> None:
        if self._connection is not None:
            raise RuntimeError("BattleHubClient.connect called twice.")

        token = self._token_factory()
        self._connection = (
            HubConnectionBuilder()
            .with_url(
                BFF_HUB_URL,
                options={
                    # access_token_factory is invoked per WebSocket open, so a
                    # fresh token is acquired on every reconnect attempt. The
                    # token_factory closure delegates to TokenClient.
                    "access_token_factory": self._token_factory,
                    "verify_ssl": False,
                },
            )
            .with_automatic_reconnect(
                {
                    "type": "raw",
                    "keep_alive_interval": 10,
                    "reconnect_interval": 5,
                    "max_attempts": 5,
                }
            )
            .build()
        )

        # Wire the global error handler BEFORE registering per-event handlers
        # so we never miss an error frame that arrives before our subscriptions
        # are processed.
        self._connection.on_error(self._on_global_error)
        self._connection.on_open(self._opened.set)

        # Per-event subscriptions. signalrcore unwraps args itself for typed
        # events, but always delivers as a list — we hand the first element
        # to the user's handler to match the C# `On<T>(name, T)` shape.
        for event_name, handler in self._handlers.items():
            self._connection.on(event_name, self._wrap_handler(handler))

        self._connection.start()
        if not self._opened.wait(timeout=timeout_sec):
            raise HubTimeout(f"SignalR did not open within {timeout_sec}s")

    def dispose(self) -> None:
        if self._connection is None:
            return
        try:
            self._connection.stop()
        except Exception:
            # Best-effort cleanup — same as C# ValueTask DisposeAsync.
            pass
        self._connection = None

    # ---- invoke + retry ------------------------------------------------------------

    def join_battle(self, battle_id: str, timeout_sec: float = 10.0):
        """JoinBattle invoke-with-result + 8-step retry on transient
        'battle not found'. Mirrors BattleHubClient.cs:71-101 verbatim."""
        delays = JOIN_BATTLE_RETRY_DELAYS_MS
        last_err: Optional[Exception] = None
        for attempt in range(len(delays) + 1):
            try:
                return self.invoke_with_result(
                    "JoinBattle", [battle_id], timeout_sec=timeout_sec
                )
            except HubError as ex:
                last_err = ex
                if not self._is_transient_battle_not_found(str(ex)):
                    raise
                if attempt == len(delays):
                    break
                self._logger.debug(
                    "JoinBattle transient (attempt %d): %s", attempt + 1, ex
                )
                threading.Event().wait(delays[attempt] / 1000.0)
            except HubTimeout as ex:
                # Timeout is not the same as a hub-side "not found" — surface it.
                raise
        raise last_err or HubError("JoinBattle exhausted retries.")

    def submit_turn_action(self, battle_id: str, turn_index: int, payload_json: str) -> None:
        """Fire-and-forget. The server pushes TurnResolved + the next TurnOpened
        on success; we never wait on a completion frame here."""
        if self._connection is None:
            raise RuntimeError("BattleHubClient: not connected.")
        self._connection.send(
            "SubmitTurnAction", [battle_id, turn_index, payload_json]
        )

    def invoke_with_result(
        self,
        method: str,
        args: List[Any],
        timeout_sec: float = 10.0,
    ):
        """Bridge signalrcore's send(on_invocation=cb) into a synchronous call.

        Generates our own invocation_id so the global on_error path can route
        the error back to this caller's container (B0 finding #2)."""
        if self._connection is None:
            raise RuntimeError("BattleHubClient: not connected.")

        invocation_id = str(uuid.uuid4())
        done = threading.Event()
        container: Dict[str, Any] = {"value": None, "error": None}

        with self._pending_lock:
            self._pending[invocation_id] = (done, container)

        def on_completion(msg):
            # signalrcore sometimes wraps the CompletionMessage in a 1-element
            # list; normalize.
            if isinstance(msg, list) and msg:
                msg = msg[0]
            self._resolve(invocation_id, value=getattr(msg, "result", None), error=None)

        try:
            self._connection.send(
                method,
                args,
                on_invocation=on_completion,
                invocation_id=invocation_id,
            )
            if not done.wait(timeout=timeout_sec):
                raise HubTimeout(f"{method} did not complete within {timeout_sec}s")
            if container["error"]:
                raise HubError(container["error"])
            return container["value"]
        finally:
            with self._pending_lock:
                self._pending.pop(invocation_id, None)

    # ---- internals -----------------------------------------------------------------

    def _wrap_handler(self, handler: Callable[[Any], None]) -> Callable[[Any], None]:
        def _wrapped(args):
            try:
                payload = args[0] if isinstance(args, list) and args else args
                handler(payload)
            except Exception as ex:
                self._logger.warning("hub event handler raised: %s", ex)

        return _wrapped

    def _on_global_error(self, message) -> None:
        """signalrcore routes ALL CompletionMessage.error frames here (B0 #2).
        Look up the pending invocation by id and forward the error string."""
        invocation_id = getattr(message, "invocation_id", None)
        error = getattr(message, "error", None) or str(message)
        if invocation_id:
            self._resolve(invocation_id, value=None, error=error)
        else:
            # Defensive: log unattached errors. In practice this should not
            # happen — the server always echoes invocation_id.
            self._logger.warning("hub error without invocation_id: %s", error)

    def _resolve(self, invocation_id: str, value, error) -> None:
        with self._pending_lock:
            pending = self._pending.get(invocation_id)
        if pending is None:
            return
        done, container = pending
        if error is not None:
            container["error"] = error
        else:
            container["value"] = value
        done.set()

    @staticmethod
    def _is_transient_battle_not_found(message: str) -> bool:
        msg = (message or "").lower()
        return TRANSIENT_BATTLE_NOT_FOUND_FRAGMENT in msg and "battle" in msg
