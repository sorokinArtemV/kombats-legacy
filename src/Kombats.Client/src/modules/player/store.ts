import { create } from 'zustand';
import type { CharacterResponse, QueueStatusResponse, GameStateResponse } from '@/types/api';

interface PlayerState {
  character: CharacterResponse | null;
  queueStatus: QueueStatusResponse | null;
  isCharacterCreated: boolean;
  degradedServices: string[] | null;
  isLoaded: boolean;

  /**
   * Set when the player finishes a battle and is returning to the lobby.
   * Consumed by `usePostBattleRefresh` on the next lobby mount to trigger
   * the DEC-5 XP/level re-fetch sequence. Non-persistent; in-memory only.
   */
  postBattleRefreshNeeded: boolean;

  /**
   * Level reached on the most recent post-battle refresh where a level-up
   * was detected. Surfaced by the lobby level-up banner; cleared when the
   * player dismisses the banner or allocates stats.
   */
  pendingLevelUpLevel: number | null;

  /**
   * BattleId the player explicitly dismissed via "Return to Lobby". Kept
   * here rather than in the battle store because the battle store is fully
   * reset when the battle shell unmounts. The backend's `BattleCompleted`
   * projection is eventually consistent, so `GET /game/state` can still
   * return `queueStatus.Matched.<sameBattleId>` for a short window after
   * the player leaves. Without this marker, the post-lobby refetch
   * overwrites the optimistic `setQueueStatus(null)` and `BattleGuard`
   * bounces the player back to `/battle/:id` → result screen → lobby in
   * a visible loop.
   *
   * Cleared automatically when `setGameState` observes a different
   * battleId in the fresh queue status (backend has moved on).
   */
  dismissedBattleId: string | null;

  setGameState: (response: GameStateResponse) => void;
  setQueueStatus: (queueStatus: QueueStatusResponse | null) => void;
  updateCharacter: (character: CharacterResponse) => void;
  setPostBattleRefreshNeeded: (needed: boolean) => void;
  setPendingLevelUpLevel: (level: number | null) => void;
  setDismissedBattleId: (battleId: string | null) => void;

  /**
   * Single atomic post-battle handoff. Consolidates the three writes
   * previously scattered in `BattleResultScreen` (dismissedBattleId +
   * setQueueStatus(null) + postBattleRefreshNeeded=true) into one `set()`
   * so the BattleGuard + usePostBattleRefresh observers always see a
   * consistent intermediate state.
   */
  returnFromBattle: (battleId: string) => void;

  clearState: () => void;
}

export const usePlayerStore = create<PlayerState>()((set, get) => ({
  character: null,
  queueStatus: null,
  isCharacterCreated: false,
  degradedServices: null,
  isLoaded: false,
  postBattleRefreshNeeded: false,
  pendingLevelUpLevel: null,
  dismissedBattleId: null,

  setGameState: (response) => {
    const dismissed = get().dismissedBattleId;
    const incoming = response.queueStatus;
    // If the server's queue snapshot is still the battle the player already
    // dismissed, suppress it — the backend has not yet projected the
    // `BattleCompleted` clear. Clear the marker once the backend has moved
    // to a different battleId or no queue entry at all.
    const suppressQueue =
      dismissed !== null && incoming?.status === 'Matched' && incoming.battleId === dismissed;
    const nextDismissed = suppressQueue ? dismissed : null;
    set({
      character: response.character,
      queueStatus: suppressQueue ? null : incoming,
      isCharacterCreated: response.isCharacterCreated,
      degradedServices: response.degradedServices,
      isLoaded: true,
      dismissedBattleId: nextDismissed,
    });
  },

  setQueueStatus: (queueStatus) => set({ queueStatus }),

  updateCharacter: (character) => set({ character }),

  setPostBattleRefreshNeeded: (needed) => set({ postBattleRefreshNeeded: needed }),

  setPendingLevelUpLevel: (level) => set({ pendingLevelUpLevel: level }),

  setDismissedBattleId: (battleId) => set({ dismissedBattleId: battleId }),

  returnFromBattle: (battleId) =>
    set({
      dismissedBattleId: battleId,
      queueStatus: null,
      postBattleRefreshNeeded: true,
    }),

  clearState: () =>
    set({
      character: null,
      queueStatus: null,
      isCharacterCreated: false,
      degradedServices: null,
      isLoaded: false,
      postBattleRefreshNeeded: false,
      pendingLevelUpLevel: null,
      dismissedBattleId: null,
    }),
}));
