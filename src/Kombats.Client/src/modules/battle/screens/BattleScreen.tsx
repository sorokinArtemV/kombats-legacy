import { useMemo } from 'react';
import { Navigate, useParams } from 'react-router';
import { motion, useReducedMotion } from 'motion/react';
import { useBattlePhase, useBattleHp } from '../hooks';
import { useBattleStore } from '../store';
import { useAuthStore } from '@/modules/auth/store';
import { usePlayerCard } from '@/modules/player/hooks';
import { BodyZoneSelector } from '../components/BodyZoneSelector';
import { TurnResultPanel } from '../components/TurnResultPanel';
import { Banner } from '../components/Banner';
import { CombatPanelHeader } from '../components/CombatPanelHeader';
import { CombatMetaRow } from '../components/CombatMetaRow';
import { ConnectionLostBanner } from '../components/ConnectionLostBanner';
import { LeaveBattleEscape } from '../components/LeaveBattleEscape';
import { FighterNameplate } from '@/modules/player/components/FighterNameplate';
import { Spinner } from '@/ui/components/Spinner';
import { ErrorBoundary } from '@/ui/components/ErrorBoundary';
import { logger } from '@/app/logger';
import { getAvatarAsset } from '@/modules/player/avatar-assets';

// DESIGN_REFERENCE.md §1.3 — match LobbyScreen exactly (transparent → ink-navy).
// BattleScreen reuses the lobby's two-stop gradient so the lobby ↔ battle
// hand-off reads as a center-overlay swap, not a scene change.
const sceneOverlayStyle: React.CSSProperties = {
  background:
    'linear-gradient(to bottom, transparent 0%, rgba(var(--rgb-ink-navy), 0.30) 60%, rgba(var(--rgb-ink-navy), 0.60) 100%)',
};

// DESIGN_REFERENCE.md §3.16 — oversized sprite drop shadow + lobby anchor
// (sprite extends 17vh below the viewport so the bottom of the silhouette is
// cropped, identical to LobbyScreen's spriteStyle).
const selfSpriteStyle: React.CSSProperties = {
  filter: 'drop-shadow(0 25px 50px rgba(var(--rgb-black), 0.9))',
  marginBottom: '-17vh',
};

// DESIGN_REFERENCE.md §3.19 — mirrored opponent sprite. Hue rotation only
// applies when both fighters happen to share the same avatar art (so the
// player can still tell them apart); distinct avatars render naturally.
const opponentSpriteFilterBase = 'drop-shadow(0 25px 50px rgba(var(--rgb-black), 0.9))';

// Module-level cached style objects so render-stable parents pass the same
// reference each render — React skips style-diff work when the object identity
// is unchanged.
const opponentSpriteInnerStyleNoHue: React.CSSProperties = {
  filter: opponentSpriteFilterBase,
};
const opponentSpriteInnerStyleHue: React.CSSProperties = {
  filter: `${opponentSpriteFilterBase} hue-rotate(180deg)`,
};

// Opponent sprite mirrors the player's lobby anchor so both fighters meet at
// the same bottom band of the scene.
const opponentSpriteOuterStyle: React.CSSProperties = {
  marginBottom: '-17vh',
};

// transform: scaleX(-1) is the only dynamic-looking field but it's actually
// constant; spreading the outer style with a fixed transform every render
// produced a fresh object. Pre-merge once.
const opponentSpriteOuterMirroredStyle: React.CSSProperties = {
  ...opponentSpriteOuterStyle,
  transform: 'scaleX(-1)',
};

/**
 * Battle layout. Full-bleed scene with bottom-anchored player/opponent
 * sprites + their nameplates, and a centered 540px combat panel containing
 * the round/timer/turn meta row and BodyZoneSelector.
 */
export function BattleScreen() {
  const { battleId: routeBattleId } = useParams<{ battleId: string }>();
  const phase = useBattlePhase();
  const lastError = useBattleStore((s) => s.lastError);
  const myId = useAuthStore((s) => s.userIdentityId);
  const playerAId = useBattleStore((s) => s.playerAId);
  const playerBId = useBattleStore((s) => s.playerBId);
  const playerAName = useBattleStore((s) => s.playerAName) ?? 'Player A';
  const playerBName = useBattleStore((s) => s.playerBName) ?? 'Player B';
  const { playerAHp, playerBHp, playerAMaxHp, playerBMaxHp } = useBattleHp();

  // Self/opponent derivation — DO NOT REORDER. BattleScreen downstream and
  // TurnResultPanel both depend on this being `myId === playerAId`.
  const isPlayerA = myId !== null && myId === playerAId;
  const myFighterId = isPlayerA ? playerAId : playerBId;
  const oppFighterId = isPlayerA ? playerBId : playerAId;
  // Self name comes from playerStore via the default FighterNameplate; only
  // the opponent's display name needs to be derived from the battle store.
  const oppName = isPlayerA ? playerBName : playerAName;
  const myHp = isPlayerA ? playerAHp : playerBHp;
  const oppHp = isPlayerA ? playerBHp : playerAHp;
  const myMaxHp = isPlayerA ? playerAMaxHp : playerBMaxHp;
  const oppMaxHp = isPlayerA ? playerBMaxHp : playerAMaxHp;

  const myCardQuery = usePlayerCard(myFighterId ?? '', !!myFighterId);
  const oppCardQuery = usePlayerCard(oppFighterId ?? '', !!oppFighterId);
  const myCard = myCardQuery.data ?? null;
  const oppCard = oppCardQuery.data ?? null;

  const myAvatarSrc = getAvatarAsset(myCard?.avatarId);
  const oppAvatarSrc = getAvatarAsset(oppCard?.avatarId);
  const opponentSpriteInnerStyle = useMemo(
    () =>
      myCard?.avatarId && oppCard?.avatarId === myCard.avatarId
        ? opponentSpriteInnerStyleHue
        : opponentSpriteInnerStyleNoHue,
    [myCard?.avatarId, oppCard?.avatarId],
  );

  const reduceMotion = useReducedMotion();

  // BattleEnded → result screen. State-driven projection: as soon as the
  // store transitions to phase='Ended', BattleShell keeps the hub mounted
  // and the route swap renders BattleResultScreen under the same shell. No
  // intermediate dialog. `replace` so the live battle URL is not left in
  // history (the result is the terminal destination of this battle).
  if (phase === 'Ended' && routeBattleId) {
    return <Navigate to={`/battle/${routeBattleId}/result`} replace />;
  }

  const isLoading = phase === 'Idle' || phase === 'Connecting' || phase === 'WaitingForJoin';

  if (isLoading) {
    return (
      <div className="relative flex h-full flex-1 items-center justify-center overflow-hidden">
        <div
          aria-hidden
          className="pointer-events-none absolute inset-0"
          style={sceneOverlayStyle}
        />
        <div className="relative z-10 flex flex-col items-center gap-3">
          <Spinner size="lg" />
          <p className="text-[11px] uppercase tracking-[0.24em] text-text-muted">
            Connecting to the arena…
          </p>
        </div>
      </div>
    );
  }

  return (
    <div className="relative h-full min-h-0 overflow-hidden">
      {/* Scene overlay — UI-contrast gradient. The arena background itself
          is painted by SessionShell via --arena-bg-image and shows through
          this <main><Outlet/></main> tree. */}
      <div aria-hidden className="pointer-events-none absolute inset-0" style={sceneOverlayStyle} />

      {/* LEFT — local player. Anchored bottom-left, sprite renders normally
          (no scaleX), HP bar is jade/green via the default `friendly` tone,
          no mirror. Layout (items-center, h-[82vh], marginBottom -17vh) is
          identical to LobbyScreen so the lobby ↔ battle hand-off reads as a
          center-overlay swap, not a scene change. */}
      <div className="pointer-events-none absolute bottom-0 left-0 z-10 flex flex-col items-center">
        <div className="pointer-events-auto">
          <FighterNameplate tone="friendly" hp={myHp} maxHp={myMaxHp} />
        </div>
        <motion.img
          src={myAvatarSrc}
          alt=""
          aria-hidden
          className="pointer-events-none h-[82vh] w-auto object-contain"
          style={selfSpriteStyle}
          initial={reduceMotion ? false : { opacity: 0, x: -50 }}
          animate={reduceMotion ? undefined : { opacity: 1, x: 0 }}
          transition={reduceMotion ? undefined : { duration: 0.5 }}
        />
      </div>

      {/* RIGHT — opponent. Anchored bottom-right, sprite mirrored via
          scaleX(-1) so the fighter faces the player, HP bar is crimson/red
          via tone="hostile" and visually mirrored via hpBarMirror so it
          depletes right-to-left. Mirrors the LEFT block geometry so both
          fighters anchor at identical heights. */}
      <div className="pointer-events-none absolute bottom-0 right-0 z-10 flex flex-col items-center">
        <div className="pointer-events-auto">
          <FighterNameplate
            name={oppName}
            level={oppCard?.level ?? null}
            hp={oppHp}
            maxHp={oppMaxHp}
            card={oppCard}
            tone="hostile"
            hpBarMirror
            alignRight
          />
        </div>
        <div
          className="pointer-events-none h-[82vh] w-auto"
          style={opponentSpriteOuterMirroredStyle}
        >
          <motion.img
            src={oppAvatarSrc}
            alt=""
            aria-hidden
            className="pointer-events-none h-full w-auto object-contain"
            style={opponentSpriteInnerStyle}
            initial={reduceMotion ? false : { opacity: 0, x: -50 }}
            animate={reduceMotion ? undefined : { opacity: 1, x: 0 }}
            transition={reduceMotion ? undefined : { duration: 0.5 }}
          />
        </div>
      </div>

      {/* Center combat panel. Offset up so it clears the nameplates on shorter
          viewports, with min-height safety. */}
      <div className="absolute left-1/2 top-1/2 z-20 w-[min(380px,calc(100%-2rem))] -translate-x-1/2 -translate-y-[55%]">
        <section
          className="rounded-md border-[0.5px] border-border-subtle bg-glass p-5 shadow-[var(--shadow-panel-lift)]"
          style={{ backdropFilter: 'blur(20px)', WebkitBackdropFilter: 'blur(20px)' }}
        >
          <CombatPanelHeader />
          <CombatMetaRow />

          <div className="my-4 h-px bg-border-divider" aria-hidden />

          {phase === 'Error' && lastError && (
            <div className="mb-3 flex flex-col gap-2">
              <Banner tone="error" id="battle-error-banner">
                {lastError}
              </Banner>
              <LeaveBattleEscape describedBy="battle-error-banner" />
            </div>
          )}
          {phase === 'ConnectionLost' && (
            <div className="mb-3">
              <ConnectionLostBanner />
            </div>
          )}

          <ErrorBoundary
            fallback={
              <Banner tone="error">
                Something went wrong rendering this turn. Waiting for the next server update…
              </Banner>
            }
            onError={(error) => {
              logger.error('BattleScreen render error', error);
            }}
          >
            <ActionPanelSlot />
          </ErrorBoundary>
        </section>
      </div>
    </div>
  );
}

function ActionPanelSlot() {
  const phase = useBattlePhase();

  if (phase === 'TurnOpen' || phase === 'Submitted' || phase === 'Resolving') {
    return <BodyZoneSelector />;
  }
  // For ArenaOpen / Ended / fallback show the latest turn result.
  return <TurnResultPanel />;
}
