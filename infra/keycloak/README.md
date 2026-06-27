# `infra/keycloak/` — realm definition for the Kombats local stack

This folder ships the Keycloak realm imported by the `keycloak` container in
the root `docker-compose.yml`. The image is built from the `Dockerfile`
here; the realm is imported via `--import-realm` from `realm.json` on first
boot (`docker-compose.yml:52, 68` — `./infra/keycloak:/opt/keycloak/data/import`).

There is a single realm: **`kombats`** (`realm.json:2`).

## Clients in this realm

| `clientId` | Public? | Flows enabled | Purpose |
|---|---|---|---|
| `account` | yes | Standard flow | Keycloak-builtin self-service account UI. Not used by Kombats code. (`realm.json:25-67`) |
| `kombats-web` | yes (PKCE) | Standard flow | Public OIDC client for the React SPA at `src/Kombats.Client/`. Audience mapper adds `kombats-api`. Direct access grants disabled. (`realm.json:69-127`) |
| `kombats-loadtest` | **no — confidential** | **Direct access grants (ROPC) only** | Used by `tests/Kombats.LoadTests/` to mint per-bot JWTs via the OAuth password grant. Local-only artefact. See "kombats-loadtest" section below. |

See also: [`tests/Kombats.LoadTests/README.md`](../../tests/Kombats.LoadTests/README.md).

## `kombats-loadtest` — rationale

### Why this client exists

The load-test harness (`tests/Kombats.LoadTests/`) needs to obtain a real
Keycloak-issued JWT for each of N virtual players. The two pre-existing
clients can't be used:

- `kombats-web` has `directAccessGrantsEnabled: false` (`realm.json:90`) — the
  ROPC token endpoint rejects requests with `client_id=kombats-web` as
  `unauthorized_client`. Enabling ROPC on the SPA client would weaken the
  production auth surface, so we don't.
- `account` is Keycloak's internal account UI client — not appropriate.

We add a separate client whose **only** purpose is to mint load-test tokens.

### Why we don't use a separate realm

Considered: ship a `realm-loadtest.json` alongside `realm.json` so the
load-test client lives in its own realm. **Doesn't work without service code
changes.** Services validate JWTs against `Keycloak:Authority` which points
to `realms/kombats` only
(`src/Kombats.Common/Kombats.Abstractions/Auth/KombatsAuthExtensions.cs:24`,
`src/Kombats.Bff/Kombats.Bff.Bootstrap/Program.cs:35`). A token issued by
any other realm fails both the `iss`-claim check and the JWKS signature
check by default. Multi-issuer support would require patching
`KombatsAuthExtensions.cs` to extend `TokenValidationParameters.ValidIssuers`
and add a second JWKS source, which we explicitly do not want.

So the load-test client lives in the existing `kombats` realm. Same issuer,
same JWKS, same audience — the services don't need to know it exists.

### Client config (in `realm.json`)

- `clientId: "kombats-loadtest"`
- `publicClient: false`, `clientAuthenticatorType: "client-secret"`
- `secret: "loadtest-secret-do-not-use-in-prod"` — checked into git on
  purpose. This realm is a local-stack-only artefact (the Azure deploy
  uploads its own realm.json via `pipelines/deploy-stack.yml`); leaking a
  fixed secret in a local config file has no security impact.
- `directAccessGrantsEnabled: true` — only flow we use.
- `standardFlowEnabled: false`, `implicitFlowEnabled: false`,
  `serviceAccountsEnabled: false` — every other flow is explicitly off.
- `attributes."access.token.lifespan": "7200"` — 2 hour token, long enough
  to cover the longest load-test run without refresh.
- `protocolMappers`: one entry, the `kombats-api-audience` mapper, identical
  to the one on `kombats-web` (`realm.json:101-111`). Without this, the
  issued token's `aud` claim won't contain `kombats-api` and every service
  rejects the request with 401.

### Token shape after ROPC

```http
POST /realms/kombats/protocol/openid-connect/token
Content-Type: application/x-www-form-urlencoded

grant_type=password
&client_id=kombats-loadtest
&client_secret=loadtest-secret-do-not-use-in-prod
&username=loadbot-0001
&password=loadtest
```

The returned access token has:
- `iss = "http://localhost:8080/realms/kombats"`
- `aud` includes `"kombats-api"`
- `sub` = the Keycloak-assigned UUID for `loadbot-0001` (stable across runs)
- `preferred_username = "loadbot-0001"`
- `exp = iat + 7200`

All four standard checks (`ValidateIssuer`, `ValidateAudience`,
`ValidateIssuerSigningKey`, `ValidateLifetime`) pass without service-side
changes.

### Removing for production

If a deploy must not ship this client:

1. Take a copy of `realm.json` for the production import.
2. Delete the entire `kombats-loadtest` client block (the entry in the
   `clients` array with `"clientId": "kombats-loadtest"`).
3. Use the trimmed file in the production realm import path.

The block is contiguous and self-contained — no other client or mapper
references it — so deletion is a clean cut. The Azure pipeline already
uploads its own realm.json via `pipelines/deploy-stack.yml`; perform the
deletion in the file that pipeline uploads.

## Files in this folder

- `realm.json` — the realm definition imported on Keycloak startup.
- `Dockerfile` — custom Keycloak 24 image baking the `kombats` FreeMarker
  theme from `infra/keycloak-themes/`.
- `README.md` — this file.
