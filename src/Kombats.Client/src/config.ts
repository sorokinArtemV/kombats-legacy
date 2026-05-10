export interface AppConfig {
  keycloak: {
    authority: string;
    clientId: string;
  };
  bff: {
    baseUrl: string;
  };
}

let cached: AppConfig | null = null;

/**
 * Loads /config.json from the hosting and caches it.
 * Must be called once before app mount.
 */
export async function loadConfig(): Promise<AppConfig> {
  if (cached) return cached;

  const res = await fetch('/config.json', { cache: 'no-store' });
  if (!res.ok) {
    throw new Error(`Failed to load /config.json: ${res.status}`);
  }
  const raw = await res.json();

  if (!raw.keycloakAuthority || !raw.keycloakClientId || !raw.bffBaseUrl) {
    throw new Error(
      `Invalid /config.json — missing required fields. Got: ${JSON.stringify(raw)}`,
    );
  }

  cached = {
    keycloak: {
      authority: String(raw.keycloakAuthority),
      clientId: String(raw.keycloakClientId),
    },
    bff: {
      baseUrl: String(raw.bffBaseUrl).replace(/\/+$/, ''),
    },
  };
  return cached;
}

/**
 * Synchronous accessor for already-loaded config.
 * Use only after loadConfig() has resolved (i.e. after app mount).
 */
export function config(): AppConfig {
  if (!cached) {
    throw new Error('config() called before loadConfig() resolved');
  }
  return cached;
}
