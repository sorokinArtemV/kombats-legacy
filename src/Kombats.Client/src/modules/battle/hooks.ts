import { useEffect, useCallback } from 'react';
import { useShallow } from 'zustand/react/shallow';
import { battleHubManager } from '@/app/transport-init';
import { useAuthStore } from '@/modules/auth/store';
import { useChatStore } from '@/modules/chat/store';
import { useBattleStore } from './store';
import { buildActionPayload, isValidBlockPair } from './zones';
import type { BattleZone, BattleSnapshotRealtime } from '@/types/battle';

function getOpponentId(
  snapshot: BattleSnapshotRealtime,
  myIdentityId: string | null,
): string | null {
  if (!myIdentityId) return null;
  if (snapshot.playerAId === myIdentityId) return snapshot.playerBId;
  if (snapshot.playerBId === myIdentityId) return snapshot.playerAId;
  return null;
}

/**
 * Manages the battle hub connection lifecycle for a specific battle.
 * Mount when entering battle; unmount to disconnect.
 */
export function useBattleConnection(battleId: string): void {
  useEffect(() => {
    let disposed = false;

    const store = useBattleStore.getState();
    store.startBattle(battleId);

    const applySnapshot = (snapshot: BattleSnapshotRealtime) => {
      if (disposed) return;
      useBattleStore.getState().handleSnapshot(snapshot);

      const myId = useAuthStore.getState().userIdentityId;
      const opponentId = getOpponentId(snapshot, myId);
      if (opponentId) {
        useChatStore.getState().setSuppressedOpponent(opponentId);
      }
    };

    battleHubManager.setEventHandlers({
      onBattleReady: (data) => {
        if (disposed) return;
        useBattleStore.getState().handleBattleReady(data);
        const myId = useAuthStore.getState().userIdentityId;
        if (myId) {
          const opponentId = data.playerAId === myId ? data.playerBId : data.playerAId;
          useChatStore.getState().setSuppressedOpponent(opponentId);
        }
      },
      onTurnOpened: (data) => {
        if (disposed) return;
        useBattleStore.getState().handleTurnOpened(data);
      },
      onPlayerDamaged: (data) => {
        if (disposed) return;
        useBattleStore.getState().handlePlayerDamaged(data);
      },
      onTurnResolved: (data) => {
        if (disposed) return;
        useBattleStore.getState().handleTurnResolved(data);
      },
      onBattleStateUpdated: (data) => {
        if (disposed) return;
        useBattleStore.getState().handleStateUpdated(data);
      },
      onBattleEnded: (data) => {
        if (disposed) return;
        useBattleStore.getState().handleBattleEnded(data);
        useChatStore.getState().clearSuppressedOpponent();
      },
      onBattleFeedUpdated: (data) => {
        if (disposed) return;
        useBattleStore.getState().handleFeedUpdated(data);
      },
      onBattleConnectionLost: () => {
        if (disposed) return;
        useBattleStore.getState().handleConnectionLost();
      },
      onConnectionStateChanged: (state) => {
        if (disposed) return;
        useBattleStore.getState().setConnectionState(state);

        if (state === 'connected') {
          const current = useBattleStore.getState();
          if (current.phase === 'Connecting') {
            current.handleConnected();
          } else if (current.phase === 'ConnectionLost') {
            current.handleReconnected();
            battleHubManager
              .joinBattle(battleId)
              .then((snapshot) => {
                applySnapshot(snapshot);
              })
              .catch(() => {
                if (disposed) return;
                useBattleStore.getState().handleError('Failed to rejoin battle after reconnect');
              });
          }
        } else if (state === 'reconnecting') {
          useBattleStore.getState().handleConnectionLost();
        } else if (state === 'failed') {
          // Automatic reconnect budget exhausted while the battle was live.
          // Surface this as a terminal battle error instead of leaving the
          // user stuck on a "reconnecting…" banner that will never resolve.
          const current = useBattleStore.getState();
          if (current.phase !== 'Ended' && current.phase !== 'Idle') {
            current.handleError('Connection to the battle was lost and could not be restored.');
          }
        }
      },
    });

    battleHubManager
      .connect()
      .then(() => {
        if (disposed) return;
        useBattleStore.getState().handleConnected();
        return battleHubManager.joinBattle(battleId);
      })
      .then((snapshot) => {
        if (snapshot) applySnapshot(snapshot);
      })
      .catch((err: unknown) => {
        if (disposed) return;
        const message = err instanceof Error ? err.message : 'Failed to connect to battle';
        useBattleStore.getState().handleError(message);
      });

    return () => {
      disposed = true;
      battleHubManager.setEventHandlers({});
      battleHubManager.disconnect().catch(() => {});
      useBattleStore.getState().reset();
      useChatStore.getState().clearSuppressedOpponent();
    };
  }, [battleId]);
}

// ---------------------------------------------------------------------------
// Focused selectors
// ---------------------------------------------------------------------------

export function useBattlePhase() {
  return useBattleStore((s) => s.phase);
}

export function useBattleConnectionState() {
  return useBattleStore((s) => s.connectionState);
}

export function useBattleTurn() {
  return useBattleStore(
    useShallow((s) => ({
      turnIndex: s.turnIndex,
      deadlineUtc: s.deadlineUtc,
    })),
  );
}

export function useBattleHp() {
  return useBattleStore(
    useShallow((s) => ({
      playerAHp: s.playerAHp,
      playerBHp: s.playerBHp,
      playerAMaxHp: s.playerAMaxHp,
      playerBMaxHp: s.playerBMaxHp,
    })),
  );
}

export function useBattleResult() {
  return useBattleStore(
    useShallow((s) => ({
      endReason: s.endReason,
      winnerPlayerId: s.winnerPlayerId,
      xpAwarded: s.xpAwarded,
      ratingDelta: s.ratingDelta,
    })),
  );
}

export function useBattleFeed() {
  return useBattleStore((s) => s.feedEntries);
}

/**
 * Battle action selectors and submit helper.
 */
export function useBattleActions() {
  const selectedAttackZone = useBattleStore((s) => s.selectedAttackZone);
  const selectedBlockPair = useBattleStore((s) => s.selectedBlockPair);
  const isSubmitting = useBattleStore((s) => s.isSubmitting);
  const phase = useBattleStore((s) => s.phase);

  const selectAttackZone = useCallback((zone: BattleZone) => {
    useBattleStore.getState().selectAttackZone(zone);
  }, []);

  const selectBlockPair = useCallback((pair: [BattleZone, BattleZone]) => {
    if (!isValidBlockPair(pair[0], pair[1])) return;
    useBattleStore.getState().selectBlockPair(pair);
  }, []);

  const canSubmit =
    phase === 'TurnOpen' &&
    selectedAttackZone !== null &&
    selectedBlockPair !== null &&
    !isSubmitting;

  const submitAction = useCallback(async () => {
    const store = useBattleStore.getState();
    if (
      store.phase !== 'TurnOpen' ||
      !store.selectedAttackZone ||
      !store.selectedBlockPair ||
      store.isSubmitting ||
      !store.battleId
    ) {
      return;
    }

    const payload = buildActionPayload(store.selectedAttackZone, store.selectedBlockPair);

    store.setSubmitting(true);
    try {
      await battleHubManager.submitTurnAction(store.battleId, store.turnIndex, payload);
    } catch {
      useBattleStore.getState().setSubmitting(false);
    }
  }, []);

  return {
    selectedAttackZone,
    selectedBlockPair,
    isSubmitting,
    canSubmit,
    selectAttackZone,
    selectBlockPair,
    submitAction,
  };
}
