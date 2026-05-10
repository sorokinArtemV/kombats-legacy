/// <reference types="vite/client" />

// URLs (Keycloak authority, BFF base URL, OIDC client ID) come from
// /config.json at runtime — see src/config.ts. They intentionally are
// NOT exposed via import.meta.env so a typo here can't silently bypass
// the runtime config loader.
interface ImportMetaEnv {
  readonly VITE_ENABLE_ARENA_THEMES?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
