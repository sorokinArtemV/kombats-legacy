import { describe, it, expect, vi } from 'vitest';

// See battle-hub.test.ts for the same stub. Lets us import the hub managers
// without VITE_* env vars being defined.
vi.mock('@/config', () => ({
  config: {
    keycloak: { authority: 'http://test', clientId: 'test' },
    bff: { baseUrl: 'http://test' },
  },
}));

import { BattleHubManager } from './battle-hub';
import { ChatHubManager } from './chat-hub';
import type { ConnectionState } from './connection-state';

// ---------------------------------------------------------------------------
// Drive the lifecycle callbacks directly. Each manager registers onreconnecting
// / onreconnected / onclose with the underlying HubConnection in
// registerLifecycle(); we simulate that by invoking its private method with a
// fake HubConnection that captures the registered handlers.
// ---------------------------------------------------------------------------

interface FakeConn {
  onreconnecting(cb: () => void): void;
  onreconnected(cb: () => void): void;
  onclose(cb: () => void): void;
  triggerReconnecting: () => void;
  triggerReconnected: () => void;
  triggerClose: () => void;
}

function makeFakeConn(): FakeConn {
  let onReconnecting: () => void = () => {};
  let onReconnected: () => void = () => {};
  let onClose: () => void = () => {};
  return {
    onreconnecting: (cb) => {
      onReconnecting = cb;
    },
    onreconnected: (cb) => {
      onReconnected = cb;
    },
    onclose: (cb) => {
      onClose = cb;
    },
    triggerReconnecting: () => onReconnecting(),
    triggerReconnected: () => onReconnected(),
    triggerClose: () => onClose(),
  };
}

// Both managers keep `registerLifecycle` private. We invoke it via a typed
// cast — its signature is identical across the two managers, and the tests
// only depend on that public behavior (state transitions), not on any
// private internals that could change independently.
function wireLifecycle(manager: BattleHubManager | ChatHubManager, conn: FakeConn): void {
  (
    manager as unknown as {
      registerLifecycle: (c: FakeConn) => void;
    }
  ).registerLifecycle(conn);
}

describe.each<[string, () => BattleHubManager | ChatHubManager]>([
  ['BattleHubManager', () => new BattleHubManager(() => 'tok')],
  ['ChatHubManager', () => new ChatHubManager(() => 'tok')],
])('%s lifecycle', (_label, factory) => {
  it('reports transient onclose as disconnected (no prior reconnect attempt)', () => {
    const manager = factory();
    const conn = makeFakeConn();
    wireLifecycle(manager, conn);

    const seen: ConnectionState[] = [];
    manager.setEventHandlers({
      onConnectionStateChanged: (state) => seen.push(state),
    });

    conn.triggerClose();
    expect(seen).toEqual(['disconnected']);
  });

  it('reports terminal failure as `failed` when reconnect budget is exhausted', () => {
    const manager = factory();
    const conn = makeFakeConn();
    wireLifecycle(manager, conn);

    const seen: ConnectionState[] = [];
    manager.setEventHandlers({
      onConnectionStateChanged: (state) => seen.push(state),
    });

    conn.triggerReconnecting();
    conn.triggerClose();

    expect(seen).toEqual(['reconnecting', 'failed']);
  });

  it('clears the reconnect marker on successful reconnect', () => {
    const manager = factory();
    const conn = makeFakeConn();
    wireLifecycle(manager, conn);

    const seen: ConnectionState[] = [];
    manager.setEventHandlers({
      onConnectionStateChanged: (state) => seen.push(state),
    });

    conn.triggerReconnecting();
    conn.triggerReconnected();
    conn.triggerClose();

    // Last close is now clean (reconnect already consumed the marker).
    expect(seen).toEqual(['reconnecting', 'connected', 'disconnected']);
  });
});
