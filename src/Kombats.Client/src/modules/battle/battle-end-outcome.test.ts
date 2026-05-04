import { describe, it, expect } from 'vitest';
import { deriveOutcome } from './battle-end-outcome';

const ME = 'me-identity';
const OPP = 'opp-identity';

describe('deriveOutcome', () => {
  it('returns victory when the current player is the winner on Normal', () => {
    const r = deriveOutcome('Normal', ME, ME);
    expect(r.outcome).toBe('victory');
    expect(r.title).toBe('Victory');
  });

  it('returns defeat when the opponent is the winner on Normal', () => {
    const r = deriveOutcome('Normal', OPP, ME);
    expect(r.outcome).toBe('defeat');
    expect(r.title).toBe('Defeat');
  });

  it('collapses Normal-with-no-winner to defeat (no draws in Kombats)', () => {
    const r = deriveOutcome('Normal', null, ME);
    expect(r.outcome).toBe('defeat');
  });

  it('collapses DoubleForfeit to defeat for the player', () => {
    const r = deriveOutcome('DoubleForfeit', null, ME);
    expect(r.outcome).toBe('defeat');
  });

  it('collapses Timeout to defeat for the player', () => {
    const r = deriveOutcome('Timeout', null, ME);
    expect(r.outcome).toBe('defeat');
  });

  it.each(['Cancelled', 'AdminForced'] as const)('collapses %s to defeat', (reason) => {
    const r = deriveOutcome(reason, null, ME);
    expect(r.outcome).toBe('defeat');
  });

  it('collapses Unknown without a winner to defeat', () => {
    const r = deriveOutcome('Unknown', null, ME);
    expect(r.outcome).toBe('defeat');
  });

  it('still derives victory from winnerId when reason is Unknown', () => {
    const r = deriveOutcome('Unknown', ME, ME);
    expect(r.outcome).toBe('victory');
  });

  it('flags SystemError as a system-error outcome with no lobby promise', () => {
    const r = deriveOutcome('SystemError', null, ME);
    expect(r.outcome).toBe('systemError');
    expect(r.title).toBe('Battle Ended');
    expect(r.subtitle).toBe('Battle ended due to a system error.');
    expect(r.subtitle.toLowerCase()).not.toContain('lobby');
  });

  it('routes a non-self winner on SystemError to systemError, not defeat', () => {
    // SystemError takes precedence — even with a winner the engine reports
    // the battle could not be resolved cleanly.
    const r = deriveOutcome('SystemError', OPP, ME);
    expect(r.outcome).toBe('systemError');
  });

  it('treats a missing myId as a defeat (cannot claim victory without identity)', () => {
    const r = deriveOutcome('Normal', OPP, null);
    expect(r.outcome).toBe('defeat');
  });

  it('treats null reason + null winner as defeat', () => {
    const r = deriveOutcome(null, null, ME);
    expect(r.outcome).toBe('defeat');
  });
});
