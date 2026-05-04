import { describe, it, expect } from 'vitest';
import { computeTurnTimerView, DEADLINE_BUFFER_MS } from './turn-timer-view';

const NOW = Date.UTC(2026, 3, 17, 12, 0, 0);

function deadlineInSeconds(seconds: number): string {
  // deadline relative to NOW, with the 1.5s buffer pre-added so that
  // `bufferedDeadline - now === seconds` seconds exactly.
  return new Date(NOW + seconds * 1000 + DEADLINE_BUFFER_MS).toISOString();
}

describe('computeTurnTimerView', () => {
  it('returns resolving regardless of deadline when phase is Resolving', () => {
    const v = computeTurnTimerView('Resolving', deadlineInSeconds(30), NOW);
    expect(v.kind).toBe('resolving');
  });

  it.each([
    'Idle',
    'Connecting',
    'WaitingForJoin',
    'ArenaOpen',
    'Submitted',
    'Ended',
    'ConnectionLost',
    'Error',
  ] as const)('returns idle when phase is %s', (phase) => {
    const v = computeTurnTimerView(phase, deadlineInSeconds(30), NOW);
    expect(v.kind).toBe('idle');
  });

  it('returns idle when phase is TurnOpen but deadline is null', () => {
    const v = computeTurnTimerView('TurnOpen', null, NOW);
    expect(v.kind).toBe('idle');
  });

  it('counts down with normal urgency when > 10s remaining', () => {
    const v = computeTurnTimerView('TurnOpen', deadlineInSeconds(20), NOW);
    expect(v.kind).toBe('countdown');
    if (v.kind !== 'countdown') return;
    expect(v.seconds).toBe(20);
    expect(v.urgency).toBe('normal');
  });

  it('switches to warning urgency at 10s', () => {
    const v = computeTurnTimerView('TurnOpen', deadlineInSeconds(10), NOW);
    if (v.kind !== 'countdown') throw new Error('expected countdown');
    expect(v.seconds).toBe(10);
    expect(v.urgency).toBe('warning');
  });

  it('switches to critical urgency at 5s', () => {
    const v = computeTurnTimerView('TurnOpen', deadlineInSeconds(5), NOW);
    if (v.kind !== 'countdown') throw new Error('expected countdown');
    expect(v.seconds).toBe(5);
    expect(v.urgency).toBe('critical');
  });

  it('clamps seconds to 0 after the buffered deadline has passed', () => {
    const v = computeTurnTimerView('TurnOpen', deadlineInSeconds(-3), NOW);
    if (v.kind !== 'countdown') throw new Error('expected countdown');
    expect(v.seconds).toBe(0);
    expect(v.urgency).toBe('critical');
  });

  it('applies the 1.5s buffer: deadline 1500ms in the future reads as 0s remaining', () => {
    const deadline = new Date(NOW + DEADLINE_BUFFER_MS).toISOString();
    const v = computeTurnTimerView('TurnOpen', deadline, NOW);
    if (v.kind !== 'countdown') throw new Error('expected countdown');
    expect(v.seconds).toBe(0);
  });
});
