import { describe, it, expect } from 'vitest';
import { isValidBlockPair, buildActionPayload, VALID_BLOCK_PAIRS, ALL_ZONES } from './zones';
import type { BattleZone } from '@/types/battle';

describe('isValidBlockPair', () => {
  it.each(VALID_BLOCK_PAIRS)('accepts adjacent pair %s + %s', (a, b) => {
    expect(isValidBlockPair(a, b)).toBe(true);
    expect(isValidBlockPair(b, a)).toBe(true);
  });

  it('rejects non-adjacent pairs', () => {
    expect(isValidBlockPair('Head', 'Belly')).toBe(false);
    expect(isValidBlockPair('Head', 'Waist')).toBe(false);
    expect(isValidBlockPair('Chest', 'Legs')).toBe(false);
    expect(isValidBlockPair('Chest', 'Waist')).toBe(false);
  });

  it('rejects same-zone pair', () => {
    for (const zone of ALL_ZONES) {
      expect(isValidBlockPair(zone, zone)).toBe(false);
    }
  });
});

describe('buildActionPayload', () => {
  it('returns a string', () => {
    const result = buildActionPayload('Head', ['Chest', 'Belly']);
    expect(typeof result).toBe('string');
  });

  it('produces valid JSON with correct field names', () => {
    const result = buildActionPayload('Head', ['Chest', 'Belly']);
    const parsed = JSON.parse(result);

    expect(parsed).toEqual({
      attackZone: 'Head',
      blockZonePrimary: 'Chest',
      blockZoneSecondary: 'Belly',
    });
  });

  it('contains exactly the three expected keys', () => {
    const result = buildActionPayload('Legs', ['Legs', 'Head']);
    const parsed = JSON.parse(result);
    const keys = Object.keys(parsed).sort();

    expect(keys).toEqual(['attackZone', 'blockZonePrimary', 'blockZoneSecondary']);
  });

  it('round-trips all zone values correctly', () => {
    const pairs: [BattleZone, [BattleZone, BattleZone]][] = [
      ['Head', ['Head', 'Chest']],
      ['Belly', ['Waist', 'Legs']],
      ['Legs', ['Legs', 'Head']],
    ];

    for (const [attack, block] of pairs) {
      const parsed = JSON.parse(buildActionPayload(attack, block));
      expect(parsed.attackZone).toBe(attack);
      expect(parsed.blockZonePrimary).toBe(block[0]);
      expect(parsed.blockZoneSecondary).toBe(block[1]);
    }
  });
});
