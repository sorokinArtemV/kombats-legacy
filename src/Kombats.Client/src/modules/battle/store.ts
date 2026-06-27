import { create } from 'zustand';
import { useAuthStore } from '@/modules/auth/store';
import type { ConnectionState } from '@/transport/signalr/connection-state';
import type { Uuid, DateTimeOffset } from '@/types/common';
import type {
  BattleZone,
  BattleEndReasonRealtime,
  BattleRulesetRealtime,
  BattleSnapshotRealtime,
  BattleReadyRealtime,
  TurnOpenedRealtime,
  PlayerDamagedRealtime,
  TurnResolvedRealtime,
  TurnResolutionLogRealtime,
  BattleStateUpdatedRealtime,
  BattleEndedRealtime,
  BattleFeedEntry,
  BattleFeedUpdate,
} from '@/types/battle';
import { pickArenaFromBattleId, type ArenaId } from './arena-id';

// ---------------------------------------------------------------------------
// Phase model
// ---------------------------------------------------------------------------

export type BattlePhase =
  | 'Idle'
  | 'Connecting'
  | 'WaitingForJoin'
  | 'ArenaOpen'
  | 'TurnOpen'
  | 'Submitted'
  | 'Resolving'
  | 'Ended'
  | 'ConnectionLost'
  | 'Error';

// ---------------------------------------------------------------------------
// State shape
// ---------------------------------------------------------------------------

interface BattleState {
  // Phase
  phase: BattlePhase;

  // Battle identity
  battleId: Uuid | null;
  playerAId: Uuid | null;
  playerBId: Uuid | null;
  playerAName: string | null;
  playerBName: string | null;
  ruleset: BattleRulesetRealtime | null;
  currentArena: ArenaId | null;

  // Turn
  turnIndex: number;
  deadlineUtc: DateTimeOffset | null;

  // Selections
  selectedAttackZone: BattleZone | null;
  selectedBlockPair: [BattleZone, BattleZone] | null;
  isSubmitting: boolean;

  // HP
  playerAHp: number | null;
  playerBHp: number | null;
  playerAMaxHp: number | null;
  playerBMaxHp: number | null;

  // Result
  endReason: BattleEndReasonRealtime | null;
  winnerPlayerId: Uuid | null;

  // Reward deltas captured from BattleEnded. Both nullable — the result
  // screen renders each row only when its field is present, so the same
  // panel works whether or not the backend has shipped the matching
  // emit. See `types/battle.ts` BattleEndedRealtime for the wire shape.
  xpAwarded: number | null;
  ratingDelta: number | null;

  // Resolution
  lastResolution: TurnResolvedRealtime | null;

  // Per-turn structured resolution history accumulated across the live
  // battle. Appended in handleTurnResolved (when log is non-null), used by
  // RoundMap to draw the per-round zone grid. Wiped by reset()/startBattle()
  // via INITIAL_STATE.
  turnHistory: TurnResolutionLogRealtime[];

  // Feed
  feedEntries: BattleFeedEntry[];

  // Archived feed of the most recently finished battle. Captured at
  // BattleEnded and kept in sync through the trailing end-of-battle
  // narration entries that arrive immediately after. Survives reset() so
  // the BottomDock can keep the BATTLE LOG tab around once the user
  // returns to /lobby; cleared by the next startBattle() or by
  // clearLastBattleLog() (the user dismissing the tab).
  // Carries the two player names alongside entries so the lobby tab can
  // render BattleLog/RoundMap with the same identities the live battle
  // showed — the active playerAName/playerBName fields are wiped by reset().
  lastBattleLog: {
    battleId: Uuid;
    entries: BattleFeedEntry[];
    playerAName: string | null;
    playerBName: string | null;
  } | null;

  // Archived turn-history snapshot of the most recently finished battle —
  // mirror of lastBattleLog for the RoundMap. Captured at BattleEnded,
  // survives reset() so the BATTLE LOG tab on /lobby can still render the
  // round grid. Cleared by the next startBattle() or by
  // clearLastTurnHistory() (the user dismissing the tab).
  lastTurnHistory: TurnResolutionLogRealtime[] | null;

  // Connection
  connectionState: ConnectionState;

  // Error
  lastError: string | null;

  // Actions
  setConnectionState: (state: ConnectionState) => void;
  startBattle: (battleId: Uuid) => void;
  handleConnected: () => void;
  handleSnapshot: (snapshot: BattleSnapshotRealtime) => void;
  handleBattleReady: (data: BattleReadyRealtime) => void;
  handleTurnOpened: (data: TurnOpenedRealtime) => void;
  handlePlayerDamaged: (data: PlayerDamagedRealtime) => void;
  handleTurnResolved: (data: TurnResolvedRealtime) => void;
  handleStateUpdated: (data: BattleStateUpdatedRealtime) => void;
  handleBattleEnded: (data: BattleEndedRealtime) => void;
  handleFeedUpdated: (data: BattleFeedUpdate) => void;
  handleConnectionLost: () => void;
  handleReconnected: () => void;
  handleError: (message: string) => void;
  selectAttackZone: (zone: BattleZone) => void;
  selectBlockPair: (pair: [BattleZone, BattleZone]) => void;
  setSubmitting: (submitting: boolean) => void;
  clearSelections: () => void;
  clearLastBattleLog: () => void;
  clearLastTurnHistory: () => void;
  reset: () => void;
}

// Upper bound on the live battle feed. Reconnect backfills already dedupe
// by key, but a very long battle would otherwise grow unboundedly. 500 is
// well above a typical fight length and matches the chat buffer cap.
const MAX_FEED_ENTRIES = 500;

// Pick the local player's XP from the per-side payload. Returns null when
// identity isn't resolvable (no winner, auth unhydrated, or local player
// not part of the battle), which collapses to "no XP row" on the result
// screen.
function selectXpForPlayer(data: BattleEndedRealtime, myIdentityId: string | null): number | null {
  if (!myIdentityId || data.winnerPlayerId === null) return null;
  const xp = data.winnerPlayerId === myIdentityId ? data.winnerXp : data.loserXp;
  return xp ?? null;
}

// ---------------------------------------------------------------------------
// Initial state
// ---------------------------------------------------------------------------

const INITIAL_STATE = {
  phase: 'Idle' as BattlePhase,
  battleId: null,
  playerAId: null,
  playerBId: null,
  playerAName: null,
  playerBName: null,
  ruleset: null,
  currentArena: null as ArenaId | null,
  turnIndex: 0,
  deadlineUtc: null,
  selectedAttackZone: null,
  selectedBlockPair: null,
  isSubmitting: false,
  playerAHp: null,
  playerBHp: null,
  playerAMaxHp: null,
  playerBMaxHp: null,
  endReason: null,
  winnerPlayerId: null,
  xpAwarded: null as number | null,
  ratingDelta: null as number | null,
  lastResolution: null,
  turnHistory: [] as TurnResolutionLogRealtime[],
  feedEntries: [] as BattleFeedEntry[],
  lastBattleLog: null as {
    battleId: Uuid;
    entries: BattleFeedEntry[];
    playerAName: string | null;
    playerBName: string | null;
  } | null,
  lastTurnHistory: null as TurnResolutionLogRealtime[] | null,
  connectionState: 'disconnected' as ConnectionState,
  lastError: null,
};

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function serverPhaseToLocal(serverPhase: string, currentPhase: BattlePhase): BattlePhase {
  switch (serverPhase) {
    case 'ArenaOpen':
      return 'ArenaOpen';
    case 'TurnOpen':
      return currentPhase === 'Submitted' ? 'Submitted' : 'TurnOpen';
    case 'Resolving':
      return 'Resolving';
    case 'Ended':
      return 'Ended';
    default:
      return currentPhase;
  }
}

// ---------------------------------------------------------------------------
// Store
// ---------------------------------------------------------------------------

export const useBattleStore = create<BattleState>()((set, get) => ({
  ...INITIAL_STATE,

  setConnectionState: (connectionState) => set({ connectionState }),

  startBattle: (battleId) =>
    set({
      ...INITIAL_STATE,
      // Preserve any live connection state established by the caller before
      // startBattle ran (e.g., useBattleConnection wires the SignalR manager
      // and may receive the 'connected' state-change event before invoking
      // startBattle on a fast remount). Spreading INITIAL_STATE would
      // otherwise clobber connectionState back to 'disconnected' and mask a
      // real connection from the rest of the app.
      connectionState: get().connectionState,
      phase: 'Connecting',
      battleId,
    }),

  handleConnected: () => {
    const state = get();
    if (state.phase === 'Connecting') {
      set({ phase: 'WaitingForJoin' });
    }
  },

  handleSnapshot: (snapshot) =>
    set({
      battleId: snapshot.battleId,
      playerAId: snapshot.playerAId,
      playerBId: snapshot.playerBId,
      playerAName: snapshot.playerAName,
      playerBName: snapshot.playerBName,
      ruleset: snapshot.ruleset,
      turnIndex: snapshot.turnIndex,
      deadlineUtc: snapshot.deadlineUtc,
      playerAHp: snapshot.playerAHp,
      playerBHp: snapshot.playerBHp,
      playerAMaxHp: snapshot.playerAMaxHp,
      playerBMaxHp: snapshot.playerBMaxHp,
      endReason: snapshot.endedReason,
      phase: serverPhaseToLocal(snapshot.phase, get().phase),
      selectedAttackZone: null,
      selectedBlockPair: null,
      isSubmitting: false,
      // Server value wins; otherwise preserve any arena BattleReady
      // already locked; otherwise compute the deterministic hash fallback
      // so a snapshot-only entry path (joinBattle without a subsequent
      // BattleReady — e.g., user navigates straight to /battle/:id mid-
      // match) still resolves to a stable arena.
      currentArena:
        snapshot.arenaId ?? get().currentArena ?? pickArenaFromBattleId(snapshot.battleId),
    }),

  handleBattleReady: (data) =>
    set({
      battleId: data.battleId,
      playerAId: data.playerAId,
      playerBId: data.playerBId,
      playerAName: data.playerAName,
      playerBName: data.playerBName,
      phase: 'ArenaOpen',
      // Symmetric with handleSnapshot: server value > preserve existing >
      // deterministic hash on battleId. Preserving an existing arena means
      // a BattleReady arriving after a snapshot already populated the
      // store doesn't re-roll. Both clients land on the same fallback for
      // the same battleId.
      currentArena:
        data.arenaId ?? get().currentArena ?? pickArenaFromBattleId(data.battleId),
    }),

  handleTurnOpened: (data) =>
    set({
      turnIndex: data.turnIndex,
      deadlineUtc: data.deadlineUtc,
      phase: 'TurnOpen',
      selectedAttackZone: null,
      selectedBlockPair: null,
      isSubmitting: false,
      lastResolution: null,
    }),

  handlePlayerDamaged: (data) => {
    const state = get();
    if (data.playerId === state.playerAId) {
      set({ playerAHp: data.remainingHp });
    } else if (data.playerId === state.playerBId) {
      set({ playerBHp: data.remainingHp });
    }
  },

  handleTurnResolved: (data) => {
    const state = get();
    // Append to the per-round history only when the structured log is
    // present. NoAction-only turns and any future event with a missing log
    // are skipped — RoundMap draws an empty row for them implicitly by
    // having no entry to render.
    const nextHistory = data.log ? [...state.turnHistory, data.log] : state.turnHistory;

    // Mirror lastBattleLog's re-sync behaviour (handleFeedUpdated): if the
    // archive snapshot was already taken (battle ending sent BattleEnded
    // before this TurnResolved due to wire reordering or future routing
    // changes), fold the late turn into lastTurnHistory so the lobby
    // RoundMap doesn't lose the killing-blow row. Gated on battleId match
    // to never bleed turns from a new battle into a previous archive.
    const archive = state.lastTurnHistory;
    const updatedArchive =
      data.log && archive && state.battleId && data.battleId === state.battleId
        ? [...archive, data.log]
        : archive;

    set({
      lastResolution: data,
      turnHistory: nextHistory,
      phase: 'Resolving',
      ...(updatedArchive !== archive ? { lastTurnHistory: updatedArchive } : {}),
    });
  },

  handleStateUpdated: (data) =>
    set({
      playerAId: data.playerAId,
      playerBId: data.playerBId,
      playerAName: data.playerAName,
      playerBName: data.playerBName,
      ruleset: data.ruleset,
      turnIndex: data.turnIndex,
      deadlineUtc: data.deadlineUtc,
      playerAHp: data.playerAHp,
      playerBHp: data.playerBHp,
      playerAMaxHp: data.playerAMaxHp,
      playerBMaxHp: data.playerBMaxHp,
      endReason: data.endedReason,
      phase: serverPhaseToLocal(data.phase, get().phase),
    }),

  handleBattleEnded: (data) => {
    const state = get();
    set({
      // Snapshot the feed for the post-lobby BATTLE LOG tab. The trailing
      // BattleFeedUpdated (battle.end / defeat / commentary entries) arrives
      // AFTER BattleEnded on the wire (see BattleHubRelay.cs), so the
      // snapshot captured here is incomplete on its own — handleFeedUpdated
      // mirrors any subsequent entries into lastBattleLog while battleId is
      // still set.
      lastBattleLog: state.battleId
        ? {
            battleId: state.battleId,
            entries: state.feedEntries,
            playerAName: state.playerAName,
            playerBName: state.playerBName,
          }
        : state.lastBattleLog,
      // RoundMap snapshot. The expected ordering is TurnResolved →
      // BattleEnded so this captures every turn including the killing blow,
      // but a late TurnResolved arriving after BattleEnded is reconciled by
      // handleTurnResolved appending into lastTurnHistory directly (mirrors
      // the lastBattleLog pattern in handleFeedUpdated).
      lastTurnHistory: state.turnHistory,
      endReason: data.reason,
      winnerPlayerId: data.winnerPlayerId,
      // Wire carries per-side XP (winnerXp / loserXp); pick the row that
      // belongs to the local player so the result screen reads a single
      // number. Falls back to null when the field is missing or when we
      // can't resolve identity (auth store unhydrated, system error with
      // no winner). `?? null` collapses absent + explicit-null + undefined
      // selector lookup to the same value.
      xpAwarded: selectXpForPlayer(data, useAuthStore.getState().userIdentityId),
      ratingDelta: data.ratingDelta ?? null,
      phase: 'Ended',
      // Final turn's per-turn resolution belongs to the live-battle UI
      // only. Leaving it set bleeds into the result screen, where a
      // TurnResultPanel could briefly flash the last turn's attack/block
      // detail underneath the outcome celebration.
      lastResolution: null,
    });
  },

  handleFeedUpdated: (data) => {
    const state = get();
    const existingKeys = new Set(state.feedEntries.map((e) => e.key));
    const newEntries = data.entries.filter((e) => !existingKeys.has(e.key));
    if (newEntries.length === 0) return;
    const merged = [...state.feedEntries, ...newEntries];
    const trimmed = merged.length > MAX_FEED_ENTRIES ? merged.slice(-MAX_FEED_ENTRIES) : merged;

    // Keep the archive in sync while it belongs to the current battle so the
    // post-BattleEnded entries (BATTLE END separator, defeat/victory lines,
    // closing commentary) are part of the snapshot the lobby tab reads.
    const archive = state.lastBattleLog;
    const updatedArchive =
      archive && state.battleId && archive.battleId === state.battleId
        ? {
            battleId: archive.battleId,
            entries: trimmed,
            playerAName: archive.playerAName,
            playerBName: archive.playerBName,
          }
        : archive;

    set({
      feedEntries: trimmed,
      ...(updatedArchive !== archive ? { lastBattleLog: updatedArchive } : {}),
    });
  },

  handleConnectionLost: () => {
    const state = get();
    // Only transition from phases representing an actively-confirmed battle.
    // Setup phases (Connecting, WaitingForJoin) are guarded by the loading
    // screen and have their own failure path via joinBattle rejection →
    // handleError; a stray BFF-emitted BattleConnectionLost during setup
    // (e.g., from BattleHubRelay disposing a prior downstream connection
    // before opening the new one) would otherwise leak the connection
    // banner onto the action UI for the 1–2s before the snapshot lands.
    // Terminal phases (Idle, Ended, Error, ConnectionLost itself) decline.
    if (
      state.phase === 'ArenaOpen' ||
      state.phase === 'TurnOpen' ||
      state.phase === 'Submitted' ||
      state.phase === 'Resolving'
    ) {
      set({ phase: 'ConnectionLost' });
    }
  },

  handleReconnected: () => {
    const state = get();
    if (state.phase === 'ConnectionLost') {
      // Will be reconciled by BattleStateUpdated from server after rejoin
      set({ phase: 'WaitingForJoin' });
    }
  },

  handleError: (message) => set({ phase: 'Error', lastError: message }),

  selectAttackZone: (zone) => set({ selectedAttackZone: zone }),

  selectBlockPair: (pair) => set({ selectedBlockPair: pair }),

  setSubmitting: (submitting) => {
    if (submitting) {
      set({ isSubmitting: true, phase: 'Submitted' });
    } else {
      // Only revert to TurnOpen if still in Submitted — don't stomp
      // a Resolving/Ended state that arrived while the invoke was failing.
      const current = get().phase;
      set({
        isSubmitting: false,
        phase: current === 'Submitted' ? 'TurnOpen' : current,
      });
    }
  },

  clearSelections: () =>
    set({
      selectedAttackZone: null,
      selectedBlockPair: null,
    }),

  clearLastBattleLog: () => set({ lastBattleLog: null }),

  clearLastTurnHistory: () => set({ lastTurnHistory: null }),

  // Preserves lastBattleLog and lastTurnHistory so the BATTLE LOG tab can
  // survive the BattleConnectionHost unmount that fires when the user
  // returns to /lobby. Both archives are cleared by the next startBattle
  // (via INITIAL_STATE spread) or by their respective clear actions when
  // the user dismisses the tab.
  reset: () => {
    const state = get();
    set({
      ...INITIAL_STATE,
      lastBattleLog: state.lastBattleLog,
      lastTurnHistory: state.lastTurnHistory,
    });
  },
}));
