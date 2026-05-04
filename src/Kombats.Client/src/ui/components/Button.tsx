import { type ButtonHTMLAttributes } from 'react';
import { clsx } from 'clsx';
import { Spinner } from './Spinner';

type ButtonVariant = 'primary' | 'secondary' | 'ghost' | 'danger';
type ButtonSize = 'sm' | 'md' | 'lg';

interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant;
  size?: ButtonSize;
  loading?: boolean;
}

const sizeClasses: Record<ButtonSize, string> = {
  sm: 'px-[14px] py-[6px] text-[11px]',
  md: 'px-6 py-2.5 text-[13px]',
  lg: 'px-10 py-4 text-[15px]',
};

const variantClasses: Record<ButtonVariant, string> = {
  primary:
    'bg-accent-primary text-text-on-accent border border-transparent hover:bg-kombats-gold-light disabled:hover:bg-accent-primary',
  secondary:
    'bg-transparent text-text-primary border-[0.5px] border-border-emphasis hover:border-accent-muted hover:text-accent-text disabled:hover:border-border-emphasis disabled:hover:text-text-primary',
  ghost: 'bg-transparent text-text-secondary border border-transparent hover:text-accent-text',
  danger:
    'bg-kombats-crimson text-text-on-danger border border-transparent hover:bg-kombats-crimson-light disabled:hover:bg-kombats-crimson',
};

export function Button({
  variant = 'primary',
  size = 'md',
  loading = false,
  disabled,
  children,
  className,
  ...props
}: ButtonProps) {
  return (
    <button
      disabled={disabled || loading}
      className={clsx(
        'inline-flex items-center justify-center gap-2 whitespace-nowrap rounded-md font-medium uppercase tracking-[0.18em] transition-colors duration-150 disabled:cursor-not-allowed disabled:opacity-50',
        sizeClasses[size],
        variantClasses[variant],
        className,
      )}
      {...props}
    >
      {loading && <Spinner size="sm" />}
      {children}
    </button>
  );
}
