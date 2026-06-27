import { Outlet } from 'react-router';
import bgScene from '@/ui/assets/backgrounds/full_moon.png';

// Layered ink-navy gradients painted over the cover-fit scene image.
// DESIGN_REFERENCE.md §1.2 — top-to-bottom darken + bottom-to-top lighten.
const sceneOverlayStyle = {
  background:
    'linear-gradient(to bottom, rgba(var(--rgb-ink-navy), 0.55) 0%, rgba(var(--rgb-ink-navy), 0.20) 45%, rgba(var(--rgb-ink-navy), 0.85) 100%), linear-gradient(to top, rgba(var(--rgb-ink-navy), 0.65) 0%, transparent 60%)',
};

// Glass header surface — gradient-to-transparent per the TopNavBar pattern
// (DESIGN_REFERENCE.md §5.8). Onboarding has no nav so the header is just
// the wordmark, but the surface treatment matches the rest of the shell.
const headerSurfaceStyle = {
  background:
    'linear-gradient(to bottom, rgba(var(--rgb-black), 0.55) 0%, rgba(var(--rgb-ink-navy), 0.35) 50%, transparent 100%)',
};

// Display wordmark gold halo — DESIGN_REFERENCE.md §3.4 / §5.8.
const wordmarkBloomStyle = {
  textShadow: 'var(--shadow-title-soft)',
};

export function OnboardingShell() {
  return (
    <div className="relative min-h-screen overflow-hidden bg-kombats-ink-navy text-text-primary">
      <img
        src={bgScene}
        alt=""
        aria-hidden
        className="pointer-events-none absolute inset-0 h-full w-full object-cover"
      />
      <div aria-hidden className="pointer-events-none absolute inset-0" style={sceneOverlayStyle} />

      <div className="relative z-10 flex min-h-screen flex-col">
        <header
          className="flex items-center px-8 py-3 backdrop-blur-[10px]"
          style={headerSurfaceStyle}
        >
          <div className="flex items-center gap-3">
            <div
              aria-hidden
              className="kombats-diamond"
              style={
                {
                  '--kombats-diamond-size': '36px',
                  '--kombats-diamond-glyph-size': '18px',
                } as React.CSSProperties
              }
            >
              <span className="kombats-diamond-glyph">拳</span>
            </div>
            <div className="flex flex-col leading-none">
              <span className="text-[9px] uppercase tracking-[0.5em] text-text-muted">The</span>
              <span
                className="mt-1.5 font-display text-[22px] font-semibold uppercase leading-none tracking-[0.34em] text-accent-primary"
                style={wordmarkBloomStyle}
              >
                Kombats
              </span>
            </div>
          </div>
        </header>

        {/* Each onboarding screen owns its own layout — NameSelectionScreen
            uses a full-bleed scene with an anchored fighter sprite, while
            InitialStatsScreen renders a centered glass card. */}
        <main className="relative flex flex-1 flex-col">
          <Outlet />
        </main>
      </div>
    </div>
  );
}
