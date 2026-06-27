import { useBattlePhase } from '../hooks';

export function CombatPanelHeader() {
  const phase = useBattlePhase();
  const title =
    phase === 'Submitted' || phase === 'Resolving'
      ? 'Awaiting Opponent'
      : phase === 'Ended'
        ? 'Battle Concluded'
        : 'Select Attack & Block';
  return (
    <header className="mb-3 text-center">
      <h3
        className="font-display text-[15px] font-black uppercase tracking-[0.24em] text-accent-text"
        style={{ textShadow: 'var(--shadow-title-soft)' }}
      >
        {title}
      </h3>
    </header>
  );
}
