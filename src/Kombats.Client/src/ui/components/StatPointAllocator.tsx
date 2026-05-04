interface StatPointAllocatorProps {
  label: string;
  baseValue: number;
  addedPoints: number;
  onIncrement: () => void;
  onDecrement: () => void;
  canIncrement: boolean;
  canDecrement: boolean;
  disabled?: boolean;
}

export function StatPointAllocator({
  label,
  baseValue,
  addedPoints,
  onIncrement,
  onDecrement,
  canIncrement,
  canDecrement,
  disabled = false,
}: StatPointAllocatorProps) {
  const total = baseValue + addedPoints;

  return (
    <div className="flex items-center gap-4 rounded-sm border-[0.5px] border-border-subtle bg-glass-subtle px-4 py-2">
      <span className="min-w-0 flex-1 truncate text-[11px] font-medium uppercase tracking-[0.18em] text-text-secondary">
        {label}
      </span>
      <div className="flex flex-shrink-0 items-center gap-2">
        <button
          type="button"
          onClick={onDecrement}
          disabled={disabled || !canDecrement}
          aria-label={`Decrease ${label}`}
          className="flex h-7 w-7 flex-shrink-0 items-center justify-center rounded-sm border-[0.5px] border-border-emphasis bg-transparent text-base text-text-primary transition-colors duration-150 hover:border-accent-muted hover:text-accent-text disabled:cursor-not-allowed disabled:opacity-30 disabled:hover:border-border-emphasis disabled:hover:text-text-primary"
        >
          −
        </button>
        <span className="inline-flex min-w-[4rem] items-baseline justify-center whitespace-nowrap text-sm tabular-nums text-text-primary">
          <span>{total}</span>
          {addedPoints > 0 && <span className="ml-1 text-kombats-jade-light">+{addedPoints}</span>}
        </span>
        <button
          type="button"
          onClick={onIncrement}
          disabled={disabled || !canIncrement}
          aria-label={`Increase ${label}`}
          className="flex h-7 w-7 flex-shrink-0 items-center justify-center rounded-sm border-[0.5px] border-border-emphasis bg-transparent text-base text-text-primary transition-colors duration-150 hover:border-accent-muted hover:text-accent-text disabled:cursor-not-allowed disabled:opacity-30 disabled:hover:border-border-emphasis disabled:hover:text-text-primary"
        >
          +
        </button>
      </div>
    </div>
  );
}
