// Pure helper used by AppCrashScreen and its test. Separated from the
// component file because the react-refresh plugin requires component-only
// modules.

import type { BattlePhase } from '@/modules/battle/store';

export function selectRecoveryTarget(
  battleId: string | null,
  phase: BattlePhase | null,
): {
  label: string;
  href: string;
  inBattle: boolean;
} {
  // A crash during the result screen (phase === 'Ended') means the battle is
  // already over on the server — sending the user back to `/battle/:id` just
  // triggers BattleGuard's ended-battle bounce to /lobby. Route them to the
  // lobby directly instead.
  const isLiveBattle = !!battleId && phase !== 'Ended';
  if (isLiveBattle) {
    return { label: 'Rejoin battle', href: `/battle/${battleId}`, inBattle: true };
  }
  return { label: 'Return to lobby', href: '/lobby', inBattle: false };
}
