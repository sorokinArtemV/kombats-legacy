import {
  useBattlePhase,
  useBattleTurn,
  useBattleActions,
  useBattleConnectionState,
} from '../hooks';
import { TurnTimer } from './TurnTimer';
import { LockInButton } from './LockInButton';

// DESIGN_REFERENCE.md §5.17 — meta row: ROUND on the left, timer + lock-in
// grouped together on the right so they read as a single "time left / act"
// pair instead of being stretched across the panel by space-between.
export function CombatMetaRow() {
  const { turnIndex } = useBattleTurn();
  const phase = useBattlePhase();
  const actions = useBattleActions();
  const connectionState = useBattleConnectionState();

  const connectionBlocked = connectionState !== 'connected';
  const isTurnOpen = phase === 'TurnOpen';
  const isWaiting = phase === 'Submitted' || phase === 'Resolving';
  const canGo = actions.canSubmit && !connectionBlocked;

  return (
    <div className="grid items-center px-1 py-0.5" style={{ gridTemplateColumns: '1fr auto 1fr' }}>
      <div className="flex items-center gap-2 text-[11px] uppercase tracking-[0.18em] text-text-muted">
        <span>Round</span>
        <span className="font-display font-black tabular-nums text-accent-text">
          {turnIndex > 0 ? turnIndex : '—'}
        </span>
      </div>

      <div className="flex items-center justify-center">
        <TurnTimer />
      </div>

      <div className="flex items-center justify-end">
        {isTurnOpen ? (
          <LockInButton onClick={actions.submitAction} disabled={!canGo} />
        ) : isWaiting ? (
          <span className="text-[11px] uppercase tracking-[0.18em] text-accent-text">
            Submitted
          </span>
        ) : (
          <span className="inline-flex items-center gap-1.5 text-[10px] uppercase tracking-[0.2em] text-text-secondary">
            <span
              aria-hidden
              className="inline-block h-1.5 w-1.5 rounded-full"
              style={{ background: 'var(--color-accent-muted)' }}
            />
            Standby
          </span>
        )}
      </div>
    </div>
  );
}
