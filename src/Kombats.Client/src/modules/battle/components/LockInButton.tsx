interface LockInButtonProps {
  onClick: () => void;
  disabled: boolean;
}

export function LockInButton({ onClick, disabled }: LockInButtonProps) {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      aria-label="Lock in attack and block"
      className="inline-flex items-center justify-center rounded-md border border-accent-primary bg-transparent px-4 py-1.5 text-[11px] font-semibold uppercase tracking-[0.18em] text-accent-primary transition-colors duration-150 enabled:hover:bg-accent-primary enabled:hover:text-text-on-accent enabled:focus-visible:bg-accent-primary enabled:focus-visible:text-text-on-accent disabled:cursor-not-allowed disabled:opacity-50"
    >
      Lock In
    </button>
  );
}
