# Keycloak Themes

Custom Keycloak themes used by the Kombats realm. The active login theme is
`kombats`, which restyles Keycloak's classic login pages to match the
unauthenticated entry screen in the React client
(`src/Kombats.Client/src/app/shells/UnauthenticatedShell.tsx`).

## Structure

```
kombats/
  login/
    theme.properties              # parent=keycloak, adds css/kombats.css
    resources/css/kombats.css     # full restyle using client design tokens
```

The theme inherits the classic `keycloak` login theme's FreeMarker templates
and overrides presentation via CSS only. No templates are forked, so upstream
Keycloak updates can ship without merge work.

## Pages covered

All pages that the classic login theme renders inherit this styling:

- `login.ftl` — sign in
- `register.ftl` — registration (reached via `prompt=create` / `kc_action=register`)
- `error.ftl` — login / flow error page
- `info.ftl` — logout confirmation, post-action info, email sent notices
- `login-reset-password.ftl` — forgot password flow (enabled on the realm)
- `login-verify-email.ftl` — email verification (if enabled)
- `login-update-password.ftl`, `login-update-profile.ftl`, `terms.ftl`, etc.

## How it's wired

- `infra/keycloak/kombats-realm.json` sets `loginTheme: "kombats"` and a
  branded `displayName` / `displayNameHtml` that the theme's CSS targets.
- `docker-compose.yml` and `docker-compose.local.yml` mount this directory to
  `/opt/keycloak/themes` (read-only).

## Local preview

```bash
docker compose up keycloak
# or, for the IDE-run stack:
docker compose -f docker-compose.local.yml up keycloak
```

Then visit any of:

- http://localhost:8080/realms/kombats/protocol/openid-connect/auth?client_id=kombats-web&response_type=code&redirect_uri=http://localhost:5173/
- `npm run dev` in `src/Kombats.Client` and click **Login** / **Register** on
  the guest landing — both redirect into the themed pages.

Keycloak runs in `start-dev` mode, which disables theme caching, so edits to
`kombats.css` are picked up on the next page load (hard refresh to bypass the
browser cache).

## Production deployment

For a production image (`kc.sh start`), bake the theme into the image or
mount the same directory at `/opt/keycloak/themes` and run `kc.sh build`
during image build so Keycloak indexes the new theme.
