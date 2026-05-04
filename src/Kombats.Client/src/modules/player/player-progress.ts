import type { CharacterResponse } from '@/types/api';

export interface PlayerProgressSnapshot {
  totalXp: number;
  level: number;
  unspentPoints: number;
}

export interface PlayerProgressDelta {
  /** True when any of xp/level/unspentPoints changed between pre and post. */
  changed: boolean;
  /** True when post.level is strictly greater than pre.level. */
  leveledUp: boolean;
  /** Non-null only when {@link leveledUp} is true. */
  newLevel: number | null;
}

export function snapshotCharacter(
  character: CharacterResponse | null | undefined,
): PlayerProgressSnapshot | null {
  if (!character) return null;
  return {
    totalXp: character.totalXp,
    level: character.level,
    unspentPoints: character.unspentPoints,
  };
}

/**
 * Pure comparator used by the post-battle refresh (DEC-5) to decide whether
 * the first refetch already brought fresh data — and whether a level-up was
 * detected. "Changed" is the disjunction of xp/level/unspent-points deltas so
 * that XP-only updates, level-only updates, and unspent-points-only updates
 * all suppress the retry.
 *
 * If either snapshot is missing, we conservatively report no change and no
 * level-up (caller will decide how to treat this; current policy is to skip
 * the retry to avoid wasted HTTP calls when we have nothing to compare).
 */
export function comparePlayerProgress(
  pre: PlayerProgressSnapshot | null,
  post: PlayerProgressSnapshot | null,
): PlayerProgressDelta {
  if (!pre || !post) {
    return { changed: false, leveledUp: false, newLevel: null };
  }
  const changed =
    post.totalXp !== pre.totalXp ||
    post.level !== pre.level ||
    post.unspentPoints !== pre.unspentPoints;
  const leveledUp = post.level > pre.level;
  return {
    changed,
    leveledUp,
    newLevel: leveledUp ? post.level : null,
  };
}
