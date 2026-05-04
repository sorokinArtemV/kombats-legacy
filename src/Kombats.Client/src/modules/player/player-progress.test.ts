import { describe, it, expect } from 'vitest';
import {
  comparePlayerProgress,
  snapshotCharacter,
  type PlayerProgressSnapshot,
} from './player-progress';
import type { CharacterResponse } from '@/types/api';

function snap(totalXp: number, level: number, unspentPoints: number): PlayerProgressSnapshot {
  return { totalXp, level, unspentPoints };
}

describe('snapshotCharacter', () => {
  it('returns null for null/undefined character', () => {
    expect(snapshotCharacter(null)).toBeNull();
    expect(snapshotCharacter(undefined)).toBeNull();
  });

  it('extracts only the progression fields', () => {
    const character: CharacterResponse = {
      characterId: 'id',
      onboardingState: 'Ready',
      name: 'Test',
      strength: 10,
      agility: 7,
      intuition: 5,
      vitality: 8,
      unspentPoints: 2,
      revision: 3,
      totalXp: 1250,
      level: 5,
      avatarId: 'shadow_oni',
    };
    expect(snapshotCharacter(character)).toEqual({
      totalXp: 1250,
      level: 5,
      unspentPoints: 2,
    });
  });
});

describe('comparePlayerProgress', () => {
  it('returns no-change when pre is null', () => {
    const d = comparePlayerProgress(null, snap(100, 2, 0));
    expect(d.changed).toBe(false);
    expect(d.leveledUp).toBe(false);
    expect(d.newLevel).toBeNull();
  });

  it('returns no-change when post is null', () => {
    const d = comparePlayerProgress(snap(100, 2, 0), null);
    expect(d.changed).toBe(false);
    expect(d.leveledUp).toBe(false);
    expect(d.newLevel).toBeNull();
  });

  it('reports no-change when xp/level/unspent are all equal — DEC-5 retry will fire', () => {
    const pre = snap(100, 2, 0);
    const post = snap(100, 2, 0);
    const d = comparePlayerProgress(pre, post);
    expect(d.changed).toBe(false);
    expect(d.leveledUp).toBe(false);
    expect(d.newLevel).toBeNull();
  });

  it('reports change on xp-only delta (no level-up) — retry suppressed', () => {
    const d = comparePlayerProgress(snap(100, 2, 0), snap(150, 2, 0));
    expect(d.changed).toBe(true);
    expect(d.leveledUp).toBe(false);
    expect(d.newLevel).toBeNull();
  });

  it('reports change on unspent-points-only delta — retry suppressed', () => {
    const d = comparePlayerProgress(snap(100, 2, 0), snap(100, 2, 1));
    expect(d.changed).toBe(true);
    expect(d.leveledUp).toBe(false);
    expect(d.newLevel).toBeNull();
  });

  it('reports level-up with newLevel populated', () => {
    const d = comparePlayerProgress(snap(100, 2, 0), snap(260, 3, 3));
    expect(d.changed).toBe(true);
    expect(d.leveledUp).toBe(true);
    expect(d.newLevel).toBe(3);
  });

  it('reports level-up across multiple levels (multi-level jump)', () => {
    const d = comparePlayerProgress(snap(0, 1, 0), snap(10_000, 7, 12));
    expect(d.leveledUp).toBe(true);
    expect(d.newLevel).toBe(7);
  });

  it('does not report level-up if level decreased (defensive — should not happen in prod)', () => {
    const d = comparePlayerProgress(snap(100, 3, 0), snap(50, 2, 0));
    expect(d.changed).toBe(true);
    expect(d.leveledUp).toBe(false);
    expect(d.newLevel).toBeNull();
  });
});
