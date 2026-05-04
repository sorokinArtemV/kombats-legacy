import { clsx } from 'clsx';

interface BannerProps {
  tone: 'error' | 'warning';
  children: React.ReactNode;
  id?: string;
}

export function Banner({ tone, children, id }: BannerProps) {
  const cls =
    tone === 'error'
      ? 'border-danger bg-danger/10 text-attack-text'
      : 'border-victory-gold bg-victory-gold/10 text-victory-gold';
  // Errors are surfaced assertively (role=alert + aria-live=assertive) so a
  // screen reader interrupts the user mid-battle. Warnings keep the polite
  // status semantics — they're informational and should not preempt the user.
  const isError = tone === 'error';
  return (
    <div
      id={id}
      className={clsx('rounded-sm border px-3 py-2 text-[11px] uppercase tracking-[0.18em]', cls)}
      role={isError ? 'alert' : 'status'}
      aria-live={isError ? 'assertive' : 'polite'}
    >
      {children}
    </div>
  );
}
