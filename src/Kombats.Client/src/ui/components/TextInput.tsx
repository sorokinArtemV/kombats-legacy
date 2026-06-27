import { type InputHTMLAttributes, useId, useState } from 'react';
import { clsx } from 'clsx';

interface TextInputProps extends Omit<InputHTMLAttributes<HTMLInputElement>, 'id'> {
  label?: string;
  error?: string;
  charCount?: { current: number; max: number };
}

export function TextInput({
  label,
  error,
  charCount,
  className,
  onFocus,
  onBlur,
  ...props
}: TextInputProps) {
  const id = useId();
  const [focused, setFocused] = useState(false);

  const hasError = Boolean(error);
  const overLimit = charCount ? charCount.current > charCount.max : false;

  return (
    <div className="flex flex-col">
      {label && (
        <label
          htmlFor={id}
          className="mb-2 text-[11px] font-medium uppercase tracking-[0.18em] text-text-muted"
        >
          {label}
        </label>
      )}
      <input
        id={id}
        onFocus={(e) => {
          setFocused(true);
          onFocus?.(e);
        }}
        onBlur={(e) => {
          setFocused(false);
          onBlur?.(e);
        }}
        className={clsx(
          'w-full rounded-sm border bg-black/30 px-4 py-2 text-sm text-text-primary outline-none transition-colors duration-150 placeholder:text-text-muted',
          hasError
            ? 'border-kombats-crimson'
            : focused
              ? 'border-accent-muted'
              : 'border-border-subtle',
          className,
        )}
        {...props}
      />
      {(error || charCount) && (
        <div className="mt-2 flex items-center justify-between text-[12px] font-medium uppercase tracking-[0.18em]">
          <span className={hasError ? 'text-kombats-crimson-light' : 'text-text-muted'}>
            {error ?? ''}
          </span>
          {charCount && (
            <span
              className={clsx(
                'tabular-nums',
                overLimit ? 'text-kombats-crimson-light' : 'text-text-muted',
              )}
            >
              {charCount.current}/{charCount.max}
            </span>
          )}
        </div>
      )}
    </div>
  );
}
