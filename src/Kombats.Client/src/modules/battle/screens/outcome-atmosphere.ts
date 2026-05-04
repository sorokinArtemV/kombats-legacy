import type { BattleEndOutcome } from '../battle-end-outcome';

// Tapered wing lines flanking the title (DESIGN_REFERENCE.md §3.9).
const WING_LEFT_VICTORY: React.CSSProperties = {
  background: 'linear-gradient(to right, transparent, rgba(var(--rgb-gold-victory), 0.5))',
};
const WING_RIGHT_VICTORY: React.CSSProperties = {
  background: 'linear-gradient(to left, transparent, rgba(var(--rgb-gold-victory), 0.5))',
};
const WING_LEFT_DEFEAT: React.CSSProperties = {
  background: 'linear-gradient(to right, transparent, rgba(var(--rgb-crimson), 0.55))',
};
const WING_RIGHT_DEFEAT: React.CSSProperties = {
  background: 'linear-gradient(to left, transparent, rgba(var(--rgb-crimson), 0.55))',
};
const WING_LEFT_NEUTRAL: React.CSSProperties = {
  background: 'linear-gradient(to right, transparent, rgba(var(--rgb-moon-silver), 0.45))',
};
const WING_RIGHT_NEUTRAL: React.CSSProperties = {
  background: 'linear-gradient(to left, transparent, rgba(var(--rgb-moon-silver), 0.45))',
};

// Top-edge accent line on the glass panel — outcome-tinted (DESIGN_REFERENCE.md §5.19).
const ACCENT_LINE_VICTORY: React.CSSProperties = {
  background:
    'linear-gradient(to right, transparent, rgba(var(--rgb-gold-victory), 0.75), transparent)',
};
const ACCENT_LINE_DEFEAT: React.CSSProperties = {
  background: 'linear-gradient(to right, transparent, rgba(var(--rgb-crimson), 0.75), transparent)',
};
const ACCENT_LINE_NEUTRAL: React.CSSProperties = {
  background:
    'linear-gradient(to right, transparent, rgba(var(--rgb-gold-accent), 0.5), transparent)',
};

// Deep-dim color reserved for the loser's name on victory — recedes
// further than text-muted so the focal point sits cleanly on the WINNER
// label without competing with the opponent. No --color-text-disabled
// token exists in tokens.css; using the rgb token directly keeps this
// reskin-safe.
const DEEP_DIM_NAME_COLOR = 'rgba(var(--rgb-text-primary), 0.35)';

export interface AtmosphereTokens {
  titleClass: string;
  titleShadow: string;
  wingLeftStyle: React.CSSProperties;
  wingRightStyle: React.CSSProperties;
  accentLineStyle: React.CSSProperties;
  myRoleLabel: string;
  oppRoleLabel: string;
  myStatusLabel: string;
  myStatusClass: string;
  oppStatusLabel: string;
  oppStatusClass: string;
  myNameStyle: React.CSSProperties | undefined;
  oppNameStyle: React.CSSProperties | undefined;
  primaryCtaLabel: string;
}

export const ATMOSPHERE: Record<BattleEndOutcome, AtmosphereTokens> = {
  victory: {
    titleClass: 'text-victory-gold',
    titleShadow: 'var(--shadow-title-victory)',
    wingLeftStyle: WING_LEFT_VICTORY,
    wingRightStyle: WING_RIGHT_VICTORY,
    accentLineStyle: ACCENT_LINE_VICTORY,
    myRoleLabel: 'You',
    oppRoleLabel: 'Opponent',
    myStatusLabel: 'Winner',
    myStatusClass: 'text-victory-gold',
    oppStatusLabel: 'Defeated',
    oppStatusClass: 'text-text-muted',
    myNameStyle: undefined,
    // Loser name recedes further than text-muted on victory so the focal
    // point sits cleanly with the WINNER label (matches design_V2's
    // VICTORY_LOSER_NAME_STYLE color stop).
    oppNameStyle: { color: DEEP_DIM_NAME_COLOR },
    primaryCtaLabel: 'Battle Again',
  },
  defeat: {
    titleClass: 'text-kombats-crimson',
    titleShadow: 'var(--shadow-title-defeat)',
    wingLeftStyle: WING_LEFT_DEFEAT,
    wingRightStyle: WING_RIGHT_DEFEAT,
    accentLineStyle: ACCENT_LINE_DEFEAT,
    myRoleLabel: 'You',
    oppRoleLabel: 'Opponent',
    myStatusLabel: 'Defeated',
    myStatusClass: 'text-kombats-crimson',
    oppStatusLabel: 'Victor',
    oppStatusClass: 'text-victory-gold',
    myNameStyle: undefined,
    oppNameStyle: undefined,
    primaryCtaLabel: 'Try Again',
  },
  systemError: {
    titleClass: 'text-kombats-gold',
    titleShadow: 'var(--shadow-title-neutral)',
    wingLeftStyle: WING_LEFT_NEUTRAL,
    wingRightStyle: WING_RIGHT_NEUTRAL,
    accentLineStyle: ACCENT_LINE_NEUTRAL,
    myRoleLabel: 'You',
    oppRoleLabel: 'Opponent',
    myStatusLabel: '',
    myStatusClass: 'text-text-muted',
    oppStatusLabel: '',
    oppStatusClass: 'text-text-muted',
    myNameStyle: undefined,
    oppNameStyle: undefined,
    primaryCtaLabel: '',
  },
};
