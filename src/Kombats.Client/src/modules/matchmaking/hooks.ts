import { useEffect, useCallback, useState } from 'react';
import { useNavigate } from 'react-router';
import { useQueryClient } from '@tanstack/react-query';
import { matchmakingPoller } from '@/transport/polling/matchmaking-poller';
import * as queueApi from '@/transport/http/endpoints/queue';
import { gameKeys } from '@/app/query-client';
import { usePlayerStore } from '@/modules/player/store';
import { useBattleStore } from '@/modules/battle/store';
import { useMatchmakingStore } from './store';
import { deriveQueueUiStatus, type QueueUiStatus } from './queue-ui-status';
import { getQueueConnectionRef } from './connection-ref';
import { executeRequeueLoop } from './requeue-loop';
import { isApiError } from '@/types/api';
import type { QueueStatusResponse, LeaveQueueResponse } from '@/types/api';

export type { RequeueOutcome, RequeueLoopDeps } from './requeue-loop';
export { executeRequeueLoop } from './requeue-loop';

const POLL_INTERVAL_MS = 2000;
const HEARTBEAT_INTERVAL_MS = 10_000;

// Bounded-retry timing for the post-battle re-queue. Three attempts at
// 400ms / 800ms / 1200ms backoff (~2.4s total) covers the 1–2s window the
// backend may need to project the previous battle's completion before a
// fresh /queue/join can succeed. Conservative: not tuned without evidence.
const REQUEUE_RETRY_DELAYS_MS = [400, 800, 1200] as const;

/**
 * Public UI projection of the queue state. Derived from the authoritative
 * `usePlayerStore.queueStatus` + the UI-local `battleTransitioning` flag.
 *
 * Consumers get the single status value plus the local timer / failure
 * counter. `matchId` / `battleId` / `matchState` live on the player store
 * for anyone who needs them; matchmaking UIs don't.
 */
export function useQueueUiState(): {
  status: QueueUiStatus;
  searchStartedAt: number | null;
  consecutiveFailures: number;
} {
  const queueStatus = usePlayerStore((s) => s.queueStatus);
  const battleTransitioning = useMatchmakingStore((s) => s.battleTransitioning);
  const searchStartedAt = useMatchmakingStore((s) => s.searchStartedAt);
  const consecutiveFailures = useMatchmakingStore((s) => s.consecutiveFailures);

  return {
    status: deriveQueueUiStatus(queueStatus, battleTransitioning),
    searchStartedAt,
    consecutiveFailures,
  };
}

/**
 * Core matchmaking hook — exposes the derived status + join/leave actions.
 *
 * Syncs the UI-local matchmaking store back to its empty shape whenever the
 * authoritative queue status drops to `Idle` / `NotQueued` / `null`. This
 * effect runs wherever `useMatchmaking()` is mounted (lobby `QueueButton`
 * and `SearchingScreen`), so it covers the post-battle return and the
 * cancel-during-search flows without needing a dedicated observer.
 */
export function useMatchmaking() {
  const { status, searchStartedAt, consecutiveFailures } = useQueueUiState();
  const queueStatus = usePlayerStore((s) => s.queueStatus);
  const queryClient = useQueryClient();

  // Reset matchmaking UI state when authoritative queue becomes inactive.
  useEffect(() => {
    const inactive =
      !queueStatus || queueStatus.status === 'Idle' || queueStatus.status === 'NotQueued';
    if (!inactive) return;

    const mm = useMatchmakingStore.getState();
    if (mm.searchStartedAt !== null || mm.battleTransitioning || mm.consecutiveFailures !== 0) {
      mm.setIdle();
    }
  }, [queueStatus]);

  const joinQueue = useCallback(async () => {
    // Read the latest derived status via the stores — the closure's `status`
    // from a previous render would be stale on rapid re-clicks.
    const currentStatus = deriveQueueUiStatus(
      usePlayerStore.getState().queueStatus,
      useMatchmakingStore.getState().battleTransitioning,
    );
    if (currentStatus !== 'idle') return;

    // A fresh user-initiated join clears any stale post-battle requeue
    // failure flag — the user is opting into a new attempt, so the previous
    // notice is no longer relevant. Cleared up-front; flipped back true
    // only by useRequeueAfterBattle on the next failure.
    useMatchmakingStore.getState().setRequeueFailed(false);

    try {
      // BFF flattens upstream 200/409 into 200 + body, so the response body
      // is the source of truth (see queue.ts comment). Either Searching or
      // Matched can come back; both are valid outcomes the guard layer
      // routes from. Without reading the body we'd optimistically write
      // Searching even when the backend told us we are still Matched on a
      // prior battle the projection has not yet cleared (latent bug — see
      // useRequeueAfterBattle for the post-battle retry that handles the
      // "Matched on the just-dismissed battle" branch in particular).
      const response = await queueApi.join(getQueueConnectionRef());

      if (response.status === 'Searching') {
        useMatchmakingStore.getState().startSearch();
        usePlayerStore.getState().setQueueStatus({
          status: 'Searching',
          matchId: null,
          battleId: null,
          matchState: null,
        });
        return;
      }

      if (response.status === 'Matched' && response.battleId) {
        // Honor the authoritative match — BattleGuard will route the user
        // to /battle/:id from here. battleTransitioning gives downstream
        // observers the same intermediate read the polling path produces.
        useMatchmakingStore.getState().setBattleTransitioning(true);
        usePlayerStore.getState().setQueueStatus(response);
        return;
      }

      // Any other response shape — fall back to a server refetch.
      await queryClient.invalidateQueries({ queryKey: gameKeys.state() });
    } catch (err: unknown) {
      // 409 path is dead under the current BFF (which flattens 409→200),
      // but keep the branch defensive in case the BFF surface ever
      // changes. Server-state refetch lets the guards route from truth.
      if (isApiError(err) && err.status === 409) {
        await queryClient.invalidateQueries({ queryKey: gameKeys.state() });
        return;
      }
      // Non-409 failures (5xx, network, 400, etc.) — rethrow so the caller
      // can surface a visible error.
      throw err;
    }
  }, [queryClient]);

  const leaveQueue = useCallback(async () => {
    const currentStatus = deriveQueueUiStatus(
      usePlayerStore.getState().queueStatus,
      useMatchmakingStore.getState().battleTransitioning,
    );
    if (currentStatus !== 'searching' && currentStatus !== 'matched') return;

    try {
      const response: LeaveQueueResponse = await queueApi.leave(getQueueConnectionRef());

      if (!response.leftQueue && response.battleId) {
        // Late match — user pressed Cancel but the server already paired
        // them. Flip the transitioning flag and write the authoritative
        // Matched+battleId snapshot so BattleGuard routes to /battle/:id.
        useMatchmakingStore.getState().setBattleTransitioning(true);
        usePlayerStore.getState().setQueueStatus({
          status: 'Matched',
          matchId: response.matchId,
          battleId: response.battleId,
          matchState: 'BattleCreated',
        });
      } else {
        useMatchmakingStore.getState().setIdle();
        usePlayerStore.getState().setQueueStatus({
          status: 'Idle',
          matchId: null,
          battleId: null,
          matchState: null,
        });
      }
    } catch (err: unknown) {
      // Refetch the authoritative queue state so the UI doesn't desync from
      // the server, then rethrow so the caller (LobbyScreen) can surface the
      // failure as an inline banner — matching the joinQueue convention and
      // the rest of the mutation error pattern (SERVER-08).
      await queryClient.invalidateQueries({ queryKey: gameKeys.state() });
      throw err;
    }
  }, [queryClient]);

  return {
    status,
    searchStartedAt,
    consecutiveFailures,
    joinQueue,
    leaveQueue,
  };
}

/**
 * Starts/stops the polling lifecycle based on the derived UI status.
 * Must be called in the SearchingScreen.
 *
 * Also restarts the UI-local search timer when reached via page refresh —
 * `playerStore.queueStatus` is restored by `GameStateLoader`, but the
 * matchmaking store resets on reload so `searchStartedAt` needs re-seeding.
 */
export function useMatchmakingPolling(): void {
  const queueStatus = usePlayerStore((s) => s.queueStatus);
  const battleTransitioning = useMatchmakingStore((s) => s.battleTransitioning);
  const searchStartedAt = useMatchmakingStore((s) => s.searchStartedAt);

  const status = deriveQueueUiStatus(queueStatus, battleTransitioning);

  // Re-seed the UI-local timer after a page refresh that lands us on the
  // searching screen. Only fires when the UI-local state is empty and the
  // authoritative queue is active.
  useEffect(() => {
    if (searchStartedAt !== null) return;
    if (!queueStatus) return;
    if (
      queueStatus.status === 'Searching' ||
      (queueStatus.status === 'Matched' && !queueStatus.battleId)
    ) {
      useMatchmakingStore.getState().startSearch();
    }
  }, [searchStartedAt, queueStatus]);

  useEffect(() => {
    if (status !== 'searching' && status !== 'matched') {
      matchmakingPoller.stop();
      return;
    }

    const handleResult = (response: QueueStatusResponse) => {
      useMatchmakingStore.getState().resetFailures();
      // Flag the transition BEFORE the authoritative write so derived
      // status observers see "battleTransition" consistently even in the
      // brief moment React batches the two updates.
      if (response.status === 'Matched' && response.battleId) {
        useMatchmakingStore.getState().setBattleTransitioning(true);
      }
      usePlayerStore.getState().setQueueStatus(response);
    };

    const handleError = () => {
      useMatchmakingStore.getState().incrementFailures();
    };

    matchmakingPoller.start(POLL_INTERVAL_MS, handleResult, handleError);

    return () => {
      matchmakingPoller.stop();
    };
  }, [status]);
}

/**
 * Drives the heartbeat-based queue presence. While the derived UI status is
 * `searching`, fires `POST /queue/heartbeat` every {@link HEARTBEAT_INTERVAL_MS}
 * so the server's 15s presence TTL stays alive. Also installs a `pagehide`
 * listener that flushes a `keepalive` `/queue/leave` so an Alt+F4 / tab-close
 * cleans up immediately rather than waiting for the sweep worker (~35s).
 *
 * Failures are swallowed deliberately — the heartbeat is best-effort and the
 * sweep worker is the authoritative safety net. Spamming retries would burn
 * the request budget and double-write logs without buying anything.
 */
export function useQueueHeartbeat(): void {
  const queueStatus = usePlayerStore((s) => s.queueStatus);
  const battleTransitioning = useMatchmakingStore((s) => s.battleTransitioning);
  const status = deriveQueueUiStatus(queueStatus, battleTransitioning);

  useEffect(() => {
    if (status !== 'searching') return;

    const connectionRef = getQueueConnectionRef();

    // Fire one immediately so the server sees the tab right away even if the
    // user joined a fraction of a second after the previous tick. Subsequent
    // ticks run at HEARTBEAT_INTERVAL_MS.
    queueApi.heartbeat(connectionRef).catch(() => {});

    const intervalId = setInterval(() => {
      queueApi.heartbeat(connectionRef).catch(() => {});
    }, HEARTBEAT_INTERVAL_MS);

    // pagehide is more reliable than beforeunload (covers mobile Safari /
    // bfcache eviction / tab discard). Both fire at most once per session;
    // browsers de-dup keepalive requests with the same body if they
    // happen to fire close together.
    const handlePageHide = () => {
      queueApi.leaveBeacon(connectionRef);
    };
    window.addEventListener('pagehide', handlePageHide);
    window.addEventListener('beforeunload', handlePageHide);

    return () => {
      clearInterval(intervalId);
      window.removeEventListener('pagehide', handlePageHide);
      window.removeEventListener('beforeunload', handlePageHide);
    };
  }, [status]);
}

// ---------------------------------------------------------------------------
// Post-battle re-queue
// ---------------------------------------------------------------------------

export type RequeueState = 'idle' | 'pending' | 'failed';

export interface RequeueController {
  state: RequeueState;
  /** Trigger the post-battle requeue+navigate flow. Idempotent; ignored if pending. */
  requeue: () => Promise<void>;
}

/**
 * Post-battle re-queue handler used by the BattleResultScreen's primary
 * CTA. Sequence:
 *
 *   1. `returnFromBattle(battleId)` — atomic handoff that sets
 *      dismissedBattleId, clears local queueStatus, and flags the lobby
 *      post-battle XP refresh. Identical to the "Return to Lobby" path so
 *      `BattleShell` unmount + `useBattleStore.reset()` (which preserves
 *      lastBattleLog/lastTurnHistory — store.ts:435-442) behave the same
 *      whether the user requeues or returns.
 *   2. `queueApi.join` with bounded retry. The Matchmaking service can
 *      respond `Matched` with the just-finished battleId for ~1–2s after
 *      BattleEnded while it projects the BattleCompleted clear. We retry
 *      at 400/800/1200ms (up to 3 attempts, ~2.4s total) checking each
 *      response body for the stale-match shape.
 *   3. On `Searching` — write Searching, navigate to /lobby; the
 *      lobby's existing matchmaking observers take over from there.
 *   4. On `Matched` with a battleId different from the dismissed one —
 *      that's a real new match. Honor it; BattleGuard will route the user
 *      to /battle/:id from the lobby.
 *   5. On retries exhausted while still Matched on the dismissed battle —
 *      flip `requeueFailed`, navigate to /lobby; the lobby queue card
 *      surfaces a small "Couldn't auto-requeue" notice.
 */
export function useRequeueAfterBattle(): RequeueController {
  const navigate = useNavigate();
  const requeueFailed = useMatchmakingStore((s) => s.requeueFailed);
  // Local pending tracks just this hook's own request lifecycle. Sourcing it
  // from the store's `battleTransitioning` would inherit a stale `true` left
  // behind by the lobby→battle transition (the poller set it when it saw
  // Matched, and `useMatchmaking`'s reset effect only runs on lobby/searching
  // screens — neither is mounted during the battle or result screen), which
  // made the result screen's primary CTA boot stuck on "Preparing…".
  const [pending, setPending] = useState(false);

  const requeue = useCallback(async () => {
    if (pending) return;

    const battleId = useBattleStore.getState().battleId;
    if (!battleId) {
      navigate('/lobby');
      return;
    }

    setPending(true);
    useMatchmakingStore.getState().setRequeueFailed(false);

    // Atomic handoff (sets dismissedBattleId for the suppression in
    // setGameState, clears queueStatus, flags post-battle refresh). Mirrors
    // the "Return to Lobby" path exactly so survivability of
    // lastBattleLog/lastTurnHistory is unchanged.
    usePlayerStore.getState().returnFromBattle(battleId);

    const outcome = await executeRequeueLoop({
      battleId,
      joinFn: queueApi.join,
      connectionRef: getQueueConnectionRef(),
      delays: REQUEUE_RETRY_DELAYS_MS,
      sleep: (ms) => new Promise<void>((resolve) => setTimeout(resolve, ms)),
    });

    switch (outcome.kind) {
      case 'searching':
        useMatchmakingStore.getState().startSearch();
        usePlayerStore.getState().setQueueStatus({
          status: 'Searching',
          matchId: null,
          battleId: null,
          matchState: null,
        });
        break;
      case 'newMatch':
        // BattleGuard routes from queueStatus.Matched + battleId; the
        // transitioning flag drives the lobby queue card UI for the brief
        // redirect window after navigate('/lobby').
        useMatchmakingStore.getState().setBattleTransitioning(true);
        usePlayerStore.getState().setQueueStatus(outcome.queueStatus);
        break;
      case 'failed':
        useMatchmakingStore.getState().setRequeueFailed(true);
        break;
    }

    navigate('/lobby');
  }, [navigate, pending]);

  const state: RequeueState = requeueFailed ? 'failed' : pending ? 'pending' : 'idle';

  return { state, requeue };
}
