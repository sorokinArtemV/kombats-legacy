import { describe, it, expect, beforeEach } from 'vitest';
import { usePlayerStore } from './store';
import { decideBattleGuard } from '@/app/guards/guard-decisions';
import { deriveQueueUiStatus } from '@/modules/matchmaking/queue-ui-status';
import type { GameStateResponse } from '@/types/api';

// Store-level integration test for the post-battle → lobby sequence.
//
// This pins the interaction between:
//   - `returnFromBattle(battleId)` atomic handoff (set in one `set()`)
//   - `setGameState` suppression of stale `queueStatus.Matched.<battleId>`
//   - `decideBattleGuard` routing decisions that observe those transitions
//   - `deriveQueueUiStatus` UI projection from the single source of truth
//
// The trip these tests cover is the one that repeatedly broke before S2
// and required fix commits: battle ends → BattleResultScreen fires the
// handoff → GameStateLoader refetches → server still reports the matched
// battle for a tick → guard must NOT bounce the player back into it.

const CHARACTER = {
  identityId: 'id-1',
  displayName: 'Test',
  level: 1,
  xp: 0,
  strength: 10,
  agility: 10,
  intuition: 10,
  vitality: 10,
  unspentStatPoints: 0,
  onboardingState: 'Ready' as const,
  stateRevision: 1,
};

function buildResponse(overrides: Partial<GameStateResponse> = {}): GameStateResponse {
  return {
    character: CHARACTER,
    queueStatus: null,
    isCharacterCreated: true,
    degradedServices: null,
    ...overrides,
  } as GameStateResponse;
}

describe('Post-battle handoff integration', () => {
  beforeEach(() => {
    usePlayerStore.getState().clearState();
  });

  it('returnFromBattle writes the three handoff fields atomically', () => {
    const store = usePlayerStore.getState();
    // Seed the pre-handoff state as it would appear on the result screen:
    // queueStatus still reports the matched battle.
    store.setGameState(
      buildResponse({
        queueStatus: {
          status: 'Matched',
          matchId: 'm1',
          battleId: 'b1',
          matchState: 'BattleCreated',
        },
      }),
    );

    store.returnFromBattle('b1');

    const after = usePlayerStore.getState();
    expect(after.queueStatus).toBeNull();
    expect(after.dismissedBattleId).toBe('b1');
    expect(after.postBattleRefreshNeeded).toBe(true);
  });

  it('suppresses a stale refetch that still returns Matched.<sameBattleId>', () => {
    // 1) Player clicks "Return to Lobby"
    usePlayerStore.getState().returnFromBattle('b1');

    // 2) usePostBattleRefresh triggers a refetch. The backend projection
    //    has not yet cleared, so the response still contains the match.
    usePlayerStore.getState().setGameState(
      buildResponse({
        queueStatus: {
          status: 'Matched',
          matchId: 'm1',
          battleId: 'b1',
          matchState: 'BattleCreated',
        },
      }),
    );

    // 3) queueStatus stays null — the dismissed-battle marker suppressed it.
    const after = usePlayerStore.getState();
    expect(after.queueStatus).toBeNull();
    expect(after.dismissedBattleId).toBe('b1');
  });

  it('accepts a refetch once the server has moved past the dismissed battle', () => {
    usePlayerStore.getState().returnFromBattle('b1');

    // Second refetch — backend projection has now caught up and reports no
    // active queue.
    usePlayerStore.getState().setGameState(buildResponse({ queueStatus: null }));

    const after = usePlayerStore.getState();
    expect(after.queueStatus).toBeNull();
    expect(after.dismissedBattleId).toBeNull();
  });

  it('lets the player re-queue immediately after dismissing the result', () => {
    usePlayerStore.getState().returnFromBattle('b1');

    // Simulate the join-queue success path: queueStatus flips to Searching
    // via the optimistic write in useMatchmaking.joinQueue.
    usePlayerStore.getState().setQueueStatus({
      status: 'Searching',
      matchId: null,
      battleId: null,
      matchState: null,
    });

    // The dismissedBattleId marker itself isn't cleared by setQueueStatus,
    // but the next setGameState (from the refetch that joinQueue may
    // trigger) will clear it once the server confirms a different state.
    const after = usePlayerStore.getState();
    expect(after.queueStatus?.status).toBe('Searching');
  });

  it('does not bounce the player back onto /battle/:id while the guard observes Ended + stale Matched', () => {
    // Simulate the moment between the atomic handoff and the next refetch:
    //   - battle store: phase 'Ended', battleId 'b1'
    //   - player store: queueStatus null (cleared by returnFromBattle)
    //   - user is about to be navigated to /lobby by BattleResultScreen
    usePlayerStore.getState().returnFromBattle('b1');

    const decision = decideBattleGuard(
      usePlayerStore.getState().queueStatus,
      'Ended',
      'b1',
      '/battle/b1/result',
    );
    // The result route must still be reachable for the dismissal UX.
    expect(decision).toEqual({ type: 'allow' });
  });

  it('guards /lobby through even if server refetch replays the stale match', () => {
    usePlayerStore.getState().returnFromBattle('b1');
    // Stale refetch comes in — suppressed.
    usePlayerStore.getState().setGameState(
      buildResponse({
        queueStatus: {
          status: 'Matched',
          matchId: 'm1',
          battleId: 'b1',
          matchState: 'BattleCreated',
        },
      }),
    );

    // queueStatus is null after suppression, so the lobby route is allowed.
    const decision = decideBattleGuard(
      usePlayerStore.getState().queueStatus,
      'Ended',
      'b1',
      '/lobby',
    );
    expect(decision).toEqual({ type: 'allow' });
  });

  it('UI projection shows idle after the handoff + stale-refetch suppression', () => {
    usePlayerStore.getState().returnFromBattle('b1');
    usePlayerStore.getState().setGameState(
      buildResponse({
        queueStatus: {
          status: 'Matched',
          matchId: 'm1',
          battleId: 'b1',
          matchState: 'BattleCreated',
        },
      }),
    );

    const ui = deriveQueueUiStatus(
      usePlayerStore.getState().queueStatus,
      /* battleTransitioning */ false,
    );
    expect(ui).toBe('idle');
  });
});
