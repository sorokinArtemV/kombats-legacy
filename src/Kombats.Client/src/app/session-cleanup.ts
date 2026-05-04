import { queryClient } from './query-client';
import { battleHubManager, chatHubManager } from './transport-init';
import { useAuthStore } from '@/modules/auth/store';
import { usePlayerStore } from '@/modules/player/store';
import { useBattleStore } from '@/modules/battle/store';
import { useChatStore } from '@/modules/chat/store';
import { useMatchmakingStore } from '@/modules/matchmaking/store';
import { getQueueConnectionRef } from '@/modules/matchmaking/connection-ref';
import * as queueApi from '@/transport/http/endpoints/queue';

/**
 * Tear down all session-scoped client state before a user switch or logout.
 *
 * Cleans up:
 *   - live SignalR connections (battle + chat)
 *   - every module Zustand store (auth/player/battle/chat/matchmaking)
 *   - every TanStack Query cache entry (cancels in-flight requests and
 *     resets cache state so the next user does not read the previous user's
 *     game state / player cards / chat history)
 *
 * Called from the auth `logout()` flow. Safe to call more than once — each
 * store's clear action is idempotent and `disconnect()` handles "already
 * disconnected" internally.
 */
export async function clearSessionState(): Promise<void> {
  // If the user is logging out mid-search, drop the queue entry server-side
  // so they don't get matched into a battle they'll never join. Must run
  // BEFORE clearAuth() — the leave call needs the live access token. The
  // heartbeat-based sweep is the safety net if this fails or the session is
  // already torn down. Best-effort: ignore failures and continue.
  const queueStatus = usePlayerStore.getState().queueStatus;
  if (queueStatus?.status === 'Searching') {
    try {
      await queueApi.leave(getQueueConnectionRef());
    } catch {
      // Network failure / already-matched — sweep worker handles it.
    }
  }

  // Disconnect transports first so any late event handlers do not repopulate
  // stores we are about to clear. These are best-effort — ignore failures.
  await Promise.allSettled([battleHubManager.disconnect(), chatHubManager.disconnect()]);

  // Cancel in-flight queries and wipe cached data. Cancel first so
  // resolving queries do not write into the cache we just cleared.
  await queryClient.cancelQueries();
  queryClient.removeQueries();

  // Reset module stores. Order doesn't matter — none of them cross-write.
  useBattleStore.getState().reset();
  useChatStore.getState().clearStore();
  useMatchmakingStore.getState().setIdle();
  usePlayerStore.getState().clearState();
  useAuthStore.getState().clearAuth();
}
