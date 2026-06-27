import { describe, it, expect, beforeEach } from 'vitest';
import { usePlayerStore } from './store';
import type { GameStateResponse } from '@/types/api';

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
  onboardingState: 'Completed' as const,
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

describe('usePlayerStore — dismissedBattleId suppression', () => {
  beforeEach(() => {
    usePlayerStore.getState().clearState();
  });

  it('suppresses queueStatus.Matched when it points at the dismissed battle', () => {
    usePlayerStore.getState().setDismissedBattleId('battle-X');

    usePlayerStore.getState().setGameState(
      buildResponse({
        queueStatus: {
          status: 'Matched',
          matchId: 'match-1',
          battleId: 'battle-X',
          matchState: 'BattleCreated',
        },
      }),
    );

    expect(usePlayerStore.getState().queueStatus).toBeNull();
    // Marker retained so subsequent stale refetches stay suppressed.
    expect(usePlayerStore.getState().dismissedBattleId).toBe('battle-X');
  });

  it('clears the dismissed marker once the server moves to a different battle', () => {
    usePlayerStore.getState().setDismissedBattleId('battle-X');

    usePlayerStore.getState().setGameState(
      buildResponse({
        queueStatus: {
          status: 'Matched',
          matchId: 'match-2',
          battleId: 'battle-Y',
          matchState: 'BattleCreated',
        },
      }),
    );

    expect(usePlayerStore.getState().queueStatus?.battleId).toBe('battle-Y');
    expect(usePlayerStore.getState().dismissedBattleId).toBeNull();
  });

  it('clears the dismissed marker when the server reports no active queue', () => {
    usePlayerStore.getState().setDismissedBattleId('battle-X');

    usePlayerStore.getState().setGameState(buildResponse({ queueStatus: null }));

    expect(usePlayerStore.getState().queueStatus).toBeNull();
    expect(usePlayerStore.getState().dismissedBattleId).toBeNull();
  });

  it('clears the dismissed marker when the server reports Searching (player re-queued)', () => {
    usePlayerStore.getState().setDismissedBattleId('battle-X');

    usePlayerStore.getState().setGameState(
      buildResponse({
        queueStatus: {
          status: 'Searching',
          matchId: null,
          battleId: null,
          matchState: null,
        },
      }),
    );

    expect(usePlayerStore.getState().queueStatus?.status).toBe('Searching');
    expect(usePlayerStore.getState().dismissedBattleId).toBeNull();
  });

  it('applies queueStatus normally when no marker is set', () => {
    usePlayerStore.getState().setGameState(
      buildResponse({
        queueStatus: {
          status: 'Matched',
          matchId: 'match-1',
          battleId: 'battle-X',
          matchState: 'BattleCreated',
        },
      }),
    );

    expect(usePlayerStore.getState().queueStatus?.battleId).toBe('battle-X');
    expect(usePlayerStore.getState().dismissedBattleId).toBeNull();
  });
});
