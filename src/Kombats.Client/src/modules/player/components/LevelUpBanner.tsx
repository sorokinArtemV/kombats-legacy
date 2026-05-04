import { usePlayerStore } from '../store';

export function LevelUpBanner() {
  const level = usePlayerStore((s) => s.pendingLevelUpLevel);
  const setLevel = usePlayerStore((s) => s.setPendingLevelUpLevel);

  if (level === null) return null;

  return (
    <div
      className="flex items-center justify-between gap-4 rounded-md border-[0.5px] px-4 py-3 backdrop-blur-[20px]"
      style={{
        borderColor: 'color-mix(in srgb, var(--color-kombats-jade) 45%, transparent)',
        background: 'color-mix(in srgb, var(--color-kombats-jade) 12%, var(--color-glass))',
      }}
    >
      <div className="flex flex-col gap-0.5">
        <span className="font-display text-[13px] font-semibold uppercase tracking-[0.18em] text-kombats-jade-light">
          Level Up · {level}
        </span>
        <span className="text-[11px] uppercase tracking-[0.18em] text-text-muted">
          Spend your new stat points below
        </span>
      </div>
      <button
        type="button"
        onClick={() => setLevel(null)}
        className="rounded-sm border-[0.5px] border-border-emphasis px-3 py-1 text-[10px] uppercase tracking-[0.18em] text-text-secondary transition-colors duration-150 hover:border-accent-muted hover:text-accent-text"
      >
        Dismiss
      </button>
    </div>
  );
}
