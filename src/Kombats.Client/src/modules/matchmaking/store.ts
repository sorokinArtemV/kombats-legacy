import { create } from 'zustand';

// Matchmaking owns ONLY UI-local concerns. The authoritative queue state
// lives in `usePlayerStore.queueStatus` (populated by GameStateLoader, the
// join/leave mutations, and the polling callback). Keeping a second mirror
// in this store was the source of the dual-source-of-truth bug (F-A3) that
// repeatedly produced matched/searching redirect loops — the fix is to stop
// mirroring.
//
// Fields that remain here are strictly UI-derived:
//   - searchStartedAt      → SearchingScreen's elapsed-time counter
//   - consecutiveFailures  → SearchingScreen's "connection issues" hint
//   - battleTransitioning  → short-lived UI flag set when the hook has seen
//                            an authoritative battleId but navigation has
//                            not yet happened. Lets SearchingScreen show
//                            "Entering battle…" before BattleGuard
//                            redirects (and across the single render gap
//                            between leaveQueue returning a battleId and
//                            the queue store write that follows it).

interface MatchmakingState {
  searchStartedAt: number | null;
  consecutiveFailures: number;
  battleTransitioning: boolean;

  /**
   * Set true when the bounded-retry post-battle re-queue (see
   * `useRequeueAfterBattle`) gives up before the backend confirms the
   * previous battle has been projected as completed. Cleared automatically
   * by `useMatchmaking`'s authoritative-queue-inactive observer (alongside
   * the rest of the UI-local state) the moment the queue store transitions
   * back to idle, and explicitly by the lobby's queue card after the user
   * has acknowledged the notice (or after a successful subsequent join).
   */
  requeueFailed: boolean;

  /** Begin a new search — UI timer starts, failure counter resets. */
  startSearch: () => void;

  /** Reset UI-local state to its empty shape. Used by session-cleanup and
   * by `useMatchmaking` when it observes the authoritative queue returning
   * to idle. */
  setIdle: () => void;

  setBattleTransitioning: (v: boolean) => void;
  setRequeueFailed: (v: boolean) => void;
  incrementFailures: () => void;
  resetFailures: () => void;
}

export const useMatchmakingStore = create<MatchmakingState>()((set) => ({
  searchStartedAt: null,
  consecutiveFailures: 0,
  battleTransitioning: false,
  requeueFailed: false,

  startSearch: () =>
    // requeueFailed is intentionally NOT cleared here — the BattleResultScreen's
    // re-queue path sets it when retries fail and then navigates to /lobby.
    // If `useMatchmaking` happened to call this in the same frame the flag
    // would be wiped before the lobby queue card observed it. Cleared
    // explicitly when the user dismisses the notice / re-queues successfully
    // / leaves the queue (see setRequeueFailed call sites).
    set({
      searchStartedAt: Date.now(),
      consecutiveFailures: 0,
      battleTransitioning: false,
    }),

  setIdle: () =>
    // See note on startSearch — requeueFailed survives this transition by
    // design.
    set({
      searchStartedAt: null,
      consecutiveFailures: 0,
      battleTransitioning: false,
    }),

  setBattleTransitioning: (v) => set({ battleTransitioning: v }),

  setRequeueFailed: (v) => set({ requeueFailed: v }),

  incrementFailures: () => set((state) => ({ consecutiveFailures: state.consecutiveFailures + 1 })),

  resetFailures: () => set({ consecutiveFailures: 0 }),
}));
