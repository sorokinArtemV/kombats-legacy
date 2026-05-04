/**
 * Arena identity types and deterministic fallback picker.
 *
 * The four arenas are: Palace Roof (default), Blood Moon, Desert,
 * Bamboo. Per-arena CSS overrides live in `src/ui/theme/arenas.css`
 * keyed on the matching `data-arena` attribute value.
 *
 * When the server payload omits `arenaId` (transitional state until
 * the backend ships the field on BattleReadyRealtime /
 * BattleSnapshotRealtime), the client falls back to a deterministic
 * pick keyed on `battleId`. Both opponents compute the same hash
 * for the same battleId and therefore land on the same arena.
 */

export type ArenaId = 'palace-roof' | 'blood-moon' | 'desert' | 'bamboo';

export const ARENA_IDS: readonly ArenaId[] = [
  'palace-roof',
  'blood-moon',
  'desert',
  'bamboo',
] as const;

/**
 * Deterministic arena pick from a battleId.
 * Sum-of-charCodes mod ARENA_IDS.length. Same pattern as
 * modules/chat/nick-color.ts.
 *
 * Used as the client-side fallback while backend `arenaId` is not
 * yet plumbed through. Once backend sends the field, this function
 * stops being called.
 */
export function pickArenaFromBattleId(battleId: string): ArenaId {
  let sum = 0;
  for (let i = 0; i < battleId.length; i++) {
    sum += battleId.charCodeAt(i);
  }
  return ARENA_IDS[sum % ARENA_IDS.length];
}
