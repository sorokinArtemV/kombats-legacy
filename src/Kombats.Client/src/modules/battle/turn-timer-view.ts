import type { BattlePhase } from './store';

// DEC-4: show deadline minus a 1.5s safety buffer
export const DEADLINE_BUFFER_MS = 1500;

export type TurnTimerUrgency = 'normal' | 'warning' | 'critical';

export type TurnTimerView =
  | { kind: 'resolving' }
  | { kind: 'idle' }
  | { kind: 'countdown'; seconds: number; urgency: TurnTimerUrgency };

/**
 * Pure view-model for the turn timer. Given the current phase, deadline,
 * and a `now` timestamp, returns what should be displayed.
 */
export function computeTurnTimerView(
  phase: BattlePhase,
  deadlineUtc: string | null,
  now: number,
): TurnTimerView {
  if (phase === 'Resolving') return { kind: 'resolving' };
  if (phase !== 'TurnOpen' || !deadlineUtc) return { kind: 'idle' };

  const bufferedDeadlineMs = new Date(deadlineUtc).getTime() - DEADLINE_BUFFER_MS;
  const remainingMs = Math.max(0, bufferedDeadlineMs - now);
  const seconds = Math.ceil(remainingMs / 1000);
  const urgency: TurnTimerUrgency =
    remainingMs <= 5000 ? 'critical' : remainingMs <= 10000 ? 'warning' : 'normal';

  return { kind: 'countdown', seconds, urgency };
}
