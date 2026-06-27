import { describe, expect, it } from 'vitest';
import { decideAuthGuard, decideOnboardingGuard, decideBattleGuard } from './guard-decisions';
import type { QueueStatusResponse, CharacterResponse } from '@/types/api';

// ---------------------------------------------------------------------------
// AuthGuard
// ---------------------------------------------------------------------------

describe('decideAuthGuard', () => {
  it('returns loading while auth bootstrap is pending', () => {
    expect(decideAuthGuard('loading')).toEqual({ type: 'loading' });
  });

  it('redirects unauthenticated users to the landing page', () => {
    expect(decideAuthGuard('unauthenticated')).toEqual({
      type: 'navigate',
      to: '/',
    });
  });

  it('allows authenticated users through', () => {
    expect(decideAuthGuard('authenticated')).toEqual({ type: 'allow' });
  });
});

// ---------------------------------------------------------------------------
// OnboardingGuard
// ---------------------------------------------------------------------------

function mkCharacter(state: CharacterResponse['onboardingState']): CharacterResponse {
  return {
    characterId: 'id-1',
    name: 'Test',
    level: 1,
    totalXp: 0,
    strength: 10,
    agility: 10,
    intuition: 10,
    vitality: 10,
    unspentPoints: 0,
    onboardingState: state,
    revision: 1,
    avatarId: 'shadow_oni',
  };
}

describe('decideOnboardingGuard', () => {
  it('allows /onboarding/name when there is no character', () => {
    expect(decideOnboardingGuard(null, '/onboarding/name')).toEqual({
      type: 'allow',
    });
  });

  it('redirects to /onboarding/name from any other path when there is no character', () => {
    expect(decideOnboardingGuard(null, '/lobby')).toEqual({
      type: 'navigate',
      to: '/onboarding/name',
    });
    expect(decideOnboardingGuard(null, '/onboarding/stats')).toEqual({
      type: 'navigate',
      to: '/onboarding/name',
    });
  });

  it('routes Draft characters to /onboarding/name', () => {
    const draft = mkCharacter('Draft');
    expect(decideOnboardingGuard(draft, '/onboarding/name')).toEqual({
      type: 'allow',
    });
    expect(decideOnboardingGuard(draft, '/onboarding/stats')).toEqual({
      type: 'navigate',
      to: '/onboarding/name',
    });
    expect(decideOnboardingGuard(draft, '/lobby')).toEqual({
      type: 'navigate',
      to: '/onboarding/name',
    });
  });

  it('routes Named characters to /onboarding/stats', () => {
    const named = mkCharacter('Named');
    expect(decideOnboardingGuard(named, '/onboarding/stats')).toEqual({
      type: 'allow',
    });
    expect(decideOnboardingGuard(named, '/onboarding/name')).toEqual({
      type: 'navigate',
      to: '/onboarding/stats',
    });
    expect(decideOnboardingGuard(named, '/lobby')).toEqual({
      type: 'navigate',
      to: '/onboarding/stats',
    });
  });

  it('blocks Ready characters from onboarding routes', () => {
    const ready = mkCharacter('Ready');
    expect(decideOnboardingGuard(ready, '/onboarding/name')).toEqual({
      type: 'navigate',
      to: '/lobby',
    });
    expect(decideOnboardingGuard(ready, '/onboarding/stats')).toEqual({
      type: 'navigate',
      to: '/lobby',
    });
  });

  it('allows Ready characters to reach non-onboarding routes', () => {
    const ready = mkCharacter('Ready');
    expect(decideOnboardingGuard(ready, '/lobby')).toEqual({ type: 'allow' });
    expect(decideOnboardingGuard(ready, '/battle/abc')).toEqual({ type: 'allow' });
  });
});

// ---------------------------------------------------------------------------
// BattleGuard — four core branches plus the REQ-P1 result-dismissal edge
// ---------------------------------------------------------------------------

function queue(overrides: Partial<QueueStatusResponse> = {}): QueueStatusResponse {
  return {
    status: 'Idle',
    matchId: null,
    battleId: null,
    matchState: null,
    ...overrides,
  } as QueueStatusResponse;
}

describe('decideBattleGuard', () => {
  describe('queueStatus = Matched+battleId (live battle)', () => {
    it('redirects from the lobby onto /battle/:id', () => {
      const q = queue({ status: 'Matched', battleId: 'b1', matchId: 'm1' });
      expect(decideBattleGuard(q, 'Idle', null, '/lobby')).toEqual({
        type: 'navigate',
        to: '/battle/b1',
      });
    });

    it('allows the battle route to render', () => {
      const q = queue({ status: 'Matched', battleId: 'b1', matchId: 'm1' });
      expect(decideBattleGuard(q, 'TurnOpen', 'b1', '/battle/b1')).toEqual({
        type: 'allow',
      });
    });

    it('allows /battle/:id/result to render', () => {
      const q = queue({ status: 'Matched', battleId: 'b1', matchId: 'm1' });
      expect(decideBattleGuard(q, 'Ended', 'b1', '/battle/b1/result')).toEqual({ type: 'allow' });
    });

    it('does NOT bounce an already-Ended battle back onto /battle/:id (REQ-P1)', () => {
      // Player is on /lobby after a battle; server still reports Matched+b1
      // for a short window. With battlePhase === 'Ended' and storeBattleId
      // matching, the guard must NOT re-redirect into /battle/b1.
      const q = queue({ status: 'Matched', battleId: 'b1', matchId: 'm1' });
      expect(decideBattleGuard(q, 'Ended', 'b1', '/lobby')).toEqual({
        type: 'allow',
      });
    });
  });

  describe('queueStatus = Searching (or Matched without battleId)', () => {
    it('allows /lobby to render while Searching (overlay swap, not route swap)', () => {
      const q = queue({ status: 'Searching' });
      expect(decideBattleGuard(q, 'Idle', null, '/lobby')).toEqual({
        type: 'allow',
      });
    });

    it('allows /lobby to render while Matched-without-battleId', () => {
      const q = queue({ status: 'Matched', matchId: 'm1', battleId: null });
      expect(decideBattleGuard(q, 'Idle', null, '/lobby')).toEqual({
        type: 'allow',
      });
    });

    it('bounces non-lobby in-app paths back to /lobby while Searching', () => {
      const q = queue({ status: 'Searching' });
      expect(decideBattleGuard(q, 'Idle', null, '/battle/b1')).toEqual({
        type: 'navigate',
        to: '/lobby',
      });
    });
  });

  describe('queueStatus = null (no active queue)', () => {
    it('blocks /battle/:id from rendering and sends user to /lobby', () => {
      expect(decideBattleGuard(null, 'Idle', null, '/battle/b1')).toEqual({
        type: 'navigate',
        to: '/lobby',
      });
    });

    it('allows /lobby to render', () => {
      expect(decideBattleGuard(null, 'Idle', null, '/lobby')).toEqual({
        type: 'allow',
      });
    });

    it('allows /battle/:id/result while the result for that Ended battle is still being dismissed (REQ-P1)', () => {
      // Player just finished a battle; queueStatus has been optimistically
      // cleared by returnFromBattle, but battle store still has the Ended
      // state + matching battleId. The user is on the result screen —
      // they must be allowed to stay until they click Return to Lobby.
      expect(decideBattleGuard(null, 'Ended', 'b1', '/battle/b1/result')).toEqual({
        type: 'allow',
      });
    });

    it('blocks /battle/:id/result when the Ended battleId does not match the route', () => {
      expect(decideBattleGuard(null, 'Ended', 'b2', '/battle/b1/result')).toEqual({
        type: 'navigate',
        to: '/lobby',
      });
    });
  });

  describe('queueStatus.status = Idle / NotQueued', () => {
    it('treats Idle the same as null queueStatus', () => {
      const q = queue({ status: 'Idle' });
      expect(decideBattleGuard(q, 'Idle', null, '/battle/b1')).toEqual({
        type: 'navigate',
        to: '/lobby',
      });
    });

    it('treats NotQueued the same as null queueStatus', () => {
      const q = queue({ status: 'NotQueued' });
      expect(decideBattleGuard(q, 'Idle', null, '/battle/b1')).toEqual({
        type: 'navigate',
        to: '/lobby',
      });
    });
  });
});
