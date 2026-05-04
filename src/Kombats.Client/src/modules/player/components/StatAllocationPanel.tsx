import { usePlayerStore } from '../store';
import { useAllocateStats, STAT_KEYS, STAT_LABELS } from '../useAllocateStats';
import { Button } from '@/ui/components/Button';
import { StatPointAllocator } from '@/ui/components/StatPointAllocator';

/**
 * Post-level-up stat allocation from the lobby. Renders nothing when the
 * character has no unspent points. Shares the `useAllocateStats` hook
 * with the onboarding initial-allocation screen; diverges only in the
 * panel chrome (glass panel, header + Reset button) and the
 * `pendingLevelUpLevel` reset on successful drain to zero.
 */
export function StatAllocationPanel() {
  const character = usePlayerStore((s) => s.character);
  const setPendingLevelUpLevel = usePlayerStore((s) => s.setPendingLevelUpLevel);

  const alloc = useAllocateStats({
    onSuccess: (response) => {
      if (response.unspentPoints === 0) {
        setPendingLevelUpLevel(null);
      }
    },
  });

  if (!character || alloc.unspentPoints <= 0) return null;

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    alloc.submit();
  }

  return (
    <div className="rounded-md border-[0.5px] border-border-subtle bg-glass p-6 shadow-[var(--shadow-panel)] backdrop-blur-[20px]">
      <form onSubmit={handleSubmit} className="flex flex-col gap-5">
        <div className="flex items-center justify-between">
          <h3 className="font-display text-[14px] font-semibold uppercase tracking-[0.24em] text-accent-text">
            Allocate Stat Points
          </h3>
          <span
            className="rounded-sm border-[0.5px] px-2 py-0.5 text-[10px] font-medium uppercase tracking-[0.18em] tabular-nums"
            style={{
              borderColor: 'color-mix(in srgb, var(--color-kombats-gold) 40%, transparent)',
              background: 'color-mix(in srgb, var(--color-kombats-gold) 12%, transparent)',
              color: 'var(--color-accent-text)',
            }}
          >
            {alloc.unspentPoints} available
          </span>
        </div>

        {alloc.revisionConflict && (
          <div
            className="rounded-sm border-[0.5px] border-kombats-crimson/40 bg-kombats-crimson/10 px-3 py-2 text-[11px] uppercase tracking-[0.18em] text-kombats-crimson-light"
            role="alert"
          >
            Character was updated elsewhere. Your draft was discarded — please re-allocate.
          </div>
        )}

        <div className="h-px bg-border-divider" aria-hidden />

        <div className="flex flex-col gap-2">
          {STAT_KEYS.map((stat) => (
            <StatPointAllocator
              key={stat}
              label={STAT_LABELS[stat]}
              baseValue={character[stat]}
              addedPoints={alloc.added[stat]}
              onIncrement={() => alloc.increment(stat)}
              onDecrement={() => alloc.decrement(stat)}
              canIncrement={alloc.canIncrement}
              canDecrement={alloc.canDecrementStat(stat)}
              disabled={alloc.isPending}
            />
          ))}
        </div>

        <div className="flex items-center justify-between rounded-sm border-[0.5px] border-border-subtle bg-glass-subtle px-4 py-2">
          <span className="text-[11px] font-medium uppercase tracking-[0.18em] text-text-muted">
            Points remaining
          </span>
          <span className="text-sm font-medium tabular-nums text-kombats-gold">
            {alloc.remaining}
          </span>
        </div>

        {alloc.errorMessage && (
          <p className="text-center text-[11px] uppercase tracking-[0.18em] text-kombats-crimson-light">
            {alloc.errorMessage}
          </p>
        )}

        <div className="flex items-center justify-end gap-2">
          <Button
            type="button"
            variant="secondary"
            onClick={alloc.reset}
            disabled={alloc.isPending || alloc.totalAdded === 0}
          >
            Reset
          </Button>
          <Button type="submit" loading={alloc.isPending} disabled={alloc.totalAdded === 0}>
            Confirm
          </Button>
        </div>
      </form>
    </div>
  );
}
