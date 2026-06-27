import type { Uuid, DateTimeOffset } from './common';
import type { ArenaId } from '../modules/battle/arena-id';

// ---------------------------------------------------------------------------
// Enums
// ---------------------------------------------------------------------------

export type BattleZone = 'Head' | 'Chest' | 'Belly' | 'Waist' | 'Legs';

export type BattlePhaseRealtime = 'ArenaOpen' | 'TurnOpen' | 'Resolving' | 'Ended';

export type BattleEndReasonRealtime =
  | 'Normal'
  | 'DoubleForfeit'
  | 'Timeout'
  | 'Cancelled'
  | 'AdminForced'
  | 'SystemError'
  | 'Unknown';

export type AttackOutcomeRealtime =
  | 'NoAction'
  | 'Dodged'
  | 'Blocked'
  | 'Hit'
  | 'CriticalHit'
  | 'CriticalBypassBlock'
  | 'CriticalHybridBlocked';

// ---------------------------------------------------------------------------
// Feed enums
// ---------------------------------------------------------------------------

export type FeedEntryKind =
  | 'AttackHit'
  | 'AttackCrit'
  | 'AttackDodge'
  | 'AttackBlock'
  | 'AttackNoAction'
  | 'BattleStart'
  | 'BattleEndVictory'
  | 'BattleEndDraw'
  | 'BattleEndForfeit'
  | 'DefeatKnockout'
  | 'CommentaryFirstBlood'
  | 'CommentaryMutualMiss'
  | 'CommentaryStalemate'
  | 'CommentaryNearDeath'
  | 'CommentaryBigHit'
  | 'CommentaryKnockout'
  | 'CommentaryDraw';

export type FeedEntrySeverity = 'Normal' | 'Important' | 'Critical';

export type FeedEntryTone =
  | 'Neutral'
  | 'Aggressive'
  | 'Defensive'
  | 'Dramatic'
  | 'System'
  | 'Flavor';

// ---------------------------------------------------------------------------
// Realtime event payloads
// ---------------------------------------------------------------------------

export interface BattleRulesetRealtime {
  turnSeconds: number;
  noActionLimit: number | null;
}

export interface BattleSnapshotRealtime {
  battleId: Uuid;
  playerAId: Uuid;
  playerBId: Uuid;
  ruleset: BattleRulesetRealtime;
  phase: BattlePhaseRealtime;
  turnIndex: number;
  deadlineUtc: DateTimeOffset;
  noActionStreakBoth: number;
  lastResolvedTurnIndex: number;
  endedReason: BattleEndReasonRealtime | null;
  version: number;
  playerAHp: number | null;
  playerBHp: number | null;
  playerAName: string | null;
  playerBName: string | null;
  playerAMaxHp: number | null;
  playerBMaxHp: number | null;
  /**
   * Optional. The arena for the current battle. See
   * BattleReadyRealtime.arenaId for semantics. On snapshot
   * (e.g., after reconnect), this arrives with the rest of the
   * battle state and is sticky for the lifetime of the match.
   */
  arenaId?: ArenaId | null;
}

export interface BattleReadyRealtime {
  battleId: Uuid;
  playerAId: Uuid;
  playerBId: Uuid;
  playerAName: string | null;
  playerBName: string | null;
  /**
   * Optional. The arena chosen for this battle. When omitted by
   * the server, the client falls back to a deterministic pick
   * keyed on battleId (see pickArenaFromBattleId). Once backend
   * ships this field, the fallback stops firing.
   */
  arenaId?: ArenaId | null;
}

export interface TurnOpenedRealtime {
  battleId: Uuid;
  turnIndex: number;
  deadlineUtc: DateTimeOffset;
}

export interface PlayerDamagedRealtime {
  battleId: Uuid;
  playerId: Uuid;
  damage: number;
  remainingHp: number;
  turnIndex: number;
}

export interface AttackResolutionRealtime {
  attackerId: Uuid;
  defenderId: Uuid;
  turnIndex: number;
  attackZone: string | null;
  defenderBlockPrimary: string | null;
  defenderBlockSecondary: string | null;
  wasBlocked: boolean;
  wasCrit: boolean;
  outcome: AttackOutcomeRealtime;
  damage: number;
}

export interface TurnResolutionLogRealtime {
  battleId: Uuid;
  turnIndex: number;
  // Wire names: backend properties AtoB/BtoA camel-cased to atoB/btoA
  // (SignalR JsonHubProtocol JsonNamingPolicy.CamelCase only lowers the
  // first character — it does not re-case interior letters).
  atoB: AttackResolutionRealtime;
  btoA: AttackResolutionRealtime;
}

export interface TurnResolvedRealtime {
  battleId: Uuid;
  turnIndex: number;
  playerAAction: string;
  playerBAction: string;
  log: TurnResolutionLogRealtime | null;
}

export interface BattleStateUpdatedRealtime {
  battleId: Uuid;
  playerAId: Uuid;
  playerBId: Uuid;
  ruleset: BattleRulesetRealtime;
  phase: BattlePhaseRealtime;
  turnIndex: number;
  deadlineUtc: DateTimeOffset;
  noActionStreakBoth: number;
  lastResolvedTurnIndex: number;
  endedReason: BattleEndReasonRealtime | null;
  version: number;
  playerAHp: number | null;
  playerBHp: number | null;
  playerAName: string | null;
  playerBName: string | null;
  playerAMaxHp: number | null;
  playerBMaxHp: number | null;
}

export interface BattleEndedRealtime {
  battleId: Uuid;
  reason: BattleEndReasonRealtime;
  winnerPlayerId: Uuid | null;
  endedAt: DateTimeOffset;
  // Per-side XP captured at the moment the battle ended. Both populated when
  // there is a winner, both null otherwise (draw, double forfeit, system
  // error). The result screen picks the right side via the selector
  // `winnerPlayerId === myId ? winnerXp : loserXp` to render the XP row.
  winnerXp?: number | null;
  loserXp?: number | null;
  // Future reward fields. Optional because the backend does not emit them
  // yet (rating system unshipped; level / unspent points may flow through a
  // separate BFF projection). Result screen renders each row only when its
  // field is present.
  levelAfter?: number | null;
  unspentPointsAfter?: number | null;
  ratingDelta?: number | null;
}

// ---------------------------------------------------------------------------
// Feed
// ---------------------------------------------------------------------------

export interface BattleFeedEntry {
  key: string;
  battleId: Uuid;
  turnIndex: number;
  sequence: number;
  kind: FeedEntryKind;
  severity: FeedEntrySeverity;
  tone: FeedEntryTone;
  text: string;
}

export interface BattleFeedUpdate {
  battleId: Uuid;
  entries: BattleFeedEntry[];
}
