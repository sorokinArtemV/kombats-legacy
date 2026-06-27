import type { QueueStatusResponse } from '@/types/api';

/**
 * Pure async core of the post-battle re-queue retry loop. Lives outside
 * `hooks.ts` so the decision tree is unit-testable without pulling the
 * transport client (which depends on config) into the test bundle.
 *
 * Behavior:
 * - Initial join + retry per configured delay (so `delays.length + 1`
 *   total attempts in the worst case).
 * - `Searching` → success, return `searching`.
 * - `Matched` with a battleId different from the just-dismissed one →
 *   real new match, return `newMatch` so the caller can honor it.
 * - `Matched` with the dismissed battleId → backend's BattleCompleted
 *   projection has not landed yet; sleep, retry. Capped by `delays`.
 * - Any other shape (Idle / NotQueued / unexpected) → return
 *   `unexpectedShape`; the lobby's setGameState refetch will reconcile.
 * - Thrown rejection → `networkError`.
 */

export type RequeueOutcome =
  | { kind: 'searching' }
  | { kind: 'newMatch'; queueStatus: QueueStatusResponse }
  | { kind: 'failed'; reason: 'staleMatch' | 'networkError' | 'unexpectedShape' };

export interface RequeueLoopDeps {
  /** Battle the user just dismissed. Used to detect the stale-match condition. */
  battleId: string;
  /** Wrapped `queueApi.join` so tests can inject a deterministic fake. */
  joinFn: (connectionRef: string) => Promise<QueueStatusResponse>;
  connectionRef: string;
  /** Per-attempt wait list. Loop runs up to `delays.length + 1` times total. */
  delays: readonly number[];
  /** Wrapped setTimeout so tests can advance time without real-clock waits. */
  sleep: (ms: number) => Promise<void>;
}

export async function executeRequeueLoop(deps: RequeueLoopDeps): Promise<RequeueOutcome> {
  for (let attempt = 0; attempt <= deps.delays.length; attempt++) {
    let response: QueueStatusResponse;
    try {
      response = await deps.joinFn(deps.connectionRef);
    } catch {
      return { kind: 'failed', reason: 'networkError' };
    }

    if (response.status === 'Matched' && response.battleId && response.battleId !== deps.battleId) {
      return { kind: 'newMatch', queueStatus: response };
    }

    if (response.status === 'Searching') {
      return { kind: 'searching' };
    }

    const stale = response.status === 'Matched' && response.battleId === deps.battleId;
    if (!stale) {
      return { kind: 'failed', reason: 'unexpectedShape' };
    }

    const delay = deps.delays[attempt];
    if (delay === undefined) break;
    await deps.sleep(delay);
  }

  return { kind: 'failed', reason: 'staleMatch' };
}
