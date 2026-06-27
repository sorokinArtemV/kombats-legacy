import { describe, it, expect, beforeEach } from 'vitest';
import { useBattleStore } from './store';
import type { BattleZone } from '@/types/battle';

function resetStore() {
  useBattleStore.getState().reset();
  // reset() now intentionally preserves lastBattleLog and lastTurnHistory
  // (so the BattleLog tab can survive lobby return). For test isolation,
  // also wipe both.
  useBattleStore.getState().clearLastBattleLog();
  useBattleStore.getState().clearLastTurnHistory();
}

function getState() {
  return useBattleStore.getState();
}

describe('Battle store state machine', () => {
  beforeEach(resetStore);

  it('starts in Idle phase', () => {
    expect(getState().phase).toBe('Idle');
  });

  describe('TurnOpen -> Submitted', () => {
    beforeEach(() => {
      // Advance to TurnOpen
      getState().startBattle('battle-1');
      getState().handleConnected();
      getState().handleTurnOpened({
        battleId: 'battle-1',
        turnIndex: 1,
        deadlineUtc: '2026-01-01T00:00:30Z',
      });
    });

    it('transitions to TurnOpen', () => {
      expect(getState().phase).toBe('TurnOpen');
      expect(getState().turnIndex).toBe(1);
    });

    it('transitions to Submitted when setSubmitting(true)', () => {
      getState().selectAttackZone('Head' as BattleZone);
      getState().selectBlockPair(['Chest', 'Belly'] as [BattleZone, BattleZone]);
      getState().setSubmitting(true);

      expect(getState().phase).toBe('Submitted');
      expect(getState().isSubmitting).toBe(true);
    });
  });

  describe('submit failure recovery', () => {
    beforeEach(() => {
      getState().startBattle('battle-1');
      getState().handleConnected();
      getState().handleTurnOpened({
        battleId: 'battle-1',
        turnIndex: 1,
        deadlineUtc: '2026-01-01T00:00:30Z',
      });
      getState().selectAttackZone('Head' as BattleZone);
      getState().selectBlockPair(['Chest', 'Belly'] as [BattleZone, BattleZone]);
      getState().setSubmitting(true);
    });

    it('recovers to TurnOpen on setSubmitting(false)', () => {
      expect(getState().phase).toBe('Submitted');

      getState().setSubmitting(false);

      expect(getState().phase).toBe('TurnOpen');
      expect(getState().isSubmitting).toBe(false);
    });

    it('preserves selections after submit failure recovery', () => {
      getState().setSubmitting(false);

      expect(getState().selectedAttackZone).toBe('Head');
      expect(getState().selectedBlockPair).toEqual(['Chest', 'Belly']);
    });

    it('does not stomp Resolving phase on late submit failure', () => {
      // Simulate: submit sent, server resolves before client gets invoke error
      getState().handleTurnResolved({
        battleId: 'battle-1',
        turnIndex: 1,
        playerAAction: '{}',
        playerBAction: '{}',
        log: null,
      });
      expect(getState().phase).toBe('Resolving');

      // Late invoke failure arrives — should NOT revert to TurnOpen
      getState().setSubmitting(false);
      expect(getState().phase).toBe('Resolving');
      expect(getState().isSubmitting).toBe(false);
    });
  });

  describe('Resolving -> TurnOpen (next turn)', () => {
    it('transitions through resolving to next turn', () => {
      getState().startBattle('battle-1');
      getState().handleConnected();
      getState().handleTurnOpened({
        battleId: 'battle-1',
        turnIndex: 1,
        deadlineUtc: '2026-01-01T00:00:30Z',
      });

      getState().handleTurnResolved({
        battleId: 'battle-1',
        turnIndex: 1,
        playerAAction: '{}',
        playerBAction: '{}',
        log: null,
      });
      expect(getState().phase).toBe('Resolving');

      getState().handleTurnOpened({
        battleId: 'battle-1',
        turnIndex: 2,
        deadlineUtc: '2026-01-01T00:01:00Z',
      });
      expect(getState().phase).toBe('TurnOpen');
      expect(getState().turnIndex).toBe(2);
      expect(getState().selectedAttackZone).toBeNull();
      expect(getState().selectedBlockPair).toBeNull();
    });
  });

  describe('Resolving -> Ended', () => {
    it('transitions to Ended via handleBattleEnded', () => {
      getState().startBattle('battle-1');
      getState().handleConnected();
      getState().handleTurnOpened({
        battleId: 'battle-1',
        turnIndex: 1,
        deadlineUtc: '2026-01-01T00:00:30Z',
      });
      getState().handleTurnResolved({
        battleId: 'battle-1',
        turnIndex: 1,
        playerAAction: '{}',
        playerBAction: '{}',
        log: null,
      });
      expect(getState().phase).toBe('Resolving');

      getState().handleBattleEnded({
        battleId: 'battle-1',
        reason: 'Normal',
        winnerPlayerId: 'player-a',
        endedAt: '2026-01-01T00:01:00Z',
      });
      expect(getState().phase).toBe('Ended');
      expect(getState().endReason).toBe('Normal');
      expect(getState().winnerPlayerId).toBe('player-a');
    });
  });

  describe('ConnectionLost -> reconnect path', () => {
    it('transitions to ConnectionLost then WaitingForJoin on reconnect', () => {
      getState().startBattle('battle-1');
      getState().handleConnected();
      getState().handleTurnOpened({
        battleId: 'battle-1',
        turnIndex: 1,
        deadlineUtc: '2026-01-01T00:00:30Z',
      });

      getState().handleConnectionLost();
      expect(getState().phase).toBe('ConnectionLost');

      getState().handleReconnected();
      expect(getState().phase).toBe('WaitingForJoin');
    });

    it('does not transition to ConnectionLost from Idle', () => {
      getState().handleConnectionLost();
      expect(getState().phase).toBe('Idle');
    });

    it('does not transition to ConnectionLost from Ended', () => {
      getState().startBattle('battle-1');
      getState().handleConnected();
      getState().handleBattleEnded({
        battleId: 'battle-1',
        reason: 'Normal',
        winnerPlayerId: null,
        endedAt: '2026-01-01T00:01:00Z',
      });
      expect(getState().phase).toBe('Ended');

      getState().handleConnectionLost();
      expect(getState().phase).toBe('Ended');
    });
  });

  describe('handleConnectionLost — active-phase whitelist', () => {
    // Positive: every whitelisted phase MUST transition to ConnectionLost.
    // These tests guard against an inverted whitelist condition shipping —
    // the no-op tests alone would all pass even if the guard were broken.

    it('transitions to ConnectionLost from ArenaOpen', () => {
      getState().startBattle('battle-1');
      getState().handleConnected();
      getState().handleBattleReady({
        battleId: 'battle-1',
        playerAId: 'a',
        playerBId: 'b',
        playerAName: 'A',
        playerBName: 'B',
      });
      expect(getState().phase).toBe('ArenaOpen');

      getState().handleConnectionLost();
      expect(getState().phase).toBe('ConnectionLost');
    });

    it('transitions to ConnectionLost from TurnOpen', () => {
      getState().startBattle('battle-1');
      getState().handleConnected();
      getState().handleTurnOpened({
        battleId: 'battle-1',
        turnIndex: 1,
        deadlineUtc: '2026-01-01T00:00:30Z',
      });
      expect(getState().phase).toBe('TurnOpen');

      getState().handleConnectionLost();
      expect(getState().phase).toBe('ConnectionLost');
    });

    it('transitions to ConnectionLost from Submitted', () => {
      getState().startBattle('battle-1');
      getState().handleConnected();
      getState().handleTurnOpened({
        battleId: 'battle-1',
        turnIndex: 1,
        deadlineUtc: '2026-01-01T00:00:30Z',
      });
      getState().selectAttackZone('Head' as BattleZone);
      getState().selectBlockPair(['Chest', 'Belly'] as [BattleZone, BattleZone]);
      getState().setSubmitting(true);
      expect(getState().phase).toBe('Submitted');

      getState().handleConnectionLost();
      expect(getState().phase).toBe('ConnectionLost');
    });

    it('transitions to ConnectionLost from Resolving', () => {
      getState().startBattle('battle-1');
      getState().handleConnected();
      getState().handleTurnOpened({
        battleId: 'battle-1',
        turnIndex: 1,
        deadlineUtc: '2026-01-01T00:00:30Z',
      });
      getState().handleTurnResolved({
        battleId: 'battle-1',
        turnIndex: 1,
        playerAAction: '{}',
        playerBAction: '{}',
        log: null,
      });
      expect(getState().phase).toBe('Resolving');

      getState().handleConnectionLost();
      expect(getState().phase).toBe('ConnectionLost');
    });

    // Negative: every non-whitelisted phase MUST stay put. Idle and Ended
    // already covered above; these add the setup phases the previous guard
    // missed (the actual leak this test suite is here to lock in) plus the
    // remaining terminal/transient phases for completeness.

    it('does not transition to ConnectionLost from Connecting', () => {
      getState().startBattle('battle-1');
      expect(getState().phase).toBe('Connecting');

      getState().handleConnectionLost();
      expect(getState().phase).toBe('Connecting');
    });

    it('does not transition to ConnectionLost from WaitingForJoin', () => {
      getState().startBattle('battle-1');
      getState().handleConnected();
      expect(getState().phase).toBe('WaitingForJoin');

      getState().handleConnectionLost();
      expect(getState().phase).toBe('WaitingForJoin');
    });

    it('does not transition to ConnectionLost from Error', () => {
      getState().startBattle('battle-1');
      getState().handleError('boom');
      expect(getState().phase).toBe('Error');

      getState().handleConnectionLost();
      expect(getState().phase).toBe('Error');
    });

    it('is idempotent when already in ConnectionLost', () => {
      getState().startBattle('battle-1');
      getState().handleConnected();
      getState().handleTurnOpened({
        battleId: 'battle-1',
        turnIndex: 1,
        deadlineUtc: '2026-01-01T00:00:30Z',
      });
      getState().handleConnectionLost();
      expect(getState().phase).toBe('ConnectionLost');

      getState().handleConnectionLost();
      expect(getState().phase).toBe('ConnectionLost');
    });
  });

  describe('battle-setup banner-leak race (regression)', () => {
    // Locks in the fix for the transient "Connection unstable — waiting for
    // server." banner showing on the action-selection UI for ~1–2 seconds at
    // battle start. Root cause: a BFF-emitted BattleConnectionLost arriving
    // during the setup window flipped phase to 'ConnectionLost', and once
    // the loader stopped covering the screen the banner leaked through.

    it('suppresses BattleConnectionLost arriving in WaitingForJoin', () => {
      // Sequence: startBattle → handleConnected (WaitingForJoin) → stray
      // BattleConnectionLost arrives BEFORE the snapshot. The snapshot
      // arrives next and sets phase to TurnOpen. Phase must never enter
      // ConnectionLost in this window.
      getState().startBattle('battle-1');
      getState().handleConnected();
      expect(getState().phase).toBe('WaitingForJoin');

      getState().handleConnectionLost();
      expect(getState().phase).toBe('WaitingForJoin');

      getState().handleSnapshot({
        battleId: 'battle-1',
        playerAId: 'a',
        playerBId: 'b',
        playerAName: 'A',
        playerBName: 'B',
        ruleset: { turnSeconds: 30, noActionLimit: null },
        turnIndex: 1,
        deadlineUtc: '2026-01-01T00:00:30Z',
        noActionStreakBoth: 0,
        lastResolvedTurnIndex: 0,
        endedReason: null,
        version: 1,
        playerAHp: 100,
        playerBHp: 100,
        playerAMaxHp: 100,
        playerBMaxHp: 100,
        phase: 'TurnOpen',
      });
      expect(getState().phase).toBe('TurnOpen');
    });

    it('still surfaces BattleConnectionLost arriving in ArenaOpen (active phase)', () => {
      // Mirror of the above for the post-setup case: once the snapshot has
      // landed and phase is ArenaOpen, a real BattleConnectionLost from the
      // BFF must still flip phase to ConnectionLost so the user sees the
      // banner. This is the load-bearing case the catch-all banner branch
      // is there for.
      getState().startBattle('battle-1');
      getState().handleConnected();
      getState().handleBattleReady({
        battleId: 'battle-1',
        playerAId: 'a',
        playerBId: 'b',
        playerAName: 'A',
        playerBName: 'B',
      });
      expect(getState().phase).toBe('ArenaOpen');

      getState().handleConnectionLost();
      expect(getState().phase).toBe('ConnectionLost');
    });
  });
});
