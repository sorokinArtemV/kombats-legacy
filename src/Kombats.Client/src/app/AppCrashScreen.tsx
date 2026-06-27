import { useBattleStore } from '@/modules/battle/store';
import mitsudamoeIcon from '@/ui/assets/icons/mitsudamoe.png';
import { selectRecoveryTarget } from './crash-recovery';

// Shown by the top-level ErrorBoundary (and the per-group route errorElements)
// when a render throws. In-memory state may be corrupt, so recovery always
// goes through a hard `window.location.assign` — that triggers a fresh
// bootstrap (auth silent-restore, GameStateLoader, hub re-connects). If the
// user was in a battle, we preserve that battleId in the URL so BattleGuard
// and BattleStateUpdated can reconcile them back into their match; otherwise
// we land them on the lobby.

function goTo(path: string): void {
  window.location.assign(path);
}

export function AppCrashScreen() {
  // Snapshot read — this component is rendered in the error state, which is a
  // terminal UI, so we don't need reactive subscription.
  const battleState = useBattleStore.getState();
  const { label, href, inBattle } = selectRecoveryTarget(battleState.battleId, battleState.phase);

  return (
    <div
      role="alert"
      aria-labelledby="app-crash-title"
      className="flex min-h-screen items-center justify-center bg-kombats-ink-navy px-6"
    >
      <div className="flex w-full max-w-md flex-col items-center gap-6 rounded-md border-[0.5px] border-border-subtle bg-glass p-10 text-center shadow-[var(--shadow-panel)] backdrop-blur-[20px]">
        <img
          src={mitsudamoeIcon}
          alt=""
          aria-hidden="true"
          width={100}
          height={100}
          className="opacity-35"
        />

        <h1
          id="app-crash-title"
          className="font-display text-[40px] font-bold uppercase leading-none tracking-[0.16em] text-accent-primary"
          // Cinzel title bloom per DESIGN_REFERENCE.md §3.4 — gold halo
          // text-shadow isn't expressible via a static Tailwind utility.
          style={{ textShadow: 'var(--shadow-title-neutral)' }}
        >
          Signal Lost
        </h1>

        <p className="font-display text-[11px] uppercase tracking-[0.24em] text-text-muted">
          Unexpected Error
        </p>

        <p className="max-w-sm text-sm leading-relaxed text-text-secondary">
          {inBattle
            ? 'The app hit an unexpected error. Your battle is still live on the server — rejoin to continue.'
            : 'The app hit an unexpected error. Return to the lobby to continue.'}
        </p>

        <div className="flex w-full flex-col items-stretch gap-3 sm:flex-row sm:justify-center">
          <button
            type="button"
            onClick={() => goTo(href)}
            className="inline-flex items-center justify-center rounded-md bg-accent-primary px-6 py-2.5 text-[13px] font-medium uppercase tracking-[0.18em] text-text-on-accent transition-colors duration-150 hover:bg-kombats-gold-light"
          >
            {label}
          </button>
          <button
            type="button"
            onClick={() => window.location.reload()}
            className="inline-flex items-center justify-center rounded-md border border-border-emphasis bg-transparent px-6 py-2.5 text-[13px] font-medium uppercase tracking-[0.18em] text-text-secondary transition-colors duration-150 hover:border-kombats-gold hover:text-kombats-gold"
          >
            Reload Page
          </button>
        </div>
      </div>
    </div>
  );
}
