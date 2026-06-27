import { clsx } from 'clsx';
import { useBattleStore } from '../store';
import { useAuthStore } from '@/modules/auth/store';
import type { AttackOutcomeRealtime, AttackResolutionRealtime } from '@/types/battle';

const outcomeLabel: Record<AttackOutcomeRealtime, string> = {
  NoAction: 'No action',
  Dodged: 'Dodged',
  Blocked: 'Blocked',
  Hit: 'Hit',
  CriticalHit: 'Critical hit',
  CriticalBypassBlock: 'Critical (bypass)',
  CriticalHybridBlocked: 'Critical (hybrid)',
};

// DESIGN_REFERENCE.md §3.20 — chip color derived from outcome tone.
function outcomeChipColor(outcome: AttackOutcomeRealtime): string {
  switch (outcome) {
    case 'Hit':
    case 'CriticalHit':
    case 'CriticalBypassBlock':
    case 'CriticalHybridBlocked':
      return 'var(--color-kombats-crimson)';
    case 'Blocked':
    case 'Dodged':
      return 'var(--color-kombats-jade)';
    case 'NoAction':
    default:
      return 'var(--color-kombats-moon-silver)';
  }
}

export function TurnResultPanel() {
  const lastResolution = useBattleStore((s) => s.lastResolution);
  const myId = useAuthStore((s) => s.userIdentityId);
  const playerAId = useBattleStore((s) => s.playerAId);
  const playerAName = useBattleStore((s) => s.playerAName);
  const playerBName = useBattleStore((s) => s.playerBName);

  if (!lastResolution || !lastResolution.log) {
    return (
      <div className="flex flex-col items-center justify-center gap-2 rounded-md border-[0.5px] border-border-subtle bg-glass-subtle px-4 py-6 text-center">
        <p
          className="font-display uppercase"
          style={{
            fontSize: 13,
            letterSpacing: '0.24em',
            color: 'var(--color-accent-text)',
          }}
        >
          Arena Ready
        </p>
        <p className="text-[11px] uppercase tracking-[0.18em] text-text-muted">
          Awaiting first turn
        </p>
      </div>
    );
  }

  const { atoB, btoA, turnIndex } = lastResolution.log;
  const isPlayerA = myId !== null && myId === playerAId;
  const myName = (isPlayerA ? playerAName : playerBName) ?? 'You';
  const opponentName = (isPlayerA ? playerBName : playerAName) ?? 'Opponent';

  const myAttack = isPlayerA ? atoB : btoA;
  const opponentAttack = isPlayerA ? btoA : atoB;

  if (!myAttack || !opponentAttack) {
    return (
      <div className="flex flex-col gap-2 rounded-md border-[0.5px] border-border-subtle bg-glass-subtle p-4">
        <header className="flex items-center justify-between">
          <h3
            className="font-display uppercase"
            style={{
              fontSize: 13,
              letterSpacing: '0.24em',
              color: 'var(--color-accent-text)',
            }}
          >
            Turn {turnIndex}
          </h3>
        </header>
        <p className="text-[11px] uppercase tracking-[0.18em] text-text-muted">Resolving turn…</p>
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-3 rounded-md border-[0.5px] border-border-subtle bg-glass-subtle p-4">
      <header className="flex items-center justify-between">
        <h3
          className="font-display uppercase"
          style={{
            fontSize: 13,
            letterSpacing: '0.24em',
            color: 'var(--color-accent-text)',
          }}
        >
          Turn {turnIndex} Result
        </h3>
      </header>
      <div className="grid grid-cols-1 gap-2 md:grid-cols-2">
        <DirectionRow
          title={`${myName} → ${opponentName}`}
          attack={myAttack}
          defenderName={opponentName}
        />
        <DirectionRow
          title={`${opponentName} → ${myName}`}
          attack={opponentAttack}
          defenderName={myName}
        />
      </div>
    </div>
  );
}

function DirectionRow({
  title,
  attack,
  defenderName,
}: {
  title: string;
  attack: AttackResolutionRealtime;
  defenderName: string;
}) {
  const blockZones = [attack.defenderBlockPrimary, attack.defenderBlockSecondary].filter(
    (z): z is string => z !== null,
  );
  const color = outcomeChipColor(attack.outcome);

  return (
    <div className="flex flex-col gap-1.5 rounded-sm border-[0.5px] border-border-subtle bg-glass-dense p-3">
      <span className="text-[10px] uppercase tracking-[0.24em] text-text-muted">{title}</span>
      <div className="flex items-center justify-between gap-2">
        <span
          className={clsx(
            'inline-flex items-center gap-1 rounded-sm border px-1.5 py-[1px] text-[9px] uppercase tracking-[0.18em]',
          )}
          style={{
            color,
            borderColor: `${color}55`,
            background: `${color}14`,
          }}
        >
          {outcomeLabel[attack.outcome]}
        </span>
        {attack.damage > 0 && (
          <span
            className="font-display tabular-nums"
            style={{
              fontSize: 13,
              letterSpacing: '0.04em',
              color: 'var(--color-kombats-crimson-light)',
            }}
          >
            −{attack.damage} HP
          </span>
        )}
      </div>
      <div className="grid grid-cols-2 gap-2 text-[11px]">
        <div>
          <span className="text-text-muted">Zone: </span>
          <span className="text-text-secondary">{attack.attackZone ?? '—'}</span>
        </div>
        <div>
          <span className="text-text-muted">{defenderName} block: </span>
          <span className="text-text-secondary">
            {blockZones.length > 0 ? blockZones.join(' + ') : '—'}
          </span>
        </div>
      </div>
    </div>
  );
}
