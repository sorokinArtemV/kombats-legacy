import { useQuery } from '@tanstack/react-query';
import { gameKeys, playerKeys } from '@/app/query-client';
import * as gameApi from '@/transport/http/endpoints/game';
import * as playersApi from '@/transport/http/endpoints/players';
import { usePlayerStore } from './store';

// Single canonical staleTime for every consumer of `playerKeys.card`. Changing
// it changes cache freshness in every place a card is rendered (FighterNameplate
// own card, BattleScreen fighter cards, DM panel cards), so keep the constant
// here and reuse it from inline-`useQuery` sites that share the same key.
export const PLAYER_CARD_STALE_TIME_MS = 30_000;

export function useGameState() {
  return useQuery({
    queryKey: gameKeys.state(),
    queryFn: async () => {
      const data = await gameApi.getState();
      usePlayerStore.getState().setGameState(data);
      return data;
    },
    // Explicit invalidations (mutations, post-battle refresh)
    // drive freshness; tab focus contributes nothing useful.
    staleTime: 5_000,
    refetchOnWindowFocus: false,
  });
}

export function usePlayerCard(playerId: string, enabled: boolean = true) {
  return useQuery({
    queryKey: playerKeys.card(playerId),
    queryFn: () => playersApi.getCard(playerId),
    enabled: enabled && !!playerId,
    staleTime: PLAYER_CARD_STALE_TIME_MS,
  });
}
