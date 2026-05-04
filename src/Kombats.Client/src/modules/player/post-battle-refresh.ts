import { useEffect, useRef } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { gameKeys } from '@/app/query-client';
import { usePlayerStore } from './store';
import {
  comparePlayerProgress,
  snapshotCharacter,
  type PlayerProgressSnapshot,
} from './player-progress';
import type { GameStateResponse } from '@/types/api';

// DEC-5: if the first post-battle game-state refetch returns unchanged
// XP/level/unspent-points, retry once after this delay before accepting
// the state.
const RETRY_DELAY_MS = 3000;

function readSnapshotFromCache(
  queryClient: ReturnType<typeof useQueryClient>,
): PlayerProgressSnapshot | null {
  const data = queryClient.getQueryData<GameStateResponse>(gameKeys.state());
  return snapshotCharacter(data?.character);
}

/**
 * Post-battle XP/level refresh (DEC-5).
 *
 * Mounted from the lobby. When `postBattleRefreshNeeded` is set, this hook:
 * 1. Snapshots the current character's xp/level/unspent-points.
 * 2. Refetches `GET /api/v1/game/state`.
 * 3. Reads the fresh result directly from the query cache for parity with
 *    the refetch path (Zustand is also updated inside `useGameState`'s
 *    queryFn, so either source is now consistent; cache reads avoid an
 *    extra subscription here).
 * 4. If nothing changed, waits {@link RETRY_DELAY_MS} and refetches once more.
 * 5. If a level-up is detected, records the new level on the player store so
 *    the lobby banner can surface it.
 *
 * The flag is consumed up-front to guarantee a single execution per trip
 * back to the lobby.
 */
export function usePostBattleRefresh(): void {
  const queryClient = useQueryClient();
  const needed = usePlayerStore((s) => s.postBattleRefreshNeeded);
  const ranRef = useRef(false);

  useEffect(() => {
    if (!needed || ranRef.current) return;
    ranRef.current = true;

    // Consume the flag up-front so a re-render/remount does not re-enter.
    usePlayerStore.getState().setPostBattleRefreshNeeded(false);

    let cancelled = false;
    let finished = false;

    const run = async () => {
      const pre = snapshotCharacter(usePlayerStore.getState().character);

      await queryClient.refetchQueries({ queryKey: gameKeys.state(), exact: true });
      if (cancelled) return;

      let post = readSnapshotFromCache(queryClient);
      let delta = comparePlayerProgress(pre, post);

      if (!delta.changed && pre !== null && post !== null) {
        await new Promise((resolve) => setTimeout(resolve, RETRY_DELAY_MS));
        if (cancelled) return;
        await queryClient.refetchQueries({ queryKey: gameKeys.state(), exact: true });
        if (cancelled) return;
        post = readSnapshotFromCache(queryClient);
        delta = comparePlayerProgress(pre, post);
      }

      if (delta.leveledUp && delta.newLevel !== null) {
        usePlayerStore.getState().setPendingLevelUpLevel(delta.newLevel);
      }
      finished = true;
    };

    run()
      .catch(() => {
        // Refetch errors are surfaced by the existing GameStateLoader error UI;
        // don't double-handle here. Flag already cleared above.
      })
      .finally(() => {
        finished = true;
      });

    return () => {
      cancelled = true;
      // If we unmount mid-flight (typically: user bounces lobby → result →
      // lobby during the 3s retry sleep) the up-front flag-consume above has
      // already burned the only signal that triggers this hook. Re-arm so the
      // next lobby mount runs the full XP/level reconciliation again instead
      // of silently skipping the level-up banner.
      if (!finished) {
        usePlayerStore.getState().setPostBattleRefreshNeeded(true);
      }
    };
  }, [needed, queryClient]);
}
