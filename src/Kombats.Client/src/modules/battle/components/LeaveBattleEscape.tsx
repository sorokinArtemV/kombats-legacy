import { useNavigate } from 'react-router';
import { useBattleStore } from '../store';
import { usePlayerStore } from '@/modules/player/store';

interface LeaveBattleEscapeProps {
  // Optional element id whose copy describes the consequences of leaving
  // (e.g. the inline error banner explaining the terminal failure). Wired
  // through `aria-describedby` so screen readers announce the context after
  // the button label.
  describedBy?: string;
}

/**
 * Terminal battle-error escape. The BattleHub reached its `failed` state
 * (reconnect attempted and could not restore) or the store transitioned to
 * `phase: 'Error'` some other way. Without this button the user is stuck
 * staring at the error banner — the only way out was a hard refresh.
 *
 * Clicking it clears the battle store and fires the atomic post-battle
 * handoff on the player store so (a) stale `queueStatus.Matched.<battleId>`
 * refetches are suppressed and the BattleGuard does not bounce us back
 * into the broken battle, and (b) the lobby's usePostBattleRefresh flag
 * is set so XP/level reconcile on the next lobby mount.
 */
export function LeaveBattleEscape({ describedBy }: LeaveBattleEscapeProps = {}) {
  const navigate = useNavigate();
  const battleId = useBattleStore((s) => s.battleId);
  const returnFromBattle = usePlayerStore((s) => s.returnFromBattle);

  const handleLeave = () => {
    if (battleId) {
      returnFromBattle(battleId);
    } else {
      // No battleId means the store never transitioned past the pre-match
      // phases; the simpler path is enough.
      usePlayerStore.getState().setQueueStatus(null);
    }
    useBattleStore.getState().reset();
    navigate('/lobby');
  };

  return (
    <div className="flex justify-end">
      <button
        type="button"
        onClick={handleLeave}
        aria-describedby={describedBy}
        className="inline-flex items-center justify-center rounded-sm border border-kombats-crimson bg-transparent px-4 py-1.5 font-display text-[11px] uppercase tracking-[0.18em] text-kombats-crimson-light transition-colors duration-150 hover:bg-kombats-crimson hover:text-text-on-danger"
      >
        Leave Battle
      </button>
    </div>
  );
}
