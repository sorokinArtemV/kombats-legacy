import { Navigate, useNavigate, useParams } from 'react-router';
import { clsx } from 'clsx';
import { useAuthStore } from '@/modules/auth/store';
import { usePlayerStore } from '@/modules/player/store';
import { useBattleStore } from '../store';
import { useBattlePhase, useBattleResult } from '../hooks';
import { deriveOutcome } from '../battle-end-outcome';
import { useRequeueAfterBattle } from '@/modules/matchmaking/hooks';
import { Spinner } from '@/ui/components/Spinner';
import { Button } from '@/ui/components/Button';
import { ATMOSPHERE } from './outcome-atmosphere';

function formatXp(n: number): string {
  return `${n >= 0 ? '+' : '−'}${Math.abs(n).toLocaleString('en-US')} XP`;
}

function formatRating(n: number): string {
  return `${n >= 0 ? '+' : '−'}${Math.abs(n).toLocaleString('en-US')} RP`;
}

export function BattleResultScreen() {
  const { battleId } = useParams<{ battleId: string }>();

  const phase = useBattlePhase();
  const storeBattleId = useBattleStore((s) => s.battleId);

  if (!battleId) {
    return <p className="p-6 text-error">Missing battle ID.</p>;
  }

  // Stale-id guard: if the battle store has rotated to a different battle
  // (e.g. user came back through the result URL after a new battle started),
  // bounce to lobby instead of rendering a result for a battle we no longer
  // hold context for.
  if (storeBattleId !== null && storeBattleId !== battleId) {
    return <Navigate to="/lobby" replace />;
  }

  if (phase !== 'Ended') {
    return (
      <div className="flex flex-1 items-center justify-center">
        <Spinner size="lg" />
      </div>
    );
  }

  return <EndedResultPanel battleId={battleId} />;
}

interface EndedResultPanelProps {
  battleId: string;
}

/**
 * Renders the final result composition. Mounted only when the battle store
 * is in `phase === 'Ended'` so requeue setup, atmosphere lookup, and reward
 * derivation never run during the pre-Ended spinner / stale-id guard paths.
 */
function EndedResultPanel({ battleId }: EndedResultPanelProps) {
  const navigate = useNavigate();
  const { endReason, winnerPlayerId, xpAwarded, ratingDelta } = useBattleResult();
  const myId = useAuthStore((s) => s.userIdentityId);
  const playerAId = useBattleStore((s) => s.playerAId);
  const playerAName = useBattleStore((s) => s.playerAName);
  const playerBName = useBattleStore((s) => s.playerBName);

  const { state: requeueState, requeue } = useRequeueAfterBattle();

  const { outcome, title, subtitle } = deriveOutcome(endReason, winnerPlayerId, myId);
  const atm = ATMOSPHERE[outcome];

  const isPlayerA = myId !== null && myId === playerAId;
  const myName = (isPlayerA ? playerAName : playerBName) ?? 'You';
  const opponentName = (isPlayerA ? playerBName : playerAName) ?? 'Opponent';

  // Rewards visibility: any present field renders the Rewards section.
  // SystemError suppresses rewards entirely — there is no clean win/loss
  // attribution when the engine could not finish the battle.
  const hasXp = xpAwarded !== null && outcome !== 'systemError';
  const hasRating = ratingDelta !== null && outcome !== 'systemError';
  const showRewards = hasXp || hasRating;

  const handleReturn = () => {
    // Atomic post-battle handoff. Marks the battle dismissed (so stale
    // `queueStatus.Matched.<battleId>` refetches are suppressed until the
    // backend projection catches up), clears any active queue entry, and
    // flags the next lobby mount to run the DEC-5 XP/level refresh.
    usePlayerStore.getState().returnFromBattle(battleId);
    // Lobby is the destination; React Router will run the BattleShell
    // unmount which resets the battle store (preserving lastBattleLog /
    // lastTurnHistory for the dock).
    navigate('/lobby');
  };

  const requeuePending = requeueState === 'pending';
  const showPrimaryCta = outcome !== 'systemError';

  return (
    <div className="relative flex h-full min-h-0 flex-1 flex-col overflow-hidden">
      {/* Scene background and atmosphere are owned by SessionShell — see ResultBackground mount there. */}

      {/* Content column. min-h-0 + overflow-y-auto so a short viewport
          (with the dock taking the bottom band) still scrolls instead of
          clipping the panel. Heavy bottom padding (300px) biases the
          justify-center math upward by ~150px so the title row sits near
          the moon's lower edge instead of floating in empty atmosphere
          well below it — title + subtitle + panel read as one composition
          with the moon. The dock is fixed-positioned by SessionShell so
          this padding does not affect dock placement. */}
      <div className="relative z-10 flex h-full min-h-0 flex-1 flex-col items-center justify-center gap-6 overflow-y-auto px-4 pt-8 pb-[332px]">
        {/* Title row */}
        <div className="flex items-center justify-center gap-5">
          <div aria-hidden style={{ width: 60, height: 1, ...atm.wingLeftStyle }} />
          <h1
            className={clsx('font-display uppercase', atm.titleClass)}
            style={{
              fontSize: 56,
              fontWeight: 700,
              letterSpacing: '0.16em',
              lineHeight: 1,
              textShadow: atm.titleShadow,
            }}
          >
            {title}
          </h1>
          <div aria-hidden style={{ width: 60, height: 1, ...atm.wingRightStyle }} />
        </div>

        <p
          className="text-center text-text-muted"
          style={{
            fontSize: 12,
            letterSpacing: '0.24em',
            textTransform: 'uppercase',
          }}
        >
          {subtitle}
        </p>

        {/* Glass result panel */}
        <div
          className="relative w-full max-w-[520px] overflow-hidden rounded-md border-[0.5px] border-border-subtle bg-glass"
          style={{
            backdropFilter: 'blur(20px)',
            WebkitBackdropFilter: 'blur(20px)',
            boxShadow: 'var(--shadow-panel-lift)',
          }}
        >
          <div
            aria-hidden
            className="absolute left-0 right-0 top-0"
            style={{ height: 3, ...atm.accentLineStyle }}
          />

          <div className="flex flex-col gap-5 px-6 py-6">
            <div className="grid grid-cols-2 gap-6">
              <NamePanel
                roleLabel={atm.myRoleLabel}
                name={myName}
                nameStyle={atm.myNameStyle}
                statusLabel={atm.myStatusLabel}
                statusClass={atm.myStatusClass}
              />
              <NamePanel
                roleLabel={atm.oppRoleLabel}
                name={opponentName}
                nameStyle={atm.oppNameStyle}
                statusLabel={atm.oppStatusLabel}
                statusClass={atm.oppStatusClass}
              />
            </div>

            {showRewards && (
              <>
                <div aria-hidden style={{ borderTop: '1px solid var(--color-border-divider)' }} />
                <div className="flex flex-col gap-2">
                  <p
                    className="text-center uppercase"
                    style={{
                      fontSize: 11,
                      fontWeight: 600,
                      letterSpacing: '0.16em',
                      color: 'var(--color-text-secondary)',
                    }}
                  >
                    Rewards
                  </p>
                  {hasXp && xpAwarded !== null && (
                    <RewardRow label="XP Gained" value={formatXp(xpAwarded)} tone="positive" />
                  )}
                  {hasRating && ratingDelta !== null && (
                    <RewardRow
                      label={ratingDelta >= 0 ? 'Rating Gained' : 'Rating Lost'}
                      value={formatRating(ratingDelta)}
                      tone={ratingDelta >= 0 ? 'positive' : 'negative'}
                    />
                  )}
                </div>
              </>
            )}

            <div className="flex flex-wrap items-center justify-center gap-3 pt-2">
              {showPrimaryCta && (
                <Button
                  type="button"
                  variant="primary"
                  size="md"
                  onClick={requeue}
                  loading={requeuePending}
                  disabled={requeuePending}
                >
                  {requeuePending ? 'Preparing…' : atm.primaryCtaLabel}
                </Button>
              )}
              <Button type="button" variant="secondary" size="md" onClick={handleReturn}>
                Return to Lobby
              </Button>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

interface NamePanelProps {
  roleLabel: string;
  name: string;
  /** Outcome-specific name color override (e.g. dimmed loser on victory). */
  nameStyle?: React.CSSProperties;
  statusLabel: string;
  statusClass: string;
}

function NamePanel({ roleLabel, name, nameStyle, statusLabel, statusClass }: NamePanelProps) {
  return (
    <div className="flex flex-col items-center gap-1 text-center">
      <span
        className="uppercase text-text-muted"
        style={{
          fontSize: 11,
          fontWeight: 500,
          letterSpacing: '0.12em',
        }}
      >
        {roleLabel}
      </span>
      <span
        className="font-display text-text-primary"
        style={{
          fontSize: 22,
          fontWeight: 600,
          letterSpacing: '0.08em',
          textShadow: 'var(--shadow-text-on-glass)',
          ...nameStyle,
        }}
      >
        {name}
      </span>
      {statusLabel && (
        <span
          className={clsx('uppercase', statusClass)}
          style={{
            fontSize: 11,
            fontWeight: 600,
            letterSpacing: '0.12em',
          }}
        >
          {statusLabel}
        </span>
      )}
    </div>
  );
}

interface RewardRowProps {
  label: string;
  value: string;
  tone: 'positive' | 'negative';
}

function RewardRow({ label, value, tone }: RewardRowProps) {
  return (
    <div className="flex items-center justify-between rounded-sm border-[0.5px] border-border-subtle bg-glass-subtle px-4 py-2">
      <span
        className="uppercase text-text-secondary"
        style={{
          fontSize: 12,
          fontWeight: 500,
          letterSpacing: '0.12em',
        }}
      >
        {label}
      </span>
      <span
        className={clsx(
          'tabular-nums',
          tone === 'positive' ? 'text-kombats-jade' : 'text-kombats-crimson',
        )}
        style={{
          fontSize: 14,
          fontWeight: 700,
          letterSpacing: '0.08em',
        }}
      >
        {value}
      </span>
    </div>
  );
}
