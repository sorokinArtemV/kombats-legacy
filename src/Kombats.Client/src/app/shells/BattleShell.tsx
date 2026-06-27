import { useEffect } from 'react';
import { Outlet, useParams } from 'react-router';
import { useBattleConnection, useBattlePhase } from '@/modules/battle/hooks';

export function BattleShell() {
  const { battleId } = useParams<{ battleId: string }>();

  return (
    <div className="flex h-full min-h-0 flex-col overflow-hidden">
      {battleId && <BattleConnectionHost battleId={battleId} />}
      <BattleUnloadGuard />
      <Outlet />
    </div>
  );
}

/**
 * Owns the battle hub connection for the entire /battle/:battleId/* subtree.
 * Mounted above both the live battle screen and the result screen so the
 * battle store (phase=Ended, feed, outcome) survives the hand-off.
 */
function BattleConnectionHost({ battleId }: { battleId: string }) {
  useBattleConnection(battleId);
  return null;
}

// Phases where closing/refreshing the tab will forfeit the active turn.
// Intentionally excludes 'Ended' (nothing to lose), 'Idle'/'Connecting'/
// 'WaitingForJoin' (no battle yet), 'ConnectionLost'/'Error' (already
// degraded — don't double-warn).
const ACTIVE_BATTLE_PHASES = new Set(['ArenaOpen', 'TurnOpen', 'Submitted', 'Resolving']);

/**
 * beforeunload warning during an active turn. The browser-rendered dialog is
 * the only thing we can show here — modern browsers ignore the returnValue
 * string and show their own generic prompt. That's fine; the point is to
 * catch accidental tab closes during a live battle.
 */
function BattleUnloadGuard() {
  const phase = useBattlePhase();
  const active = ACTIVE_BATTLE_PHASES.has(phase);
  useEffect(() => {
    if (!active) return;
    const handler = (event: BeforeUnloadEvent) => {
      event.preventDefault();
      // Legacy browsers require returnValue to be set for the prompt to show.
      event.returnValue = '';
    };
    window.addEventListener('beforeunload', handler);
    return () => window.removeEventListener('beforeunload', handler);
  }, [active]);
  return null;
}
