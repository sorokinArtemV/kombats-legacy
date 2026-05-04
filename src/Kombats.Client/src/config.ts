interface AppConfig {
  keycloak: {
    authority: string;
    clientId: string;
  };
  bff: {
    baseUrl: string;
  };
}

function requireEnv(key: string): string {
  const value = import.meta.env[key];
  if (!value) {
    throw new Error(`Missing required environment variable: ${key}`);
  }
  return value;
}

export const config: AppConfig = {
  keycloak: {
    authority: requireEnv('VITE_KEYCLOAK_AUTHORITY'),
    clientId: requireEnv('VITE_KEYCLOAK_CLIENT_ID'),
  },
  bff: {
    baseUrl: requireEnv('VITE_BFF_BASE_URL').replace(/\/+$/, ''),
  },
};
