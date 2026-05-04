import { useEffect, useMemo, useRef } from 'react';
import { clsx } from 'clsx';
import { Sword } from 'lucide-react';
import { getNickColor } from '@/modules/chat/nick-color';
import { ALL_ZONES } from '../zones';
import type { BattleZone, TurnResolutionLogRealtime } from '@/types/battle';

interface RoundMapProps {
  entries: readonly TurnResolutionLogRealtime[];
  playerAId: string | null;
  playerBId: string | null;
  playerAName: string | null;
  playerBName: string | null;
  maxRounds?: number;
}

interface ZoneCellState {
  attacked: boolean;
  blocked: boolean;
}

interface RoundRow {
  turnIndex: number;
  playerA: ZoneCellState[];
  playerB: ZoneCellState[];
}

function isBattleZone(value: string | null): value is BattleZone {
  return (
    value === 'Head' ||
    value === 'Chest' ||
    value === 'Belly' ||
    value === 'Waist' ||
    value === 'Legs'
  );
}

function emptyCells(): ZoneCellState[] {
  return ALL_ZONES.map(() => ({ attacked: false, blocked: false }));
}

function deriveRow(log: TurnResolutionLogRealtime): RoundRow {
  const playerA = emptyCells();
  const playerB = emptyCells();

  // log.atoB: A's attack vs B's block.
  // -> Mark A's attacked zone (A's cell at log.atoB.attackZone)
  // -> Mark B's blocked zones (B's cells at log.atoB.defenderBlock*)
  const aAttack = log.atoB.attackZone;
  if (isBattleZone(aAttack)) {
    playerA[ALL_ZONES.indexOf(aAttack)].attacked = true;
  }
  const bBlockPrimary = log.atoB.defenderBlockPrimary;
  if (isBattleZone(bBlockPrimary)) {
    playerB[ALL_ZONES.indexOf(bBlockPrimary)].blocked = true;
  }
  const bBlockSecondary = log.atoB.defenderBlockSecondary;
  if (isBattleZone(bBlockSecondary)) {
    playerB[ALL_ZONES.indexOf(bBlockSecondary)].blocked = true;
  }

  // log.btoA: B's attack vs A's block — symmetric.
  const bAttack = log.btoA.attackZone;
  if (isBattleZone(bAttack)) {
    playerB[ALL_ZONES.indexOf(bAttack)].attacked = true;
  }
  const aBlockPrimary = log.btoA.defenderBlockPrimary;
  if (isBattleZone(aBlockPrimary)) {
    playerA[ALL_ZONES.indexOf(aBlockPrimary)].blocked = true;
  }
  const aBlockSecondary = log.btoA.defenderBlockSecondary;
  if (isBattleZone(aBlockSecondary)) {
    playerA[ALL_ZONES.indexOf(aBlockSecondary)].blocked = true;
  }

  return { turnIndex: log.turnIndex, playerA, playerB };
}

/**
 * Per-round visual grid of attacks and blocks. Rendered in the right column
 * of the BottomDock when the BATTLE LOG tab is active.
 *
 * Each round is one row: a tiny round number, then two 5-cell columns (one
 * per player, top-to-bottom = head → legs). A green tint on a cell means the
 * cell's player blocked that zone; a red sword icon means the OPPONENT
 * attacked that zone. The two compose: a sword on a green cell is "attack
 * landed in a block".
 *
 * Pure renderer — owner picks the source (live `turnHistory` while a battle
 * is active, archived `lastTurnHistory` after the user returns to /lobby).
 */
export function RoundMap({
  entries,
  playerAId,
  playerBId,
  playerAName,
  playerBName,
  maxRounds = 8,
}: RoundMapProps) {
  const visibleRows = useMemo(() => {
    const rows = entries.map(deriveRow);
    return rows.length > maxRounds ? rows.slice(rows.length - maxRounds) : rows;
  }, [entries, maxRounds]);
  const lastIndex = visibleRows.length - 1;

  const tailRef = useRef<HTMLDivElement | null>(null);
  const tailSignal = visibleRows[lastIndex]?.turnIndex ?? -1;
  useEffect(() => {
    if (tailSignal < 0) return;
    tailRef.current?.scrollIntoView({ behavior: 'smooth', block: 'end' });
  }, [tailSignal]);

  const aColor = playerAId ? getNickColor(playerAId) : 'var(--color-text-primary)';
  const bColor = playerBId ? getNickColor(playerBId) : 'var(--color-text-primary)';

  return (
    <div className="flex min-h-0 flex-1 flex-col px-2 pb-2 pt-2">
      <header className="flex items-center justify-between gap-2 px-1 pb-1.5">
        <span className="text-[11px] font-semibold uppercase tracking-[0.18em] text-accent-text">
          Round Map
        </span>
        <span className="text-[10px] uppercase tracking-[0.12em] text-text-muted">↑ head</span>
      </header>

      {visibleRows.length === 0 ? (
        <p className="px-1 py-4 text-center text-[11px] uppercase tracking-[0.18em] text-text-muted">
          No rounds yet
        </p>
      ) : (
        <>
          <PlayerHeaderRow
            playerAName={playerAName ?? 'Player A'}
            playerBName={playerBName ?? 'Player B'}
            aColor={aColor}
            bColor={bColor}
          />
          <div className="kombats-scroll flex min-h-0 flex-1 flex-col gap-1 overflow-y-auto pr-1">
            {visibleRows.map((row, idx) => (
              <RoundRowView key={row.turnIndex} row={row} isCurrent={idx === lastIndex} />
            ))}
            <div ref={tailRef} />
          </div>
        </>
      )}
    </div>
  );
}

function PlayerHeaderRow({
  playerAName,
  playerBName,
  aColor,
  bColor,
}: {
  playerAName: string;
  playerBName: string;
  aColor: string;
  bColor: string;
}) {
  return (
    <div className="grid grid-cols-[24px_1fr_1fr] items-center gap-x-2 px-1 pb-1">
      <span aria-hidden />
      <span
        className="truncate text-center text-[10px] font-medium tracking-[0.06em]"
        style={{ color: aColor }}
        title={playerAName}
      >
        {playerAName}
      </span>
      <span
        className="truncate text-center text-[10px] font-medium tracking-[0.06em]"
        style={{ color: bColor }}
        title={playerBName}
      >
        {playerBName}
      </span>
    </div>
  );
}

function RoundRowView({ row, isCurrent }: { row: RoundRow; isCurrent: boolean }) {
  return (
    <div
      className={clsx(
        'grid grid-cols-[24px_1fr_1fr] items-center gap-x-2 rounded-sm px-1 py-0.5 transition-colors',
        isCurrent && 'bg-[rgba(var(--rgb-gold-accent),0.10)]',
      )}
    >
      <span
        className={clsx(
          'text-right text-[10px] tabular-nums',
          isCurrent ? 'text-accent-text' : 'text-text-muted',
        )}
      >
        {row.turnIndex}
      </span>
      <ZonesGrid cells={row.playerA} />
      <ZonesGrid cells={row.playerB} />
    </div>
  );
}

function ZonesGrid({ cells }: { cells: ZoneCellState[] }) {
  return (
    <div
      className="grid grid-cols-5 gap-px rounded-[4px] p-0.5"
      style={{
        background: 'rgba(var(--rgb-ink-navy), 0.7)',
        border: '0.5px solid rgba(255, 255, 255, 0.06)',
      }}
    >
      {cells.map((cell, idx) => (
        <ZoneCell key={idx} cell={cell} />
      ))}
    </div>
  );
}

function ZoneCell({ cell }: { cell: ZoneCellState }) {
  return (
    <div
      className="flex aspect-square items-center justify-center rounded-[1px]"
      style={{
        background: cell.blocked ? 'rgba(var(--rgb-jade), 0.38)' : 'rgba(255, 255, 255, 0.06)',
      }}
    >
      {cell.attacked && (
        <Sword
          aria-hidden
          className="h-[70%] w-[70%]"
          style={{ color: 'var(--color-kombats-crimson)' }}
        />
      )}
    </div>
  );
}
