import { type ReactNode } from 'react';

interface OnboardingCardProps {
  eyebrow: string;
  title: string;
  subtitle?: string;
  children: ReactNode;
}

// Display title gold bloom — DESIGN_REFERENCE.md §5.11.
const titleBloomStyle = {
  textShadow: 'var(--shadow-title-soft)',
};

export function OnboardingCard({ eyebrow, title, subtitle, children }: OnboardingCardProps) {
  return (
    <div className="flex w-full flex-col gap-6">
      <header className="flex flex-col items-center gap-3 text-center">
        <span className="text-[10px] font-medium uppercase tracking-[0.32em] text-text-muted">
          {eyebrow}
        </span>
        <h2
          className="font-display text-[22px] font-semibold uppercase leading-none tracking-[0.28em] text-kombats-gold"
          style={titleBloomStyle}
        >
          {title}
        </h2>
        {subtitle && (
          <p className="text-[12px] uppercase tracking-[0.18em] text-text-muted">{subtitle}</p>
        )}
      </header>

      <div aria-hidden className="border-t border-border-divider" />

      {children}
    </div>
  );
}
