import { describe, it, expect, vi, beforeAll, afterAll } from 'vitest';

vi.mock('@/config', () => ({
  config: {
    keycloak: {
      authority: 'http://localhost:8080/realms/kombats',
      clientId: 'kombats-web',
    },
    bff: { baseUrl: 'http://localhost:5000' },
  },
}));

const TEST_ORIGIN = 'http://localhost:5173';

function createMemoryStorage(): Storage {
  const map = new Map<string, string>();
  return {
    get length() {
      return map.size;
    },
    clear: () => map.clear(),
    getItem: (k: string) => (map.has(k) ? map.get(k)! : null),
    key: (i: number) => Array.from(map.keys())[i] ?? null,
    removeItem: (k: string) => void map.delete(k),
    setItem: (k: string, v: string) => void map.set(k, String(v)),
  };
}

beforeAll(() => {
  // user-manager reads window.location.origin at module-load time AND passes
  // window.sessionStorage into a WebStorageStateStore (which, if the store is
  // undefined, falls back to a global `localStorage` reference that blows up
  // under the default node test env). Stub just enough of the DOM to let the
  // module construct — no jsdom, matching the project's testing convention.
  const sessionStorage = createMemoryStorage();
  vi.stubGlobal('window', { location: { origin: TEST_ORIGIN }, sessionStorage });
});

afterAll(() => {
  vi.unstubAllGlobals();
});

describe('userManager settings', () => {
  it('builds the login redirect_uri with the /auth/callback path', async () => {
    const { userManager } = await import('./user-manager');
    expect(userManager.settings.redirect_uri).toBe(`${TEST_ORIGIN}/auth/callback`);
  });

  it('builds the silent-renew redirect_uri with the /silent-renew path', async () => {
    const { userManager } = await import('./user-manager');
    expect(userManager.settings.silent_redirect_uri).toBe(`${TEST_ORIGIN}/silent-renew`);
  });

  // Keycloak registers `post.logout.redirect.uris` as `<origin>/*` for the
  // kombats-web client. The wildcard matcher requires a path separator after
  // the host, so a bare origin (no trailing slash) is rejected as an invalid
  // redirect URI and the user lands on Keycloak's error page instead of the
  // SPA. Lock the trailing slash in so this regression cannot silently return.
  it('builds the post_logout_redirect_uri with a trailing slash to match Keycloak `<origin>/*`', async () => {
    const { userManager } = await import('./user-manager');
    const value = userManager.settings.post_logout_redirect_uri;
    expect(value).toBe(`${TEST_ORIGIN}/`);
    expect(value?.endsWith('/')).toBe(true);
  });
});

// These tests lock the oidc-client-ts contract that `hooks.ts#logout` relies
// on when it captures `id_token_hint` BEFORE `removeUser()` and passes it into
// `signoutRedirect({ id_token_hint })`. Without `id_token_hint`, Keycloak 19+
// cannot authenticate the end-session request and lands the user on its own
// hosted "Logout confirmation" page instead of redirecting back silently —
// the user-visible symptom of the original bug.
describe('signout URL construction (Keycloak end-session contract)', () => {
  async function buildSignoutUrl(extra: { id_token_hint?: string }) {
    const { userManager } = await import('./user-manager');
    // Stub metadata lookup so the test does not hit the network.
    const client = (
      userManager as unknown as {
        _client: {
          metadataService: { getEndSessionEndpoint: () => Promise<string> };
          createSignoutRequest: (args: {
            id_token_hint?: string;
            post_logout_redirect_uri?: string;
            request_type?: string;
          }) => Promise<{ url: string }>;
        };
      }
    )._client;
    client.metadataService.getEndSessionEndpoint = async () =>
      'http://localhost:8080/realms/kombats/protocol/openid-connect/logout';
    const req = await client.createSignoutRequest({
      post_logout_redirect_uri: `${TEST_ORIGIN}/`,
      request_type: 'so:r',
      ...extra,
    });
    return new URL(req.url);
  }

  it('includes id_token_hint on the logout URL when hooks.ts passes one in', async () => {
    const url = await buildSignoutUrl({ id_token_hint: 'FAKE_ID_TOKEN' });
    expect(url.searchParams.get('id_token_hint')).toBe('FAKE_ID_TOKEN');
    expect(url.searchParams.get('post_logout_redirect_uri')).toBe(`${TEST_ORIGIN}/`);
    // When id_token_hint is present, oidc-client-ts MUST NOT also set client_id
    // — Keycloak uses id_token_hint alone to authenticate and skip the
    // confirmation page.
    expect(url.searchParams.get('client_id')).toBeNull();
  });

  // Negative case: documents the exact failure mode the fix defends against.
  // If id_token_hint is omitted (regression shape: logout() clears the oidc
  // user before capturing its id_token), oidc-client-ts falls back to the
  // anonymous `client_id` form — which Keycloak handles by showing its hosted
  // "Logout confirmation" screen instead of redirecting silently.
  it('falls back to client_id and omits id_token_hint when none is passed (regression guard)', async () => {
    const url = await buildSignoutUrl({});
    expect(url.searchParams.get('id_token_hint')).toBeNull();
    expect(url.searchParams.get('client_id')).toBe('kombats-web');
  });
});
