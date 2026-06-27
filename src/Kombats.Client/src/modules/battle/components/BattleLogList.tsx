import { useMemo, useRef } from 'react';
import type { ComponentType, SVGProps } from 'react';
import { clsx } from 'clsx';
import { useScrollToBottom } from '@/ui/hooks/useScrollToBottom';
import {
  Flag,
  LogOut,
  MessageCircle,
  Minus,
  Scale,
  Shield,
  Skull,
  Sword,
  Trophy,
  Wind,
  Zap,
} from 'lucide-react';
import { END_OF_BATTLE_TURN_INDEX } from '../feed-constants';
import type { BattleFeedEntry, FeedEntryKind, FeedEntrySeverity } from '@/types/battle';

type LucideIcon = ComponentType<SVGProps<SVGSVGElement>>;

interface KindMeta {
  Icon: LucideIcon;
  // CSS color value (hex or var()) used for the entry text. The icon mirrors
  // this except for commentary, which intentionally uses a quieter icon tint.
  color: string;
  textClass: string;
  isCommentary?: boolean;
}

function kindMeta(kind: FeedEntryKind): KindMeta {
  switch (kind) {
    case 'AttackHit':
      return { Icon: Sword, color: '#D4A86A', textClass: '' };
    case 'AttackCrit':
      return { Icon: Zap, color: '#C97B7B', textClass: 'font-bold' };
    case 'AttackDodge':
      return { Icon: Wind, color: '#7EB8D4', textClass: '' };
    case 'AttackBlock':
      return { Icon: Shield, color: '#7EC48A', textClass: '' };
    case 'AttackNoAction':
      return { Icon: Minus, color: 'var(--color-text-muted)', textClass: '' };
    case 'BattleStart':
      return { Icon: Flag, color: '#A98FD4', textClass: 'italic' };
    case 'BattleEndVictory':
      return {
        Icon: Trophy,
        color: 'var(--color-accent-primary)',
        textClass: 'font-bold',
      };
    case 'BattleEndDraw':
      return {
        Icon: Scale,
        color: 'var(--color-text-secondary)',
        textClass: '',
      };
    case 'BattleEndForfeit':
      return { Icon: LogOut, color: '#C97B7B', textClass: '' };
    case 'DefeatKnockout':
      return { Icon: Skull, color: '#C97B7B', textClass: 'font-bold' };
    case 'CommentaryFirstBlood':
    case 'CommentaryMutualMiss':
    case 'CommentaryStalemate':
    case 'CommentaryNearDeath':
    case 'CommentaryBigHit':
    case 'CommentaryKnockout':
    case 'CommentaryDraw':
      return {
        Icon: MessageCircle,
        // Text color per spec is text-text-secondary; the icon is rendered
        // with text-text-muted (handled in BattleLogRow) so commentary reads
        // as flavor, not a combat action.
        color: 'var(--color-text-secondary)',
        textClass: 'italic text-[12px]',
        isCommentary: true,
      };
  }
}

function severityBorderColor(severity: FeedEntrySeverity): string {
  switch (severity) {
    case 'Critical':
      return 'var(--color-kombats-crimson)';
    case 'Important':
      return 'var(--color-accent-primary)';
    case 'Normal':
    default:
      return 'transparent';
  }
}

function roundLabel(turnIndex: number): string {
  if (turnIndex === 0) return 'BATTLE START';
  if (turnIndex === END_OF_BATTLE_TURN_INDEX) return 'BATTLE END';
  return `ROUND ${turnIndex}`;
}

interface RoundGroup {
  turnIndex: number;
  entries: BattleFeedEntry[];
}

function groupByTurn(entries: readonly BattleFeedEntry[]): RoundGroup[] {
  const groups: RoundGroup[] = [];
  for (const entry of entries) {
    const last = groups[groups.length - 1];
    if (last && last.turnIndex === entry.turnIndex) {
      last.entries.push(entry);
    } else {
      groups.push({ turnIndex: entry.turnIndex, entries: [entry] });
    }
  }
  return groups;
}

interface BattleLogListProps {
  entries: readonly BattleFeedEntry[];
}

/**
 * Inline battle-log feed used inside the BottomDock's chat-tab content area.
 * The owner (BottomDock) chooses the source — the live store while a battle
 * is active, the archived `lastBattleLog.entries` once the user has returned
 * to /lobby — so this component stays a pure renderer.
 *
 * Renders each turn as a labeled group with a per-entry icon + text row. No
 * surrounding panel chrome — sits flush in the same surface as GENERAL chat
 * messages.
 */
export function BattleLogList({ entries }: BattleLogListProps) {
  const messagesEndRef = useRef<HTMLDivElement | null>(null);

  // Stable signal for the auto-scroll effect: avoid scrolling on unrelated
  // store mutations by keying off the tail entry's identity.
  const tail = entries[entries.length - 1];
  const tailSignal = tail ? `${tail.key}:${tail.sequence}` : '';

  useScrollToBottom(messagesEndRef, tailSignal);

  const groups = useMemo(() => groupByTurn(entries), [entries]);

  if (entries.length === 0) {
    return (
      <p className="py-6 text-center text-[11px] uppercase tracking-[0.18em] text-text-muted">
        No events yet
      </p>
    );
  }

  return (
    <div className="flex flex-col">
      {groups.map((group) => (
        <div key={group.turnIndex} className="flex flex-col">
          <RoundSeparator turnIndex={group.turnIndex} />
          {group.entries.map((entry) => (
            <BattleLogRow key={entry.key} entry={entry} />
          ))}
        </div>
      ))}
      <div ref={messagesEndRef} />
    </div>
  );
}

function RoundSeparator({ turnIndex }: { turnIndex: number }) {
  return (
    <div className="flex items-center gap-2 py-2">
      <div className="h-px flex-1 bg-border-subtle" />
      <span className="text-[10px] font-semibold uppercase tracking-[0.18em] text-text-muted">
        {roundLabel(turnIndex)}
      </span>
      <div className="h-px flex-1 bg-border-subtle" />
    </div>
  );
}

function BattleLogRow({ entry }: { entry: BattleFeedEntry }) {
  const { Icon, color, textClass, isCommentary } = kindMeta(entry.kind);
  const iconColor = isCommentary ? 'var(--color-text-muted)' : color;

  return (
    <div
      className="flex items-start gap-2 px-2 py-0.5 text-[13px] leading-relaxed"
      style={{ borderLeft: `2px solid ${severityBorderColor(entry.severity)}` }}
    >
      <span
        className="mt-0.5 flex h-4 w-4 shrink-0 items-center justify-center"
        style={{ color: iconColor }}
      >
        <Icon className="h-3.5 w-3.5" aria-hidden />
      </span>
      <span className={clsx('min-w-0', textClass)} style={{ color }}>
        {entry.text}
      </span>
    </div>
  );
}
