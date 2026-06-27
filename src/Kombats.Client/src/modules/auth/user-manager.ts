import { UserManager, InMemoryWebStorage, WebStorageStateStore } from 'oidc-client-ts';
import { config } from '@/config';

// userStore/stateStore require the StateStore interface (set/get/remove/getAllKeys).
// InMemoryWebStorage implements the DOM Storage interface (setItem/getItem/removeItem),
// so it must be passed *inside* a WebStorageStateStore, never used directly as
// userStore. DEC-6 (tokens in memory, never localStorage) is preserved by backing
// the userStore with InMemoryWebStorage.
const inMemoryStorage = new InMemoryWebStorage();

export const userManager = new UserManager({
  authority: config().keycloak.authority,
  client_id: config().keycloak.clientId,
  redirect_uri: `${window.location.origin}/auth/callback`,
  // Dedicated silent-renew endpoint. main.tsx short-circuits on this path and
  // calls `signinSilentCallback()` instead of rendering the app. Used both for
  // token renewal AND for SSO session restore on page refresh — since tokens
  // live in memory (DEC-6), we recover the session via Keycloak's SSO cookie.
  silent_redirect_uri: `${window.location.origin}/silent-renew`,
  // Trailing slash is required. Keycloak's `post.logout.redirect.uris` for this
  // client is registered as `<origin>/*` — its wildcard matcher requires a path
  // separator after the host, so bare-origin (e.g. `http://localhost:5173`) is
  // rejected as an invalid redirect. `/` matches the wildcard and lands the
  // user on the UnauthenticatedShell route.
  post_logout_redirect_uri: `${window.location.origin}/`,
  response_type: 'code',
  scope: 'openid profile email',
  automaticSilentRenew: true,
  accessTokenExpiringNotificationTimeInSeconds: 60,
  // Hard cap on the iframe-based silent flow so a blocked iframe or
  // unreachable IdP cannot leave signinSilent() pending forever. The library
  // default is 10s; make it explicit to guarantee the promise settles and
  // the bootstrap finalizer always runs.
  silentRequestTimeoutInSeconds: 10,
  userStore: new WebStorageStateStore({ store: inMemoryStorage }),
  stateStore: new WebStorageStateStore({ store: window.sessionStorage }),
});

// ---------------------------------------------------------------------------
// Access-token cache
// ---------------------------------------------------------------------------
//
// The HTTP client and SignalR `accessTokenFactory` both need a SYNCHRONOUS
// read of the current access token (signalR's `accessTokenFactory: () =>
// string` is invoked synchronously on connect and on every reconnect). The
// authoritative User lives inside oidc-client-ts and `userManager.getUser()`
// is async, so we keep a one-way mirror of the token here and update it via
// UserManager events:
//
//   addUserLoaded    → set    (initial sign-in, signinSilent, automatic renew)
//   addUserUnloaded  → clear  (oidcAuth.removeUser() → e.g. logout teardown)
//   addUserSignedOut → clear  (back-channel / RP-initiated signout)
//
// The cache is one-way: nothing outside this module writes it. Consumers
// read via `getAccessToken()`.
let cachedAccessToken: string | null = null;

userManager.events.addUserLoaded((user) => {
  cachedAccessToken = user.access_token;
});

userManager.events.addUserUnloaded(() => {
  cachedAccessToken = null;
});

userManager.events.addUserSignedOut(() => {
  cachedAccessToken = null;
});

// One-shot priming. On a normal cold start the InMemoryWebStorage is empty
// and this resolves to null — `signinSilent()` from the AuthProvider's
// bootstrap effect will populate the cache via `addUserLoaded`. The priming
// matters for cases where a User already exists in the in-memory store at
// module-load time (HMR, StrictMode mount→unmount→mount) before any event
// has had a chance to fire. Conditional write avoids overwriting a fresher
// token if an event raced ahead of this resolve.
void userManager.getUser().then((user) => {
  if (user && !user.expired && cachedAccessToken === null) {
    cachedAccessToken = user.access_token;
  }
});

/**
 * Synchronous access to the current access token. Returns `null` when no
 * user is loaded. Reads from a module-level cache that mirrors the in-memory
 * User held by oidc-client-ts; updated via UserManager events above.
 *
 * Consumers: `transport/http/client.ts` (Authorization header) and the
 * `accessTokenFactory` injected into the SignalR hub managers. Both
 * surfaces require synchronous reads.
 */
export function getAccessToken(): string | null {
  return cachedAccessToken;
}
