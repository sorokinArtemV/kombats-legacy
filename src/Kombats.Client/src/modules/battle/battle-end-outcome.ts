import type { BattleEndReasonRealtime } from '@/types/battle';

export type BattleEndOutcome = 'victory' | 'defeat' | 'systemError';

export interface BattleEndPresentation {
  outcome: BattleEndOutcome;
  title: string;
  subtitle: string;
}

/**
 * Derive the user-facing battle-end presentation from the end reason,
 * winner, and current player identity.
 *
 * Kombats has two terminal user-facing outcomes — victory and defeat — plus
 * a thin "system error" carve-out for when the engine could not finish the
 * battle cleanly. Forfeit / timeout / cancelled / admin-forced / unknown
 * reasons all collapse to defeat for whoever is not the surviving winner;
 * if no winner is recoverable, the player is treated as having lost so the
 * UI never has to render a third "draw" branch the rest of the product
 * does not support.
 *
 * SystemError is the one branch that is allowed to render without a winner
 * — the Battle.Domain engine has reported it could not complete the match,
 * and pretending the player won or lost would be dishonest. Result screen
 * suppresses the rewards block in this branch.
 */
export function deriveOutcome(
  reason: BattleEndReasonRealtime | null,
  winnerId: string | null,
  myId: string | null,
): BattleEndPresentation {
  if (reason === 'SystemError') {
    return {
      outcome: 'systemError',
      title: 'Battle Ended',
      subtitle: 'Battle ended due to a system error.',
    };
  }

  if (winnerId !== null && myId !== null && winnerId === myId) {
    return {
      outcome: 'victory',
      title: 'Victory',
      subtitle: 'Triumph in Combat',
    };
  }

  return {
    outcome: 'defeat',
    title: 'Defeat',
    subtitle: 'Honor in Battle',
  };
}
