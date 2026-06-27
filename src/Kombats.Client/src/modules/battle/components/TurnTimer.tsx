import { useEffect, useState } from 'react';
import { clsx } from 'clsx';
import { useBattlePhase, useBattleTurn } from '../hooks';
import { computeTurnTimerView, type TurnTimerUrgency } from '../turn-timer-view';

/**
 * Inline Clock+number turn timer. Matches DESIGN_REFERENCE.md §5.17 meta row
 * (gold Clock icon + tabular-nums countdown).
 */
export function TurnTimer() {
  const phase = useBattlePhase();
  const { deadlineUtc } = useBattleTurn();
  const [now, setNow] = useState<number>(() => Date.now());

  const countsDown = phase === 'TurnOpen' && deadlineUtc !== null;

  useEffect(() => {
    if (!countsDown) return;

    const tick = () => setNow(Date.now());
    tick();
    // Display granularity is 1 second; ticking every 250ms is more than fast
    // enough to keep the second-boundary visually crisp without re-rendering
    // the timer subtree 10x/sec.
    const timer = setInterval(tick, 250);
    return () => {
      clearInterval(timer);
    };
  }, [countsDown, deadlineUtc]);

  const view = computeTurnTimerView(phase, deadlineUtc, now);

  if (view.kind === 'resolving') {
    return (
      <span
        className="inline-flex items-center gap-1.5 font-primary text-[13px] uppercase tracking-[0.18em]"
        style={{ color: 'var(--color-kombats-gold-light)' }}
      >
        <ClockIcon />
        <span>—</span>
      </span>
    );
  }
  if (view.kind === 'idle') {
    return (
      <span className="inline-flex items-center gap-1.5 font-primary text-[13px] tabular-nums text-text-muted">
        <ClockIcon />
        <span>—</span>
      </span>
    );
  }

  return (
    <span
      className={clsx(
        'inline-flex items-center gap-1.5 font-primary text-[13px] tabular-nums',
        urgencyClass(view.urgency),
      )}
    >
      <ClockIcon />
      <span>{view.seconds}</span>
    </span>
  );
}

function urgencyClass(urgency: TurnTimerUrgency): string {
  switch (urgency) {
    case 'critical':
      return 'text-kombats-crimson-light';
    case 'warning':
      return 'text-victory-gold';
    default:
      return 'text-accent-text';
  }
}

function ClockIcon() {
  return (
    <svg
      width="14"
      height="14"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden
    >
      <circle cx="12" cy="12" r="10" />
      <polyline points="12 6 12 12 16 14" />
    </svg>
  );
}
