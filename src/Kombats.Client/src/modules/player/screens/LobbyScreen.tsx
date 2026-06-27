import { useState, useEffect } from 'react';
import { StatAllocationPanel } from '../components/StatAllocationPanel';
import { LevelUpBanner } from '../components/LevelUpBanner';
import { FighterNameplate } from '../components/FighterNameplate';
import { QueueButton } from '@/modules/matchmaking/components/QueueButton';
import { SearchingIndicator } from '@/modules/matchmaking/components/SearchingIndicator';
import {
  useMatchmaking,
  useMatchmakingPolling,
  useQueueHeartbeat,
} from '@/modules/matchmaking/hooks';
import { Button } from '@/ui/components/Button';
import { usePlayerStore } from '../store';
import { usePostBattleRefresh } from '../post-battle-refresh';
import { isApiError } from '@/types/api';
import { getAvatarAsset } from '../avatar-assets';

// DESIGN_REFERENCE.md §1.3 — full-bleed scene + ink-navy bottom gradient.
// Two-stop gradient (transparent → ink-navy/30 → ink-navy/60) darkens the
// bottom so the fighter sprite/nameplate read over scene art.
const sceneOverlayStyle: React.CSSProperties = {
  background:
    'linear-gradient(to bottom, transparent 0%, rgba(var(--rgb-ink-navy), 0.30) 60%, rgba(var(--rgb-ink-navy), 0.60) 100%)',
};

// DESIGN_REFERENCE.md §3.16 — oversized sprite drop shadow.
const spriteStyle: React.CSSProperties = {
  filter: 'drop-shadow(0 25px 50px rgba(var(--rgb-black), 0.9))',
  marginBottom: '-17vh',
};

/**
 * Lobby — full-bleed scene with bottom-left fighter anchor and a centered
 * overlay that swaps based on queue UI status:
 *   - searching/matched/battleTransition → SearchingCard
 *   - unspent stat points → LevelUpBanner + StatAllocationPanel
 *   - otherwise → QueueCard
 *
 * Background, sprite, and nameplate stay mounted across the swap so the
 * idle ↔ searching transition reads as a center-overlay change, not a
 * scene change (no flicker, no remount).
 *
 * All hooks are called unconditionally at the top, before any branching.
 *
 * `usePostBattleRefresh()` runs once per arrival to reconcile XP/level
 * after a battle (DEC-5). `useMatchmakingPolling()` is self-gating — it
 * only starts the poller when the derived status is searching/matched.
 */
export function LobbyScreen() {
  usePostBattleRefresh();

  const { status, searchStartedAt, consecutiveFailures, leaveQueue } = useMatchmaking();
  useMatchmakingPolling();
  useQueueHeartbeat();

  const [cancelling, setCancelling] = useState(false);
  const [cancelError, setCancelError] = useState<string | null>(null);

  const character = usePlayerStore((s) => s.character);
  const hasUnspentPoints = (character?.unspentPoints ?? 0) > 0;

  const handleCancel = async () => {
    setCancelling(true);
    setCancelError(null);
    try {
      await leaveQueue();
    } catch (err: unknown) {
      setCancelError(extractCancelErrorMessage(err));
    } finally {
      setCancelling(false);
    }
  };

  const isSearching =
    status === 'searching' || status === 'matched' || status === 'battleTransition';

  return (
    <div className="relative h-full min-h-0 overflow-hidden">
      <div aria-hidden className="pointer-events-none absolute inset-0" style={sceneOverlayStyle} />

      <div className="pointer-events-none absolute bottom-0 left-0 z-10 flex flex-col items-center">
        <div className="pointer-events-auto">
          <FighterNameplate />
        </div>
        <img
          src={getAvatarAsset(character?.avatarId)}
          alt=""
          aria-hidden
          className="pointer-events-none h-[82vh] w-auto object-contain"
          style={spriteStyle}
        />
      </div>

      <div
        className="absolute left-1/2 top-1/2 z-20 w-80 max-w-[calc(100%-3rem)]"
        style={{ transform: 'translate(-50%, -55%)' }}
      >
        {isSearching ? (
          <SearchingCard
            status={status}
            searchStartedAt={searchStartedAt}
            consecutiveFailures={consecutiveFailures}
            cancelling={cancelling}
            onCancel={handleCancel}
            cancelError={cancelError}
          />
        ) : hasUnspentPoints ? (
          <div className="flex flex-col gap-4">
            <LevelUpBanner />
            <StatAllocationPanel />
          </div>
        ) : (
          <QueueCard />
        )}
      </div>
    </div>
  );
}

/**
 * DESIGN_REFERENCE.md §5.10 (ready state). Glass panel with PanelHeader
 * caption title (NOT a display heading), centered Join Queue button (natural
 * width), divider, then a centered Battle Type label-above-value footer.
 */
function QueueCard() {
  return (
    <section className="rounded-md border-[0.5px] border-border-subtle bg-glass shadow-[var(--shadow-panel)] backdrop-blur-[20px]">
      <div className="p-6">
        <div className="mb-4 px-0 text-center text-[11px] font-medium uppercase tracking-[0.18em] text-text-muted">
          Ready to Fight
        </div>

        <div className="flex justify-center">
          <QueueButton />
        </div>

        <div className="my-4 border-t border-border-divider" aria-hidden />

        <div className="text-center">
          <span className="text-[11px] font-medium uppercase tracking-[0.18em] text-text-muted">
            Battle Type
          </span>
          <span className="mt-1 block text-[16px] font-medium uppercase tracking-[0.08em] text-accent-text">
            Fist Fight
          </span>
        </div>
      </div>
    </section>
  );
}

interface SearchingCardProps {
  status: ReturnType<typeof useMatchmaking>['status'];
  searchStartedAt: number | null;
  consecutiveFailures: number;
  cancelling: boolean;
  onCancel: () => void;
  cancelError: string | null;
}

/**
 * DESIGN_REFERENCE.md §5.10 (searching state). Glass panel mirroring the
 * QueueCard geometry with a Mitsudomoe spinner, elapsed timer, cancel
 * action, and the Finding / Worthy Challenger footer.
 */
function SearchingCard({
  status,
  searchStartedAt,
  consecutiveFailures,
  cancelling,
  onCancel,
  cancelError,
}: SearchingCardProps) {
  const title =
    status === 'matched'
      ? 'Opponent Found'
      : status === 'battleTransition'
        ? 'Entering Battle'
        : 'Searching for Opponent';
  const showElapsed = status === 'searching';
  const canCancel = status === 'searching' || status === 'matched';

  return (
    <section className="rounded-md border-[0.5px] border-border-subtle bg-glass shadow-[var(--shadow-panel)] backdrop-blur-[20px]">
      <div className="p-6">
        <div className="mb-4 px-0 text-center text-[11px] font-medium uppercase tracking-[0.18em] text-accent-text">
          {title}
        </div>

        <div className="mb-4 flex justify-center">
          <SearchingIndicator />
        </div>

        {showElapsed && searchStartedAt !== null && (
          <div
            className="mb-4 flex items-center justify-center gap-2 text-text-secondary"
            aria-live="polite"
          >
            <ClockIcon />
            <ElapsedTime searchStartedAt={searchStartedAt} />
          </div>
        )}

        {consecutiveFailures >= 3 && (
          <p
            className="mb-4 text-center text-[11px] uppercase tracking-[0.18em] text-kombats-crimson-light"
            role="alert"
          >
            Connection issues — retrying…
          </p>
        )}

        {canCancel && (
          <div className="flex justify-center">
            <Button
              variant="secondary"
              size="lg"
              onClick={onCancel}
              loading={cancelling}
              disabled={cancelling}
            >
              Cancel Search
            </Button>
          </div>
        )}

        {cancelError && (
          <p
            className="mt-3 text-center text-[11px] uppercase tracking-[0.18em] text-kombats-crimson-light"
            role="alert"
          >
            {cancelError}
          </p>
        )}
      </div>
    </section>
  );
}

function extractCancelErrorMessage(err: unknown): string {
  if (isApiError(err)) {
    if (err.status >= 500) return 'Matchmaking is temporarily unavailable.';
    if (err.error?.message) return err.error.message;
  }
  return 'Could not cancel the search. Please try again.';
}

/**
 * Displays elapsed seconds since `searchStartedAt`, ticking every second.
 * Isolated from `LobbyScreen` so the per-second re-render is scoped to this
 * tiny subtree instead of cascading through the full lobby (sprite,
 * nameplate, queue card).
 */
function ElapsedTime({ searchStartedAt }: { searchStartedAt: number }) {
  // setState inside the setInterval callback is deferred (not synchronous in
  // the effect body), and the lazy useState initializer runs only on the
  // first render — both keep the component compatible with the React 19
  // hooks-purity / set-state-in-effect rules.
  const [elapsed, setElapsed] = useState(() => Math.floor((Date.now() - searchStartedAt) / 1000));

  useEffect(() => {
    const timer = setInterval(() => {
      setElapsed(Math.floor((Date.now() - searchStartedAt) / 1000));
    }, 1000);
    return () => clearInterval(timer);
  }, [searchStartedAt]);

  return <span className="text-[18px] tabular-nums">{elapsed}s</span>;
}

function ClockIcon() {
  return (
    <svg
      width="14"
      height="14"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden
    >
      <circle cx="12" cy="12" r="10" />
      <polyline points="12 6 12 12 16 14" />
    </svg>
  );
}
