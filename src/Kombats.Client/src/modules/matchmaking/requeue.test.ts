import { describe, it, expect, vi } from 'vitest';
import { executeRequeueLoop } from './requeue-loop';
import type { QueueStatusResponse } from '@/types/api';

const BATTLE_ID = 'battle-just-finished';
const CONN_REF = 'conn-1';
const DELAYS = [400, 800, 1200] as const;

function searching(): QueueStatusResponse {
  return { status: 'Searching', matchId: null, battleId: null, matchState: null };
}

function staleMatched(): QueueStatusResponse {
  return {
    status: 'Matched',
    matchId: 'match-prior',
    battleId: BATTLE_ID,
    matchState: 'Completed',
  };
}

function freshMatched(battleId = 'battle-new'): QueueStatusResponse {
  return {
    status: 'Matched',
    matchId: 'match-new',
    battleId,
    matchState: 'BattleCreated',
  };
}

interface Harness {
  joinFn: ReturnType<typeof vi.fn>;
  sleep: ReturnType<typeof vi.fn>;
  sleepDurations: number[];
  run: () => ReturnType<typeof executeRequeueLoop>;
}

function makeHarness(responses: Array<QueueStatusResponse | Error>): Harness {
  const joinFn = vi.fn(async () => {
    const next = responses.shift();
    if (next === undefined) throw new Error('No more mock responses queued');
    if (next instanceof Error) throw next;
    return next;
  });
  const sleepDurations: number[] = [];
  const sleep = vi.fn(async (ms: number) => {
    sleepDurations.push(ms);
  });

  return {
    joinFn,
    sleep,
    sleepDurations,
    run: () =>
      executeRequeueLoop({
        battleId: BATTLE_ID,
        joinFn,
        connectionRef: CONN_REF,
        delays: DELAYS,
        sleep,
      }),
  };
}

describe('executeRequeueLoop', () => {
  it('returns searching on the very first response when backend has already cleared', async () => {
    const h = makeHarness([searching()]);

    const outcome = await h.run();

    expect(outcome).toEqual({ kind: 'searching' });
    expect(h.joinFn).toHaveBeenCalledTimes(1);
    expect(h.joinFn).toHaveBeenCalledWith(CONN_REF);
    expect(h.sleepDurations).toEqual([]);
  });

  it('returns newMatch when the backend hands back a different battleId', async () => {
    const newQueueStatus = freshMatched('battle-fresh');
    const h = makeHarness([newQueueStatus]);

    const outcome = await h.run();

    expect(outcome).toEqual({ kind: 'newMatch', queueStatus: newQueueStatus });
    expect(h.joinFn).toHaveBeenCalledTimes(1);
    expect(h.sleepDurations).toEqual([]);
  });

  it('retries on stale-match and resolves when the backend catches up', async () => {
    const h = makeHarness([staleMatched(), staleMatched(), searching()]);

    const outcome = await h.run();

    expect(outcome).toEqual({ kind: 'searching' });
    expect(h.joinFn).toHaveBeenCalledTimes(3);
    // Sleep happened between attempt 0→1 and 1→2 only (third attempt
    // resolved before any further wait).
    expect(h.sleepDurations).toEqual([400, 800]);
  });

  it('exhausts the bounded retry on persistent stale-match', async () => {
    const h = makeHarness([staleMatched(), staleMatched(), staleMatched(), staleMatched()]);

    const outcome = await h.run();

    expect(outcome).toEqual({ kind: 'failed', reason: 'staleMatch' });
    // 4 attempts (1 initial + 3 retries), 3 sleeps in between.
    expect(h.joinFn).toHaveBeenCalledTimes(4);
    expect(h.sleepDurations).toEqual([400, 800, 1200]);
  });

  it('eventually returns newMatch if the backend resolves to a fresh battle mid-retry', async () => {
    const fresh = freshMatched('battle-after-retry');
    const h = makeHarness([staleMatched(), fresh]);

    const outcome = await h.run();

    expect(outcome).toEqual({ kind: 'newMatch', queueStatus: fresh });
    expect(h.joinFn).toHaveBeenCalledTimes(2);
    expect(h.sleepDurations).toEqual([400]);
  });

  it('returns networkError on a thrown rejection without retrying', async () => {
    const h = makeHarness([new Error('network down')]);

    const outcome = await h.run();

    expect(outcome).toEqual({ kind: 'failed', reason: 'networkError' });
    expect(h.joinFn).toHaveBeenCalledTimes(1);
    expect(h.sleepDurations).toEqual([]);
  });

  it('returns unexpectedShape and bails when the backend responds Idle', async () => {
    const h = makeHarness([{ status: 'Idle', matchId: null, battleId: null, matchState: null }]);

    const outcome = await h.run();

    expect(outcome).toEqual({ kind: 'failed', reason: 'unexpectedShape' });
    expect(h.joinFn).toHaveBeenCalledTimes(1);
  });
});
