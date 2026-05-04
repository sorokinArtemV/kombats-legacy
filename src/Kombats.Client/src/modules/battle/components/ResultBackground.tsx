import { useAuthStore } from '@/modules/auth/store';
import { useBattlePhase, useBattleResult } from '../hooks';
import { deriveOutcome } from '../battle-end-outcome';

// Pre-built once at module load. 12×30° = 24 alternating gold beams,
// painted statically as a faint halo behind the title. The earlier
// 60s ceremonial rotation was dropped to remove gold-on-gold visual
// overload — gold should appear in only one prominent place on the
// result screen (the VICTORY title), so the rays read as a background
// hint, not an attraction. Driven from CSS-variable RGB tokens so a
// future reskin only touches tokens.css.
const VICTORY_RAYS_BACKGROUND = (() => {
  const stops: string[] = [];
  for (let i = 0; i < 12; i++) {
    const base = i * 30;
    stops.push(`rgba(var(--rgb-gold-victory), 0.08) ${base}deg ${base + 8}deg`);
    stops.push(`transparent ${base + 8}deg ${base + 15}deg`);
    stops.push(`rgba(var(--rgb-gold-victory), 0.06) ${base + 15}deg ${base + 23}deg`);
    stops.push(`transparent ${base + 23}deg ${base + 30}deg`);
  }
  return `conic-gradient(from 0deg, ${stops.join(', ')})`;
})();

// Tight halo around the title, fading well before the panel so the panel
// reads on clean dark space rather than animated texture.
const VICTORY_RAYS_MASK = 'radial-gradient(circle, black 15%, transparent 40%)';

// Atmosphere overlays — heavier ink darkening on victory so the bright
// gold title keeps contrast, lighter on defeat so the red vignette
// reads as the dominant edge treatment. Painted first inside this
// component so they sit above SessionShell's bg-1.png (the temple) but
// below the outcome-specific bright layers (rays, blooms, vignette,
// sword stroke), darkening the whole scene as a moody base coat.
const VICTORY_OVERLAY: React.CSSProperties = {
  background: 'rgba(var(--rgb-black), 0.65)',
};
const DEFEAT_OVERLAY: React.CSSProperties = {
  background: 'rgba(var(--rgb-black), 0.50)',
};

// Defeat edge vignette (closing-in red) + two-layer victory bloom (gold
// ring + white core). All pulled from prod tokens — never hex.
const DEFEAT_VIGNETTE: React.CSSProperties = {
  background:
    'radial-gradient(ellipse at 50% 50%, transparent 30%, rgba(var(--rgb-crimson), 0.10) 65%, rgba(var(--rgb-crimson), 0.18) 95%)',
};
const VICTORY_GOLD_BLOOM: React.CSSProperties = {
  background:
    'radial-gradient(circle, rgba(var(--rgb-gold-victory), 0.15) 0%, rgba(var(--rgb-gold-victory), 0.06) 45%, transparent 70%)',
};
const VICTORY_WHITE_BLOOM: React.CSSProperties = {
  background:
    'radial-gradient(circle, rgba(var(--rgb-white), 0.22) 0%, rgba(var(--rgb-white), 0.06) 40%, transparent 65%)',
};

/**
 * Atmospheric scene for the post-battle result route. Rendered by SessionShell
 * (not by BattleResultScreen) so it sits as a sibling of bg-1.png BEHIND the
 * `relative z-10` stacking context that hosts the AppHeader. With this
 * placement the translucent header shows the result atmosphere through it the
 * same way it shows bg-1.png on lobby — no horizontal seam at the bottom of
 * the header band, no per-outcome hack on the header itself.
 *
 * All atmosphere layers anchor with `position: fixed; inset: 0` so they span
 * the viewport regardless of where SessionShell's flex children land. With
 * `absolute inset-0` against SessionShell's wrapper the layers stop at the
 * top of BottomDock (a sibling that takes its own space in the flex column),
 * which produced a visible horizontal seam on shorter viewports — the dock
 * has its own opaque background, so atmosphere extending behind it is fine
 * and consistent with the lobby scene.
 *
 * Outcome derivation guards: render nothing until the battle store has a
 * resolved end state AND auth identity is known. Without those guards a
 * direct URL refresh on /battle/:id/result could briefly flash the defeat
 * variant while `myId` is still null (deriveOutcome's victory branch needs
 * `winnerId === myId`, so a null myId always falls through to defeat).
 * SystemError suppresses the dramatic atmosphere too — the result panel
 * itself communicates the engine failure; an extra red vignette would be
 * dishonest about the cause.
 */
export function ResultBackground() {
  const phase = useBattlePhase();
  const { endReason, winnerPlayerId } = useBattleResult();
  const myId = useAuthStore((s) => s.userIdentityId);

  if (phase !== 'Ended') return null;
  if (myId === null) return null;

  const { outcome } = deriveOutcome(endReason, winnerPlayerId, myId);
  if (outcome === 'systemError') return null;

  if (outcome === 'victory') {
    return (
      <>
        <div aria-hidden className="pointer-events-none fixed inset-0" style={VICTORY_OVERLAY} />
        <div
          aria-hidden
          className="pointer-events-none fixed top-1/2 left-1/2"
          style={{
            width: '150vmax',
            height: '150vmax',
            marginTop: '-75vmax',
            marginLeft: '-75vmax',
            borderRadius: '50%',
            background: VICTORY_RAYS_BACKGROUND,
            WebkitMaskImage: VICTORY_RAYS_MASK,
            maskImage: VICTORY_RAYS_MASK,
          }}
        />
        <div
          aria-hidden
          className="pointer-events-none fixed left-1/2 top-1/4 -translate-x-1/2 -translate-y-1/2"
          style={{
            width: 380,
            height: 380,
            borderRadius: '50%',
            ...VICTORY_GOLD_BLOOM,
          }}
        />
        <div
          aria-hidden
          className="pointer-events-none fixed left-1/2 top-1/4 -translate-x-1/2 -translate-y-1/2"
          style={{
            width: 200,
            height: 200,
            borderRadius: '50%',
            ...VICTORY_WHITE_BLOOM,
          }}
        />
      </>
    );
  }

  // outcome === 'defeat'
  // Single katana stroke as a fixed 900×900 SVG centered on the
  // viewport with `overflow: visible`. Earlier viewport-spanning
  // approaches (`preserveAspectRatio="xMidYMid slice"`/`meet` on a
  // full-viewport SVG) deformed the rotated ellipses on wide aspect
  // ratios — at 1920×1080 a 400×400 viewBox stretches 4.8× wide vs
  // 2.7× tall, turning the lens into either a fat oval (`meet`) or
  // an edge-to-edge laser stripe (`slice`/`none`). Fixed pixel size
  // + flexbox-centered container preserves the geometry the design
  // was tuned in. The 900px size is designer-tuned for 1920×1080;
  // smaller viewports will see a proportionally larger slash —
  // acceptable for now, may revisit responsive sizing separately.
  return (
    <>
      <div aria-hidden className="pointer-events-none fixed inset-0" style={DEFEAT_OVERLAY} />
      <div aria-hidden className="pointer-events-none fixed inset-0" style={DEFEAT_VIGNETTE} />
      <div
        aria-hidden
        className="pointer-events-none fixed inset-0 flex items-center justify-center"
      >
        <svg
          width="900"
          height="900"
          viewBox="0 0 400 400"
          opacity={0.7}
          style={{ overflow: 'visible', transform: 'translateY(-5vh)' }}
        >
          <defs>
            <filter id="defeat-slash-glow" x="-50%" y="-50%" width="200%" height="200%">
              <feGaussianBlur stdDeviation="3" result="blur" />
              <feMerge>
                <feMergeNode in="blur" />
                <feMergeNode in="SourceGraphic" />
              </feMerge>
            </filter>
          </defs>
          <ellipse
            cx="200"
            cy="200"
            rx="12"
            ry="160"
            transform="rotate(-50 200 200)"
            fill="rgba(var(--rgb-crimson), 0.12)"
          />
          <ellipse
            cx="200"
            cy="200"
            rx="5"
            ry="145"
            transform="rotate(-50 200 200)"
            fill="rgba(var(--rgb-crimson), 0.35)"
          />
          <ellipse
            cx="200"
            cy="200"
            rx="0.8"
            ry="140"
            transform="rotate(-50 200 200)"
            fill="rgba(255,90,90,0.95)"
            filter="url(#defeat-slash-glow)"
          />
        </svg>
      </div>
    </>
  );
}
