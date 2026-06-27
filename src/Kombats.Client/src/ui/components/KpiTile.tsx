import type { CSSProperties } from 'react';

type KpiTone = 'jade' | 'crimson';
type KpiVariant = 'card' | 'nameplate';

interface KpiTileProps {
  label: string;
  value: number;
  tone: KpiTone;
  variant?: KpiVariant;
}

const TONE_VAR: Record<KpiTone, string> = {
  jade: 'var(--color-kombats-jade)',
  crimson: 'var(--color-kombats-crimson)',
};

export function KpiTile({ label, value, tone, variant = 'card' }: KpiTileProps) {
  const toneVar = TONE_VAR[tone];

  if (variant === 'nameplate') {
    const containerStyle: CSSProperties = {
      background: `color-mix(in srgb, ${toneVar} 10%, transparent)`,
      border: `1px solid color-mix(in srgb, ${toneVar} 30%, transparent)`,
    };
    return (
      <div className="rounded-sm py-1.5 text-center" style={containerStyle}>
        <div className="leading-none tabular-nums" style={{ fontSize: 18, color: toneVar }}>
          {value}
        </div>
        <div className="mt-1 text-[9px] uppercase tracking-[0.05em] text-text-muted">{label}</div>
      </div>
    );
  }

  const containerStyle: CSSProperties = {
    background: `color-mix(in srgb, ${toneVar} 10%, transparent)`,
    borderColor: `color-mix(in srgb, ${toneVar} 35%, transparent)`,
  };
  return (
    <div className="flex flex-col gap-1 rounded-sm border-[0.5px] px-3 py-2" style={containerStyle}>
      <span className="text-[10px] uppercase tracking-[0.18em] text-text-muted">{label}</span>
      <span className="text-lg font-semibold tabular-nums" style={{ color: toneVar }}>
        {value}
      </span>
    </div>
  );
}
