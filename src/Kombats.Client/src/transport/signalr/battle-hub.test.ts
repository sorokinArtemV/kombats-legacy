import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { HubConnectionState } from '@microsoft/signalr';

// The production config module reads Vite env vars at load time. Stub it so
// this test can import BattleHubManager without VITE_* env vars being set.
vi.mock('@/config', () => ({
  config: {
    keycloak: { authority: 'http://test', clientId: 'test' },
    bff: { baseUrl: 'http://test' },
  },
}));

import { BattleHubManager } from './battle-hub';

// ---------------------------------------------------------------------------
// Helpers: inject a fake HubConnection so we don't need a live SignalR server.
// ---------------------------------------------------------------------------

type FakeConnection = {
  state: HubConnectionState;
  invoke: ReturnType<typeof vi.fn>;
};

function installFakeConnection(manager: BattleHubManager, conn: FakeConnection): void {
  (manager as unknown as { connection: FakeConnection }).connection = conn;
}

function makeConnection(invokeImpl: (...args: unknown[]) => Promise<unknown>): FakeConnection {
  return {
    state: HubConnectionState.Connected,
    invoke: vi.fn(invokeImpl),
  };
}

const BATTLE_ID = '11111111-1111-1111-1111-111111111111';

// The SignalR JS client wraps HubExceptions thrown by the server in this format.
function notFoundError(battleId: string): Error {
  return new Error(
    `An unexpected error occurred invoking 'JoinBattle' on the server. HubException: Battle ${battleId} not found`,
  );
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('BattleHubManager.joinBattle', () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('retries on transient "Battle X not found" and resolves once the battle is ready', async () => {
    const manager = new BattleHubManager(() => 'fake-token');
    const snapshot = { battleId: BATTLE_ID } as unknown;

    let attempt = 0;
    const conn = makeConnection(() => {
      attempt++;
      if (attempt < 3) {
        return Promise.reject(notFoundError(BATTLE_ID));
      }
      return Promise.resolve(snapshot);
    });
    installFakeConnection(manager, conn);

    const promise = manager.joinBattle(BATTLE_ID);
    // Let the retry backoffs and their awaited invokes flush.
    await vi.runAllTimersAsync();

    await expect(promise).resolves.toBe(snapshot);
    expect(conn.invoke).toHaveBeenCalledTimes(3);
    expect(conn.invoke).toHaveBeenNthCalledWith(1, 'JoinBattle', BATTLE_ID);
  });

  it('does not retry permanent errors (e.g. not-a-participant)', async () => {
    const manager = new BattleHubManager(() => 'fake-token');
    const permanent = new Error(
      "An unexpected error occurred invoking 'JoinBattle' on the server. HubException: User is not a participant in this battle",
    );
    const conn = makeConnection(() => Promise.reject(permanent));
    installFakeConnection(manager, conn);

    await expect(manager.joinBattle(BATTLE_ID)).rejects.toBe(permanent);
    expect(conn.invoke).toHaveBeenCalledTimes(1);
  });

  it('gives up after exhausting the retry budget and surfaces the last error', async () => {
    const manager = new BattleHubManager(() => 'fake-token');
    const transient = notFoundError(BATTLE_ID);
    const conn = makeConnection(() => Promise.reject(transient));
    installFakeConnection(manager, conn);

    // Catch up-front so `await vi.runAllTimersAsync()` doesn't see an unhandled rejection.
    const settled = manager.joinBattle(BATTLE_ID).then(
      (v) => ({ ok: true as const, value: v }),
      (e: unknown) => ({ ok: false as const, error: e }),
    );
    await vi.runAllTimersAsync();
    const result = await settled;

    expect(result.ok).toBe(false);
    if (!result.ok) expect(result.error).toBe(transient);
    // 1 initial + 7 backoff retries = 8 total attempts.
    expect(conn.invoke).toHaveBeenCalledTimes(8);
  });

  it('throws immediately if the connection is not connected', async () => {
    const manager = new BattleHubManager(() => 'fake-token');
    // No connection installed.
    await expect(manager.joinBattle(BATTLE_ID)).rejects.toThrow(/not connected/i);
  });
});
