import type { BattleZone } from '@/types/battle';

// ---------------------------------------------------------------------------
// Zone ring topology
// ---------------------------------------------------------------------------

// The 5 zones form a ring: Head → Chest → Belly → Waist → Legs → Head
// A block pair must be two adjacent zones on the ring.

export const ALL_ZONES: readonly BattleZone[] = [
  'Head',
  'Chest',
  'Belly',
  'Waist',
  'Legs',
] as const;

// Adjacency ring — each zone is adjacent to the one before and after it
const ZONE_INDEX: Record<BattleZone, number> = {
  Head: 0,
  Chest: 1,
  Belly: 2,
  Waist: 3,
  Legs: 4,
};

/**
 * Valid block pairs: two adjacent zones on the ring.
 * Order within the pair does not matter.
 */
export const VALID_BLOCK_PAIRS: readonly [BattleZone, BattleZone][] = [
  ['Head', 'Chest'],
  ['Chest', 'Belly'],
  ['Belly', 'Waist'],
  ['Waist', 'Legs'],
  ['Legs', 'Head'],
];

/**
 * Check whether a pair of zones forms a valid block selection.
 */
export function isValidBlockPair(a: BattleZone, b: BattleZone): boolean {
  const diff = Math.abs(ZONE_INDEX[a] - ZONE_INDEX[b]);
  // Adjacent on ring: diff is 1, or wrap-around diff is 4 (Head↔Legs)
  return diff === 1 || diff === 4;
}

// ---------------------------------------------------------------------------
// Action payload construction
// ---------------------------------------------------------------------------

interface TurnAction {
  attackZone: BattleZone;
  blockZonePrimary: BattleZone;
  blockZoneSecondary: BattleZone;
}

/**
 * Build the action payload as a JSON string.
 * The backend expects `SubmitTurnAction(battleId, turnIndex, payload)`
 * where payload is a JSON-stringified object with fields:
 * `attackZone`, `blockZonePrimary`, `blockZoneSecondary`.
 */
export function buildActionPayload(
  attackZone: BattleZone,
  blockPair: [BattleZone, BattleZone],
): string {
  const action: TurnAction = {
    attackZone,
    blockZonePrimary: blockPair[0],
    blockZoneSecondary: blockPair[1],
  };
  return JSON.stringify(action);
}
