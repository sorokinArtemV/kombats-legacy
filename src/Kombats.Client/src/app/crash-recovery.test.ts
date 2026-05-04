import { describe, expect, it } from 'vitest';
import { selectRecoveryTarget } from './crash-recovery';

describe('selectRecoveryTarget', () => {
  it('routes a crash during a live battle to the same battleId for rejoin', () => {
    const { label, href, inBattle } = selectRecoveryTarget('abc-123', 'TurnOpen');
    expect(inBattle).toBe(true);
    expect(label).toBe('Rejoin battle');
    expect(href).toBe('/battle/abc-123');
  });

  it('routes a crash during the result screen to the lobby', () => {
    // Phase 'Ended' means the battle is over on the server. Sending the user
    // to /battle/:id would just bounce back to the lobby via BattleGuard, so
    // go there directly.
    const { label, href, inBattle } = selectRecoveryTarget('abc-123', 'Ended');
    expect(inBattle).toBe(false);
    expect(label).toBe('Return to lobby');
    expect(href).toBe('/lobby');
  });

  it('routes a lobby crash (no battleId) to the lobby', () => {
    const { label, href, inBattle } = selectRecoveryTarget(null, null);
    expect(inBattle).toBe(false);
    expect(label).toBe('Return to lobby');
    expect(href).toBe('/lobby');
  });

  it('treats empty battleId as non-battle crash', () => {
    expect(selectRecoveryTarget('', 'TurnOpen').inBattle).toBe(false);
  });

  it('treats error-phase crash as recoverable via rejoin', () => {
    // When BattleScreen is already in the Error phase (terminal connection
    // failure) but the server-side battle is still live, rejoin is still
    // the right move — a fresh page load will re-establish the hub.
    const { inBattle } = selectRecoveryTarget('abc-123', 'Error');
    expect(inBattle).toBe(true);
  });
});
