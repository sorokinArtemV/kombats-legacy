import { useState } from 'react';
import { AnimatePresence, motion } from 'motion/react';
import { Sword, Zap, TrendingUp, Heart } from 'lucide-react';
import type { LucideIcon } from 'lucide-react';
import { useAuthStore } from '@/modules/auth/store';
import { KpiTile } from '@/ui/components/KpiTile';
import type { PlayerCardResponse } from '@/types/player';
import { usePlayerStore } from '../store';
import { deriveMaxHp } from '../hp-formula';
import { usePlayerCard } from '../hooks';

// DESIGN_REFERENCE.md §5.18 — soft elliptical black halo behind the name so
// text stays legible over bright scene art. Cannot be expressed as a Tailwind
// utility (elliptical radial-gradient + 22 px blur).
const halo: React.CSSProperties = {
  inset: '-38px -56px',
  background:
    'radial-gradient(ellipse 68% 62% at center, rgba(var(--rgb-black), 0.62) 0%, rgba(var(--rgb-black), 0.38) 48%, rgba(var(--rgb-black), 0) 88%)',
  filter: 'blur(22px)',
};

// DESIGN_REFERENCE.md §5.18 — double black drop-shadow on the display name.
const nameShadow: React.CSSProperties = {
  textShadow: '0 2px 8px rgba(var(--rgb-black), 0.95), 0 0 20px rgba(var(--rgb-black), 0.7)',
};

// Chevron drop-shadow per design — soft 1px/3px black to keep the chevron
// readable over scene art without clobbering the surrounding name halo.
const chevronShadow: React.CSSProperties = {
  filter: 'drop-shadow(0 1px 3px rgba(var(--rgb-black), 0.9))',
};

type AttrTone = 'crimson' | 'gold' | 'jade' | 'silver';

const ATTR_COLOR: Record<AttrTone, string> = {
  crimson: 'var(--color-kombats-crimson)',
  gold: 'var(--color-kombats-gold)',
  jade: 'var(--color-kombats-jade)',
  silver: 'var(--color-kombats-moon-silver)',
};

interface AttrSet {
  strength: number;
  agility: number;
  intuition: number;
  vitality: number;
}

interface FighterNameplateProps {
  /** Override displayed name. Falls back to playerStore.character.name. */
  name?: string;
  /** Override displayed level. Falls back to playerStore.character.level. */
  level?: number | null;
  /** Live HP. Falls back to maxHp (idle lobby state). */
  hp?: number | null;
  /** Max HP. Falls back to deriveMaxHp(playerStore.character.vitality). */
  maxHp?: number | null;
  /**
   * Externally-resolved fighter card (e.g. an opponent's card). When provided
   * the popover reads attributes/record from this card and the own-card query
   * is skipped.
   */
  card?: PlayerCardResponse | null;
  /** Mirror the HP bar so it depletes right-to-left (opponent layout). */
  hpBarMirror?: boolean;
  /** Right-align the name + chevron + popover (opponent layout). */
  alignRight?: boolean;
  /** HP bar / accent tone. 'friendly' = jade (default), 'hostile' = crimson. */
  tone?: 'friendly' | 'hostile';
}

/**
 * Fighter nameplate. By default reads the signed-in player's character +
 * own /players/:id/card (lobby usage). Pass override props (`name`, `level`,
 * `hp`, `maxHp`, `card`) to render a different fighter — used in battle
 * for both the player (overrides hp/maxHp) and the opponent (overrides
 * everything + `hpBarMirror` + `alignRight` + `tone="hostile"`).
 *
 * DESIGN_REFERENCE.md §5.18 (nameplate) + §5.12 (stats popover).
 */
export function FighterNameplate({
  name,
  level,
  hp,
  maxHp,
  card,
  hpBarMirror = false,
  alignRight = false,
  tone = 'friendly',
}: FighterNameplateProps = {}) {
  const character = usePlayerStore((s) => s.character);
  const userIdentityId = useAuthStore((s) => s.userIdentityId);
  const [open, setOpen] = useState(false);

  // Only fetch own card when no external card was supplied. Without `card`
  // the nameplate is rendering the signed-in player and needs the record.
  const externalCardProvided = card !== undefined;
  const ownCardQuery = usePlayerCard(userIdentityId ?? '', !externalCardProvided);
  const resolvedCard: PlayerCardResponse | null = externalCardProvided
    ? (card ?? null)
    : (ownCardQuery.data ?? null);

  const resolvedName = name ?? character?.name ?? 'Unknown';
  const resolvedLevel = level !== undefined ? level : (character?.level ?? null);

  // Attributes for the popover. External card takes precedence; otherwise
  // the signed-in character's stats. If neither is available the popover
  // simply hides the attribute block.
  const resolvedAttrs: AttrSet | null = externalCardProvided
    ? resolvedCard
      ? {
          strength: resolvedCard.strength,
          agility: resolvedCard.agility,
          intuition: resolvedCard.intuition,
          vitality: resolvedCard.vitality,
        }
      : null
    : character
      ? {
          strength: character.strength,
          agility: character.agility,
          intuition: character.intuition,
          vitality: character.vitality,
        }
      : null;

  // HP source is explicit when the caller passes either hp or maxHp (even as
  // null). In that mode — battle — HP must come from the battle store and we
  // never derive from character vitality. Lobby usage passes neither, so we
  // fall back to a full bar derived from the signed-in character's vitality.
  const battleHpProvided = hp !== undefined || maxHp !== undefined;
  const resolvedMaxHp = battleHpProvided
    ? (maxHp ?? 0)
    : character
      ? deriveMaxHp(character.vitality)
      : 0;
  const resolvedHp = battleHpProvided ? (hp ?? 0) : resolvedMaxHp;

  // Render whenever the caller has handed us battle data (hp/maxHp) or an
  // external card (opponent mode); only the pure lobby self-mode short-
  // circuits before the player store has loaded a character. Without this
  // gate the LEFT (player) nameplate would disappear during the brief window
  // between BattleScreen mounting and playerStore.character settling, and
  // the green HP bar would never render.
  if (!externalCardProvided && !battleHpProvided && !character) return null;

  const wins = resolvedCard?.wins ?? 0;
  const losses = resolvedCard?.losses ?? 0;
  const total = wins + losses;
  const winRate = total > 0 ? Math.round((wins / total) * 100) : 0;

  const popoverLevel = resolvedLevel ?? '—';

  return (
    <div
      className={`relative z-20 mb-3 w-[420px] max-w-[calc(100vw-3rem)]${alignRight ? ' text-right' : ''}`}
    >
      <AnimatePresence>
        {open && (
          <motion.div
            key="popover"
            initial={{ opacity: 0, y: 8 }}
            animate={{ opacity: 1, y: 0 }}
            exit={{ opacity: 0, y: 8 }}
            transition={{ duration: 0.2 }}
            className="absolute bottom-full left-0 right-0 mb-3 overflow-hidden rounded-md border-[0.5px] border-border-subtle bg-glass shadow-[var(--shadow-panel)] backdrop-blur-[20px]"
          >
            <div
              className={`flex items-center justify-between border-b border-border-divider px-4 py-2${alignRight ? ' flex-row-reverse' : ''}`}
            >
              <span className="text-[10px] font-medium uppercase tracking-[0.22em] text-text-muted">
                Fighter Profile
              </span>
              <span className="text-[10px] font-medium uppercase tracking-[0.22em] text-kombats-gold">
                Lv {popoverLevel}
              </span>
            </div>

            <div className="grid grid-cols-2 gap-x-6 px-4 py-3 text-left">
              <div>
                <div className="mb-2 text-[9px] uppercase tracking-[0.05em] text-text-muted">
                  Attributes
                </div>
                <div className="flex flex-col gap-1.5">
                  <AttrRow
                    icon={Sword}
                    tone="crimson"
                    label="Strength"
                    value={resolvedAttrs?.strength ?? 0}
                  />
                  <AttrRow
                    icon={Zap}
                    tone="gold"
                    label="Agility"
                    value={resolvedAttrs?.agility ?? 0}
                  />
                  <AttrRow
                    icon={TrendingUp}
                    tone="jade"
                    label="Intuition"
                    value={resolvedAttrs?.intuition ?? 0}
                  />
                  <AttrRow
                    icon={Heart}
                    tone="silver"
                    label="Vitality"
                    value={resolvedAttrs?.vitality ?? 0}
                  />
                </div>
              </div>
              <div>
                <div className="mb-2 text-[9px] uppercase tracking-[0.05em] text-text-muted">
                  Record
                </div>
                <div className="grid grid-cols-2 gap-2">
                  <KpiTile value={wins} label="Wins" tone="jade" variant="nameplate" />
                  <KpiTile value={losses} label="Losses" tone="crimson" variant="nameplate" />
                </div>
                <div className="mt-2 flex flex-col gap-1">
                  <AuxRow
                    label="Winrate"
                    value={total > 0 ? `${winRate}%` : '—'}
                    valueColor="var(--color-kombats-gold)"
                  />
                </div>
              </div>
            </div>
          </motion.div>
        )}
      </AnimatePresence>

      <div aria-hidden className="pointer-events-none absolute" style={halo} />

      <div className="relative">
        <div className={`mb-2 flex${alignRight ? ' justify-end' : ''}`}>
          <h2 className="text-2xl leading-none tracking-wide text-text-primary" style={nameShadow}>
            {resolvedName}
          </h2>
        </div>

        <div className={`flex items-center gap-2${alignRight ? ' flex-row-reverse' : ''}`}>
          <HpBar hp={resolvedHp} maxHp={resolvedMaxHp} tone={tone} mirror={hpBarMirror} />
          <button
            type="button"
            onClick={() => setOpen((v) => !v)}
            aria-expanded={open}
            aria-label={open ? 'Hide fighter stats' : 'Show fighter stats'}
            className="flex h-7 w-7 shrink-0 items-center justify-center text-kombats-moon-silver transition-colors duration-150 hover:text-kombats-gold focus:outline-none focus-visible:text-kombats-gold"
            style={chevronShadow}
          >
            <Chevron open={open} />
          </button>
        </div>
      </div>
    </div>
  );
}

// DESIGN_REFERENCE.md §3.11 — parallelogram HP bar with jade gradient fill.
// `mirror` flips the parallelogram skew + fill direction for the opponent
// nameplate; `tone` swaps the jade fill for crimson.
interface HpBarProps {
  hp: number;
  maxHp: number;
  tone: 'friendly' | 'hostile';
  mirror: boolean;
}

function HpBar({ hp, maxHp, tone, mirror }: HpBarProps) {
  const pct = maxHp > 0 ? Math.max(0, Math.min(100, (hp / maxHp) * 100)) : 0;

  const skewLeft = 'polygon(12px 0, 100% 0, calc(100% - 12px) 100%, 0 100%)';
  const skewRight = 'polygon(0 0, calc(100% - 12px) 0, 100% 100%, 12px 100%)';

  const fillBackground =
    tone === 'hostile'
      ? 'linear-gradient(180deg, var(--palette-hp-crimson-1) 0%, var(--palette-hp-crimson-2) 55%, var(--palette-hp-crimson-3) 100%)'
      : 'linear-gradient(180deg, var(--palette-hp-jade-1) 0%, var(--palette-hp-jade-2) 55%, var(--palette-hp-jade-3) 100%)';

  return (
    <div
      className="relative h-7 flex-1"
      style={{
        clipPath: mirror ? skewRight : skewLeft,
        background: 'rgba(var(--rgb-ink-navy), 0.75)',
        border: '0.5px solid rgba(var(--rgb-white), 0.08)',
      }}
    >
      <div
        aria-hidden
        className="absolute inset-y-0 transition-[width] duration-300 ease-out"
        style={{
          width: `${pct}%`,
          left: mirror ? 'auto' : 0,
          right: mirror ? 0 : 'auto',
          background: fillBackground,
        }}
      >
        <div
          aria-hidden
          className="pointer-events-none absolute inset-y-0 w-px"
          style={{
            left: mirror ? 0 : 'auto',
            right: mirror ? 'auto' : 0,
            background: 'rgba(var(--rgb-white), 0.22)',
          }}
        />
      </div>
      <span
        className="absolute inset-0 flex items-center font-display tabular-nums text-text-primary"
        style={{
          justifyContent: mirror ? 'flex-start' : 'flex-end',
          padding: '0 14px',
          fontStyle: 'italic',
          fontSize: 13,
          letterSpacing: '0.04em',
          fontFeatureSettings: '"tnum"',
          textShadow: 'var(--shadow-text-on-glass-strong)',
        }}
      >
        {hp}
        <span className="mx-[3px] opacity-55">/</span>
        {maxHp}
      </span>
    </div>
  );
}

function AttrRow({
  icon: Icon,
  tone,
  label,
  value,
}: {
  icon: LucideIcon;
  tone: AttrTone;
  label: string;
  value: number;
}) {
  return (
    <div className="flex items-center justify-between">
      <div className="flex items-center gap-2">
        <Icon className="h-3.5 w-3.5" style={{ color: ATTR_COLOR[tone] }} />
        <span className="text-[12px] text-text-secondary">{label}</span>
      </div>
      <span className="text-[12px] font-medium tabular-nums text-text-primary">{value}</span>
    </div>
  );
}

function AuxRow({
  label,
  value,
  valueColor,
}: {
  label: string;
  value: string;
  valueColor: string;
}) {
  return (
    <div className="flex items-center justify-between text-[10px] uppercase tracking-[0.05em]">
      <span className="text-text-muted">{label}</span>
      <span className="font-medium tabular-nums" style={{ color: valueColor }}>
        {value}
      </span>
    </div>
  );
}

function Chevron({ open }: { open: boolean }) {
  return (
    <svg
      width="16"
      height="16"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden
      className="transition-transform duration-200"
      style={{ transform: open ? 'rotate(180deg)' : 'none' }}
    >
      <path d="M18 15l-6-6-6 6" />
    </svg>
  );
}
