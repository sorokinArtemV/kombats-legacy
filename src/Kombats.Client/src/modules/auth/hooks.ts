import { useCallback } from 'react';
import { useAuth as useOidcAuth } from 'react-oidc-context';
import { useAuthStore } from './store';
import { clearSessionState } from '@/app/session-cleanup';
import { logger } from '@/app/logger';

export function useAuth() {
  const oidcAuth = useOidcAuth();
  const { authStatus, userIdentityId, displayName } = useAuthStore();

  const login = useCallback(() => {
    oidcAuth.signinRedirect();
  }, [oidcAuth]);

  const register = useCallback(() => {
    // Keycloak has two parameters that trigger the registration form on
    // the authorization endpoint:
    //   - `kc_action=register`  — introduced in Keycloak 24; semantics
    //                             were narrowed in later releases so it
    //                             is not reliable for anonymous users on
    //                             every version.
    //   - `prompt=create`       — the OIDC-standard draft parameter,
    //                             supported by Keycloak 21+ for the
    //                             anonymous-registration case.
    // Sending both lets whichever parameter this realm's Keycloak
    // version honors take effect; the other is ignored. This is the
    // most forward- and backward-compatible shape short of constructing
    // the URL manually and hitting the `/registrations` endpoint.
    oidcAuth.signinRedirect({
      extraQueryParams: { kc_action: 'register', prompt: 'create' },
    });
  }, [oidcAuth]);

  const logout = useCallback(async () => {
    // Order matters.
    //
    // 1. Capture `id_token_hint` from the current oidc user BEFORE we touch
    //    anything. Keycloak's end-session endpoint only skips its interactive
    //    "Logout confirmation" page and honors `post_logout_redirect_uri`
    //    silently when it can authenticate the logout request — which means
    //    `id_token_hint` must be on the logout URL. Without it, Keycloak
    //    lands the user on its own hosted confirmation / warning page
    //    instead of the SPA's unauthenticated home, which the user sees as
    //    a redirect error. oidc-client-ts *would* read the id_token off the
    //    current user inside `signoutRedirect()`, but step 3 below removes
    //    that user first, so we have to capture it now and pass it in
    //    explicitly.
    const idTokenHint = oidcAuth.user?.id_token;

    // 2. Tear down every session-scoped artifact (SignalR connections,
    //    module stores, TanStack Query cache). A clean slate on user
    //    switch / re-login — no previous session's game state, chat
    //    history, or battle store leaks through.
    //
    //    Runs BEFORE removeUser() so the access-token cache held by
    //    user-manager.ts is still populated when clearSessionState's
    //    `queueApi.leave()` HTTP call fires — without this ordering the
    //    leave call gets a 401 because removeUser() synchronously fires
    //    `addUserUnloaded`, which clears the cache (the heartbeat sweep
    //    is the safety net but the 401 is avoidable). The transient
    //    "un-logging-out" race the previous ordering defended against is
    //    now harmless: the access-token mirror is no longer kept on
    //    useAuthStore (lives only in user-manager.ts and is cleared by
    //    removeUser), so any intermediate AuthSync render before
    //    removeUser settles can only repopulate userIdentityId /
    //    displayName, which are re-cleared the moment auth.user flips
    //    to null.
    await clearSessionState();

    // 3. Remove the oidc-client-ts user. Fires addUserUnloaded which
    //    clears the user-manager.ts access-token cache and propagates
    //    auth.isAuthenticated -> false through react-oidc-context,
    //    triggering AuthSync to re-clear the auth store.
    try {
      await oidcAuth.removeUser();
    } catch (err) {
      // removeUser is local; failures here are surprising but not fatal.
      logger.warn('oidcAuth.removeUser() failed during logout', err);
    }

    // 4. Hit Keycloak's end-session endpoint and redirect. Passing the
    //    captured `id_token_hint` lets Keycloak authenticate the logout,
    //    validate `post_logout_redirect_uri` against the client, kill the
    //    SSO session, and redirect back silently — no confirmation page.
    //    If this fails (unreachable IdP, CORS, etc.) fall back to a hard
    //    nav to the origin so the user still lands on a guest page.
    try {
      await oidcAuth.signoutRedirect({ id_token_hint: idTokenHint });
    } catch (err) {
      logger.error('oidcAuth.signoutRedirect() failed during logout', err);
      window.location.assign('/');
    }
  }, [oidcAuth]);

  return {
    authStatus,
    userIdentityId,
    displayName,
    isAuthenticated: authStatus === 'authenticated',
    isLoading: oidcAuth.isLoading || authStatus === 'loading',
    login,
    register,
    logout,
  };
}
