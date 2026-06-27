import { type ReactNode, useCallback, useEffect, useState } from 'react';
import { AuthProvider as OidcAuthProvider, useAuth as useOidcAuth } from 'react-oidc-context';
import type { User } from 'oidc-client-ts';
import { useAuthStore } from './store';
import { userManager } from './user-manager';
import {
  BOOTSTRAP_RETRY_EVENT,
  getBootstrapAttempt,
  getBootstrapPromise,
  setBootstrapPromise,
} from './bootstrap-retry';

// Hard upper bound on the bootstrap silent-restore attempt. Independent of
// oidc-client-ts's internal silentRequestTimeoutInSeconds so we always have a
// deterministic exit — even if the underlying promise is somehow pending.
const BOOTSTRAP_TIMEOUT_MS = 12_000;

// Bootstrap guard state (promise singleton + attempt counter) lives in
// `./bootstrap-retry.ts` so the retry entry point can coordinate without
// forcing `AuthProvider.tsx` to mix component + non-component exports.

function extractIdentity(user: User): {
  identityId: string;
  displayName: string;
} {
  return {
    identityId: user.profile.sub,
    displayName: (user.profile.preferred_username as string) ?? 'Unknown',
  };
}

function AuthSync({ children }: { children: ReactNode }) {
  const auth = useOidcAuth();
  const { setUser, clearAuth } = useAuthStore();

  // Tokens live in memory only (DEC-6), so a page refresh OR a browser restart
  // leaves us with no local user. On startup we attempt a prompt=none SSO
  // check against Keycloak to recover the session via its HTTP-only SSO
  // cookies. Until that attempt resolves we must NOT transition the store to
  // 'unauthenticated' — doing so bounces authenticated users back to the
  // guest landing on every reload. The /auth/callback route already has its
  // own redirect-based sign-in flow in flight, so we mark bootstrap complete
  // there (initialized true).
  const [bootstrapComplete, setBootstrapComplete] = useState<boolean>(
    () => window.location.pathname === '/auth/callback',
  );

  // Reset local bootstrap flag on retry request from the shell.
  useEffect(() => {
    const handler = () => setBootstrapComplete(false);
    window.addEventListener(BOOTSTRAP_RETRY_EVENT, handler);
    return () => window.removeEventListener(BOOTSTRAP_RETRY_EVENT, handler);
  }, []);

  const syncUser = useCallback(
    (user: User | null | undefined) => {
      if (user && !user.expired) {
        const { identityId, displayName } = extractIdentity(user);
        setUser(identityId, displayName);
      } else {
        clearAuth();
      }
    },
    [setUser, clearAuth],
  );

  // Bootstrap: try silent SSO restore exactly once per page load.
  //
  // Guaranteed-completion contract:
  //   1. Single run via module-level `bootstrapPromise` (StrictMode-safe).
  //   2. Explicit timeout (BOOTSTRAP_TIMEOUT_MS) wins if signinSilent never
  //      settles — no cancel flag gates the finalizer.
  //   3. `setBootstrapComplete(true)` ALWAYS runs when the race resolves.
  //   4. The main sync effect observes `bootstrapComplete === true` and drives
  //      authStatus to exactly one of 'authenticated' or 'unauthenticated'.
  //   5. If the external timeout wins, we stamp `authError: 'bootstrap_timeout'`
  //      on the auth store so the UnauthenticatedShell can surface a retry UI.

  useEffect(() => {
    if (bootstrapComplete) return;
    if (auth.isLoading) return;

    let bootstrapPromise = getBootstrapPromise();

    if (!bootstrapPromise) {
      const attempt = getBootstrapAttempt();

      if (auth.isAuthenticated && auth.user) {
        // Already authenticated on mount (e.g., HMR kept oidc state alive).
        // No silent restore needed — but we still flip bootstrapComplete so
        // the main sync effect's post-bootstrap branch is unblocked for
        // future state transitions.
        bootstrapPromise = Promise.resolve();
      } else {
        let timedOut = false;

        const silent = auth.signinSilent().catch(() => {
          // login_required (no SSO cookie / session expired) is the expected
          // failure mode — main sync effect will transition to unauthenticated.
          // Network / iframe-timeout errors land here too.
        });

        const timeout = new Promise<void>((resolve) => {
          setTimeout(() => {
            timedOut = true;
            resolve();
          }, BOOTSTRAP_TIMEOUT_MS);
        });

        bootstrapPromise = Promise.race([silent, timeout]).then(() => {
          // Stamp the error only if the timeout actually won AND this is
          // still the current attempt (a retry would have bumped the counter).
          if (timedOut && getBootstrapAttempt() === attempt) {
            useAuthStore.getState().setAuthError('bootstrap_timeout');
          }
        });
      }

      setBootstrapPromise(bootstrapPromise);
    }

    // `.finally` callback is async (microtask or later), so this never
    // triggers a synchronous cascading render from the effect body.
    bootstrapPromise.finally(() => {
      setBootstrapComplete(true);
    });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [bootstrapComplete, auth.isLoading, auth.isAuthenticated, auth.user, auth.signinSilent]);

  // Drive authStatus transitions. Runs whenever auth state or bootstrap
  // progresses. Pre-bootstrap we stay on 'loading'. Post-bootstrap we
  // unconditionally resolve to 'authenticated' (if we have a user) or
  // 'unauthenticated' (otherwise) — we deliberately do NOT gate on
  // activeNavigator here, because a silent navigator that never settles
  // would otherwise trap us in 'loading' forever.
  useEffect(() => {
    if (auth.isLoading) return;

    if (auth.isAuthenticated && auth.user && !auth.user.expired) {
      syncUser(auth.user);
      return;
    }

    if (bootstrapComplete) {
      // Bootstrap finished (success, rejection, or external timeout) and we
      // do not have a usable user — resolve to guest.
      clearAuth();
    }
  }, [
    auth.isLoading,
    auth.isAuthenticated,
    auth.user,
    auth.activeNavigator,
    auth.error,
    bootstrapComplete,
    syncUser,
    clearAuth,
  ]);

  // Note: keeping the in-memory access token fresh across silent renewals is
  // handled by the cache subscription in `./user-manager.ts` (it listens to
  // the same `addUserLoaded` event). No mirror write is needed here.

  useEffect(() => {
    const handleSilentRenewError = () => {
      clearAuth();
    };

    userManager.events.addSilentRenewError(handleSilentRenewError);
    return () => {
      userManager.events.removeSilentRenewError(handleSilentRenewError);
    };
  }, [clearAuth]);

  return <>{children}</>;
}

export function AuthProvider({ children }: { children: ReactNode }) {
  return (
    <OidcAuthProvider userManager={userManager}>
      <AuthSync>{children}</AuthSync>
    </OidcAuthProvider>
  );
}
