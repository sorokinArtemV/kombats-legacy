import { usePlayerStore } from '@/modules/player/store';
import {
  useAllocateStats,
  STAT_KEYS,
  STAT_LABELS,
  type StatKey,
} from '@/modules/player/useAllocateStats';
import { Button } from '@/ui/components/Button';
import { StatIcon } from '@/ui/components/StatIcon';
import { OnboardingCard } from '../components/OnboardingCard';

const STAT_DESCRIPTIONS: Record<StatKey, string> = {
  strength: 'Increases physical attack damage',
  agility: 'Improves dodge chance and attack speed',
  intuition: 'Enhances critical hit rate',
  vitality: 'Increases maximum health points',
};

export function InitialStatsScreen() {
  const character = usePlayerStore((s) => s.character);
  const updateCharacter = usePlayerStore((s) => s.updateCharacter);

  const alloc = useAllocateStats({
    onSuccess: () => {
      // Onboarding-specific side effect: flip onboardingState to 'Ready'
      // so the OnboardingGuard redirects the user out to /lobby. The hook
      // has already merged the response stats into the character.
      const c = usePlayerStore.getState().character;
      if (c) updateCharacter({ ...c, onboardingState: 'Ready' });
    },
  });

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    alloc.submit();
  }

  // Brief: Confirm is disabled while there are still unspent points to allocate.
  const allPointsSpent = alloc.remaining === 0;

  return (
    <div className="flex flex-1 items-center justify-center p-6">
      <div className="w-full max-w-2xl rounded-md border-[0.5px] border-border-subtle bg-glass p-8 shadow-[var(--shadow-panel)] backdrop-blur-[20px] sm:p-10">
        <OnboardingCard
          eyebrow="Onboarding"
          title="Allocate Stats"
          subtitle="Spend your initial points across four attributes"
        >
          <form onSubmit={handleSubmit} className="flex w-full flex-col gap-6">
            {alloc.revisionConflict && (
              <div
                className="rounded-sm border-[0.5px] border-kombats-crimson/40 bg-kombats-crimson/10 px-3 py-2 text-[11px] uppercase tracking-[0.18em] text-kombats-crimson-light"
                role="alert"
              >
                Character was updated elsewhere. Your draft was discarded — please re-allocate.
              </div>
            )}

            <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
              {STAT_KEYS.map((stat) => (
                <StatCard
                  key={stat}
                  stat={stat}
                  baseValue={character?.[stat] ?? 3}
                  addedPoints={alloc.added[stat]}
                  onIncrement={() => alloc.increment(stat)}
                  onDecrement={() => alloc.decrement(stat)}
                  canIncrement={alloc.canIncrement}
                  canDecrement={alloc.canDecrementStat(stat)}
                  disabled={alloc.isPending}
                />
              ))}
            </div>

            <div className="flex items-center justify-between py-2">
              <span className="text-[11px] font-medium uppercase tracking-[0.18em] text-text-secondary">
                Points remaining
              </span>
              <span className="font-display text-base font-bold tabular-nums text-accent-primary">
                {alloc.remaining}
              </span>
            </div>

            {alloc.errorMessage && (
              <p className="text-center text-[12px] uppercase tracking-[0.18em] text-kombats-crimson-light">
                {alloc.errorMessage}
              </p>
            )}

            <div className="flex justify-center pt-2">
              <Button
                type="submit"
                variant="primary"
                size="lg"
                loading={alloc.isPending}
                disabled={!allPointsSpent}
              >
                Confirm Stats
              </Button>
            </div>
          </form>
        </OnboardingCard>
      </div>
    </div>
  );
}

interface StatCardProps {
  stat: StatKey;
  baseValue: number;
  addedPoints: number;
  onIncrement: () => void;
  onDecrement: () => void;
  canIncrement: boolean;
  canDecrement: boolean;
  disabled: boolean;
}

function StatCard({
  stat,
  baseValue,
  addedPoints,
  onIncrement,
  onDecrement,
  canIncrement,
  canDecrement,
  disabled,
}: StatCardProps) {
  const label = STAT_LABELS[stat];
  const total = baseValue + addedPoints;
  return (
    <div className="flex flex-col items-center gap-3 rounded-sm border-[0.5px] border-border-subtle bg-glass-subtle p-4 text-center">
      <StatIcon kind={stat} size={28} />

      <div className="flex flex-col items-center gap-1">
        <h3 className="font-display text-[13px] font-semibold uppercase tracking-[0.18em] text-kombats-gold">
          {label}
        </h3>
        <p className="text-[11px] leading-snug text-text-muted">{STAT_DESCRIPTIONS[stat]}</p>
      </div>

      <div className="mt-auto flex items-center gap-3">
        <button
          type="button"
          onClick={onDecrement}
          disabled={disabled || !canDecrement}
          aria-label={`Decrease ${label}`}
          className="flex h-8 w-8 items-center justify-center rounded-sm border-[0.5px] border-border-emphasis bg-transparent text-base text-text-primary transition-colors duration-150 hover:border-accent-muted hover:text-accent-text disabled:cursor-not-allowed disabled:opacity-30 disabled:hover:border-border-emphasis disabled:hover:text-text-primary"
        >
          −
        </button>
        <span className="inline-flex min-w-[2.5rem] items-baseline justify-center whitespace-nowrap font-display text-xl font-semibold tabular-nums text-text-primary">
          <span>{total}</span>
          {addedPoints > 0 && (
            <span className="ml-1 text-sm text-kombats-jade-light">+{addedPoints}</span>
          )}
        </span>
        <button
          type="button"
          onClick={onIncrement}
          disabled={disabled || !canIncrement}
          aria-label={`Increase ${label}`}
          className="flex h-8 w-8 items-center justify-center rounded-sm border-[0.5px] border-border-emphasis bg-transparent text-base text-text-primary transition-colors duration-150 hover:border-accent-muted hover:text-accent-text disabled:cursor-not-allowed disabled:opacity-30 disabled:hover:border-border-emphasis disabled:hover:text-text-primary"
        >
          +
        </button>
      </div>
    </div>
  );
}
