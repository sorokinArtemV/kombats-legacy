import { Navigate } from 'react-router';
import { useAuth } from '@/modules/auth/hooks';
import { useAuthStore } from '@/modules/auth/store';
import { retryBootstrap } from '@/modules/auth/bootstrap-retry';
import { SplashScreen } from '@/ui/components/SplashScreen';

// Ambient radial glow that sits behind the login panel. DESIGN_REFERENCE.md
// §3.3 — not expressible as a Tailwind utility.
const ambientGlowStyle = {
  background:
    'radial-gradient(circle, rgba(var(--rgb-gold-accent), 0.08) 0%, rgba(var(--rgb-gold-accent), 0.03) 40%, transparent 70%)',
};

// Display title bloom per DESIGN_REFERENCE.md §3.4 — gold halo text-shadow
// has no static Tailwind counterpart.
const titleBloomStyle = {
  textShadow: 'var(--shadow-title-neutral)',
};

export function UnauthenticatedShell() {
  const authStatus = useAuthStore((s) => s.authStatus);
  const authError = useAuthStore((s) => s.authError);
  const { login, register } = useAuth();

  // On initial app load we attempt a prompt=none SSO restore against Keycloak.
  // Until that bootstrap attempt resolves, authStatus stays 'loading'. Showing
  // the guest landing here would flash the login screen and then navigate
  // away — instead, hold the render until bootstrap succeeds or fails.
  if (authStatus === 'loading') {
    return <SplashScreen />;
  }

  // Silent restore succeeded (SSO cookie still valid on Keycloak): route the
  // user into the authenticated app. The downstream guards (GameStateLoader,
  // OnboardingGuard, BattleGuard) will redirect to the correct destination.
  if (authStatus === 'authenticated') {
    return <Navigate to="/lobby" replace />;
  }

  return (
    <div className="relative flex min-h-screen items-center justify-center overflow-hidden bg-kombats-ink-navy px-6 text-text-primary">
      <div
        aria-hidden
        className="pointer-events-none absolute left-1/2 top-1/2 h-[480px] w-[480px] -translate-x-1/2 -translate-y-1/2 rounded-full"
        style={ambientGlowStyle}
      />

      <div className="relative z-10 flex w-full max-w-[380px] flex-col items-center gap-7 rounded-[var(--radius-lg)] border-[0.5px] border-border-subtle bg-glass px-10 py-12 text-center shadow-[var(--shadow-panel-lift)] backdrop-blur-[20px]">
        <div aria-hidden className="kombats-diamond">
          <span className="kombats-diamond-glyph">拳</span>
        </div>

        <div className="flex flex-col items-center gap-2">
          <h1
            className="font-display text-[28px] font-bold uppercase leading-none tracking-[0.20em] text-accent-primary"
            style={titleBloomStyle}
          >
            The Kombats
          </h1>
          <p className="text-[12px] font-medium uppercase tracking-[0.24em] text-text-muted">
            Enter the Arena
          </p>
        </div>

        {authError === 'bootstrap_timeout' && (
          <div
            role="alert"
            className="flex w-full flex-col items-center gap-3 rounded-md border-[0.5px] border-kombats-crimson/60 bg-kombats-crimson/10 px-4 py-3 text-[12px] text-text-secondary"
          >
            <p>
              We couldn't restore your session. This usually means the sign-in service is
              unreachable.
            </p>
            <button
              type="button"
              onClick={retryBootstrap}
              className="inline-flex items-center justify-center rounded-md border-[0.5px] border-border-emphasis px-4 py-1.5 text-[11px] font-medium uppercase tracking-[0.18em] text-text-primary transition-colors duration-150 hover:border-accent-muted hover:text-accent-text"
            >
              Retry restore
            </button>
          </div>
        )}

        <div className="flex w-full flex-col gap-3">
          <button
            type="button"
            onClick={login}
            className="inline-flex w-full items-center justify-center rounded-md bg-accent-primary px-6 py-2.5 text-[13px] font-medium uppercase tracking-[0.18em] text-text-on-accent transition-colors duration-150 hover:bg-kombats-gold-light"
          >
            Log In
          </button>
          <button
            type="button"
            onClick={register}
            className="inline-flex w-full items-center justify-center rounded-md border-[0.5px] border-border-emphasis bg-transparent px-6 py-2.5 text-[13px] font-medium uppercase tracking-[0.18em] text-text-primary transition-colors duration-150 hover:border-accent-muted hover:text-accent-text"
          >
            Sign Up
          </button>
        </div>
      </div>
    </div>
  );
}
