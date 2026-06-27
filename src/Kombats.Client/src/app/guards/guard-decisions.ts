import type { QueueStatusResponse, CharacterResponse } from '@/types/api';
import type { AuthStatus } from '@/modules/auth/store';
import type { BattlePhase } from '@/modules/battle/store';

// Pure decision helpers extracted from the React guards so the branches
// can be unit-tested without a DOM or router. Each guard component calls
// the matching helper and renders accordingly.

export type GuardDecision =
  | { type: 'allow' }
  | { type: 'navigate'; to: string }
  | { type: 'loading' };

// ---------------------------------------------------------------------------
// Auth
// ---------------------------------------------------------------------------

export function decideAuthGuard(authStatus: AuthStatus): GuardDecision {
  if (authStatus === 'loading') return { type: 'loading' };
  if (authStatus === 'unauthenticated') return { type: 'navigate', to: '/' };
  return { type: 'allow' };
}

// ---------------------------------------------------------------------------
// Onboarding
// ---------------------------------------------------------------------------

export function decideOnboardingGuard(
  character: CharacterResponse | null,
  pathname: string,
): GuardDecision {
  // No character at all — only /onboarding/name is valid.
  if (!character) {
    if (pathname === '/onboarding/name') return { type: 'allow' };
    return { type: 'navigate', to: '/onboarding/name' };
  }

  const { onboardingState } = character;

  if (onboardingState === 'Draft') {
    if (pathname === '/onboarding/name') return { type: 'allow' };
    return { type: 'navigate', to: '/onboarding/name' };
  }

  if (onboardingState === 'Named') {
    if (pathname === '/onboarding/stats') return { type: 'allow' };
    return { type: 'navigate', to: '/onboarding/stats' };
  }

  // Ready / Completed / unknown — block access to onboarding routes.
  if (pathname.startsWith('/onboarding')) {
    return { type: 'navigate', to: '/lobby' };
  }
  return { type: 'allow' };
}

// ---------------------------------------------------------------------------
// Battle
// ---------------------------------------------------------------------------

export function decideBattleGuard(
  queueStatus: QueueStatusResponse | null,
  battlePhase: BattlePhase,
  storeBattleId: string | null,
  pathname: string,
): GuardDecision {
  if (queueStatus) {
    if (queueStatus.status === 'Matched' && queueStatus.battleId) {
      const battlePath = `/battle/${queueStatus.battleId}`;
      // REQ-P1 (hard gate: result must be dismissed): once the battle has
      // ended, do not force-redirect the player back onto the live battle
      // path. They may be on `/battle/:id/result` dismissing the result,
      // or on their way to the lobby.
      const battleEnded = battlePhase === 'Ended' && storeBattleId === queueStatus.battleId;
      if (!battleEnded && !pathname.startsWith(battlePath)) {
        return { type: 'navigate', to: battlePath };
      }
      return { type: 'allow' };
    }

    if (
      queueStatus.status === 'Searching' ||
      (queueStatus.status === 'Matched' && !queueStatus.battleId)
    ) {
      // Searching / matched-without-battleId render inside the unified
      // lobby screen — no separate route. Allow /lobby through; bounce
      // any other in-app path back to it.
      if (pathname !== '/lobby') {
        return { type: 'navigate', to: '/lobby' };
      }
      return { type: 'allow' };
    }
  }

  // No active queue state — block access to battle routes,
  // EXCEPT while the result screen is still being dismissed (hard gate
  // REQ-P1). The result screen itself clears state by navigating to /lobby.
  const onResultForEndedBattle =
    battlePhase === 'Ended' && !!storeBattleId && pathname === `/battle/${storeBattleId}/result`;

  if (!onResultForEndedBattle && pathname.startsWith('/battle')) {
    return { type: 'navigate', to: '/lobby' };
  }

  return { type: 'allow' };
}
