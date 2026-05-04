import { useEffect } from 'react';
import { Outlet, useMatch } from 'react-router';
import { useChatConnection } from '@/modules/chat/hooks';
import { AppHeader } from '@/app/AppHeader';
import { BottomDock } from '@/app/BottomDock';
import { useNetworkRecovery } from '@/app/useNetworkRecovery';
import { ResultBackground } from '@/modules/battle/components/ResultBackground';
import { useBattleStore } from '@/modules/battle/store';

/**
 * Owns the session-scoped chrome (top header + persistent bottom chat dock)
 * and the session-scoped chat connection. The chat connection survives
 * lobby ↔ battle ↔ result navigation because it is mounted above
 * `BattleGuard`. The dock is present on every authenticated screen so the
 * BATTLE LOG / Round Map tabs stay reachable through the post-battle
 * result screen as well — its content is driven off `lastBattleLog` /
 * `lastTurnHistory` regardless of the current route.
 *
 * The scene background covers the full viewport — including behind the
 * header — so the translucent glass header reads against the scene rather
 * than against a flat ink-navy gap. Individual screens paint their own
 * scene+overlay on top inside main.
 */
export function SessionShell() {
  useChatConnection();
  useNetworkRecovery();

  // Mount the result-screen atmospheric scene as a sibling of the arena
  // background so it sits BEHIND the `relative z-10` stacking context that
  // hosts the AppHeader. With this placement the translucent header shows
  // the rays / vignette / sword stroke through it, the same way it shows
  // the arena background on lobby — no horizontal seam at the bottom of
  // the header band, and AppHeader stays unmodified. ResultBackground's
  // own layers use `position: fixed` so they span the full viewport
  // including behind BottomDock; the dock has its own opaque background,
  // so atmosphere visible through it stays consistent with the lobby
  // scene. ResultBackground itself returns null when the battle store has
  // no resolved end state, so the useMatch gate is purely a coupling-
  // locator: SessionShell only ever reaches for the result component on
  // the route that mounts BattleResultScreen.
  const onResultRoute = useMatch('/battle/:battleId/result') !== null;

  // Project the battle store's currentArena onto <html data-arena> so the
  // per-arena CSS overrides in ui/theme/arenas.css can retint the UI.
  // Gated by VITE_ENABLE_ARENA_THEMES so the feature can be cut over per
  // environment without a code change. The cleanup on unmount also clears
  // the attribute so logged-out shells (login, splash) never inherit a
  // stale arena from a previous session.
  const currentArena = useBattleStore((s) => s.currentArena);
  useEffect(() => {
    const flagEnabled = import.meta.env.VITE_ENABLE_ARENA_THEMES === 'true';
    if (!flagEnabled) return;

    if (currentArena) {
      document.documentElement.dataset.arena = currentArena;
    } else {
      delete document.documentElement.dataset.arena;
    }

    return () => {
      delete document.documentElement.dataset.arena;
    };
  }, [currentArena]);

  return (
    <div className="relative flex h-screen flex-col overflow-hidden bg-kombats-ink-navy text-text-primary">
      {/* Arena background — driven by the --arena-bg-image CSS variable.
          Per-arena overrides in ui/theme/arenas.css swap this when the
          data-arena attribute is set on <html>; the :root default in
          tokens.css points at full_moon.png (Palace Roof). Using a CSS
          variable on a div instead of an <img src> means the swap is
          purely a style update — no JS recompute needed when the arena
          changes. */}
      <div
        aria-hidden
        className="pointer-events-none absolute inset-0"
        style={{
          backgroundImage: 'var(--arena-bg-image)',
          backgroundSize: 'cover',
          backgroundPosition: 'center',
        }}
      />
      {/* Arena overlay — per-arena atmospheric gradient stacked above the
          background but below the foreground. Kept as a separate div from
          the background so future polish (overlay opacity animation, fade
          transitions) can target it independently. */}
      <div
        aria-hidden
        className="pointer-events-none absolute inset-0"
        style={{ background: 'var(--arena-overlay)' }}
      />

      {onResultRoute && <ResultBackground />}

      <div className="relative z-10 flex flex-1 min-h-0 flex-col overflow-hidden">
        <AppHeader />
        <main className="relative flex flex-1 min-h-0 flex-col overflow-hidden">
          <Outlet />
        </main>
      </div>
      <BottomDock />
    </div>
  );
}
