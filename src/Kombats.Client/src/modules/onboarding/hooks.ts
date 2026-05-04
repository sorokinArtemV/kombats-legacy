import { useEffect, useRef, useCallback } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { gameKeys } from '@/app/query-client';
import * as gameApi from '@/transport/http/endpoints/game';
import { usePlayerStore } from '@/modules/player/store';

// Bounded-retry timing for the user-driven onboarding retry. Three attempts
// at 400/800/1200ms backoff (~2.4s total) mirror the post-battle requeue
// pattern in matchmaking/requeue-loop.ts — same intent: prevent the user
// from hammering Retry while the BFF is unhealthy without giving up too
// quickly. Beyond three attempts the button no-ops; a page refresh is the
// established escape hatch for terminal failure.
const RETRY_DELAYS_MS = [400, 800, 1200] as const;

/**
 * Automatically calls POST /api/v1/game/onboard when game state has loaded
 * but no character exists. The call is idempotent. On success, invalidates
 * the game state query so GameStateLoader re-fetches and the player store
 * gets the new Draft character.
 */
export function useAutoOnboard() {
  const isLoaded = usePlayerStore((s) => s.isLoaded);
  const isCharacterCreated = usePlayerStore((s) => s.isCharacterCreated);
  const queryClient = useQueryClient();
  const attemptedRef = useRef(false);
  const retryCountRef = useRef(0);
  const retryTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  const { mutate, isPending, isError, error, reset } = useMutation({
    mutationFn: gameApi.onboard,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: gameKeys.state() });
    },
  });

  useEffect(() => {
    if (!isLoaded) return;
    if (isCharacterCreated) return;
    if (attemptedRef.current) return;
    if (isPending) return;

    attemptedRef.current = true;
    mutate();
  }, [isLoaded, isCharacterCreated, isPending, mutate]);

  // Clear any pending retry timer on unmount so a delayed mutate() does not
  // fire after the host (GameStateLoader) is gone.
  useEffect(() => {
    return () => {
      if (retryTimerRef.current !== null) {
        clearTimeout(retryTimerRef.current);
        retryTimerRef.current = null;
      }
    };
  }, []);

  const retry = useCallback(() => {
    // In-flight guard — repeat clicks while a retry is already scheduled
    // are silently absorbed.
    if (retryTimerRef.current !== null) return;
    // Bounded retry — once exhausted the call no-ops. The error UI keeps
    // showing; reload is the escape hatch.
    if (retryCountRef.current >= RETRY_DELAYS_MS.length) return;

    const delay = RETRY_DELAYS_MS[retryCountRef.current];
    retryCountRef.current += 1;

    retryTimerRef.current = setTimeout(() => {
      retryTimerRef.current = null;
      reset();
      attemptedRef.current = false;
      mutate();
    }, delay);
  }, [mutate, reset]);

  return { isPending, isError, error, retry };
}
