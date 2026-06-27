import { clsx } from 'clsx';
import { Heart, Sword } from 'lucide-react';

export type StatIconKind = 'strength' | 'agility' | 'intuition' | 'vitality';

interface StatIconProps {
  kind: StatIconKind;
  size?: number;
  className?: string;
}

// Token-driven stat colors, matching the AttrRow tone palette in
// FighterNameplate (crimson / gold / jade / silver) so stat color reads
// consistently across the lobby + onboarding surfaces.
const STAT_COLOR: Record<StatIconKind, string> = {
  strength: 'var(--color-kombats-crimson-light)',
  agility: 'var(--color-kombats-gold)',
  intuition: 'var(--color-kombats-jade-light)',
  vitality: 'var(--color-kombats-moon-silver)',
};

export function StatIcon({ kind, size = 28, className }: StatIconProps) {
  const color = STAT_COLOR[kind];
  const cls = clsx('shrink-0', className);

  if (kind === 'strength') {
    return <Sword size={size} color={color} strokeWidth={1.6} aria-hidden className={cls} />;
  }
  if (kind === 'vitality') {
    return <Heart size={size} color={color} strokeWidth={1.6} aria-hidden className={cls} />;
  }

  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke={color}
      strokeWidth={1.6}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden
      className={cls}
    >
      {kind === 'agility' && <AgilityGlyph />}
      {kind === 'intuition' && <IntuitionGlyph />}
    </svg>
  );
}

// Lightning bolt glyph.
function AgilityGlyph() {
  return <path d="M13 2L4 14h7l-1 8 9-12h-7l1-8z" />;
}

// Ripple / wave glyph — three offset crests for "intuition" / sense.
function IntuitionGlyph() {
  return (
    <>
      <path d="M3 9c1.5-1.5 3-1.5 4.5 0S10.5 10.5 12 9s3-1.5 4.5 0 3 1.5 4.5 0" />
      <path d="M3 14c1.5-1.5 3-1.5 4.5 0s3 1.5 4.5 0 3-1.5 4.5 0 3 1.5 4.5 0" />
      <path d="M3 19c1.5-1.5 3-1.5 4.5 0s3 1.5 4.5 0 3-1.5 4.5 0 3 1.5 4.5 0" />
    </>
  );
}
