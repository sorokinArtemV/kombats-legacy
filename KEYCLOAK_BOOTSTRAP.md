# Keycloak local-dev bootstrap

Short note on how the Keycloak realm gets initialized in the local docker
stack, why a one-shot bootstrap container exists, and how to recover when
the Postgres volume backing Keycloak is wiped.

## How the `kombats` realm is initialized

1. The `keycloak` service in `docker-compose.yml` mounts `./infra/keycloak/`
   into `/opt/keycloak/data/import/` and starts with `start-dev --import-realm`.
2. On **first boot against a fresh `keycloak_db` volume**, Keycloak's
   liquibase migrations create the schema, then the bootstrap admin user
   (`admin` / `admin`, set via `KEYCLOAK_ADMIN[_PASSWORD]` — deprecated names,
   still work in 26.x), then imports `infra/keycloak/realm.json`. That JSON
   defines the `kombats` realm with `sslRequired: "none"` and three clients:
   `account`, `kombats-web`, `kombats-loadtest`.
3. On every subsequent boot the import strategy is `IGNORE_EXISTING` — the
   realm is kept as-is regardless of what `realm.json` says. To re-apply
   realm changes after the realm exists, either delete the `kombats` realm
   via the admin UI or wipe the volume (see below).

## Why `keycloak-bootstrap` exists

The seed-users tool (`tests/Kombats.LoadTests/SeedUsers/Program.cs`) talks
to the **admin REST API**, which lives under the `master` realm. The
`kombats` realm import only sets `sslRequired: "none"` for `kombats`; the
`master` realm keeps Keycloak's OOTB default of `external`.

When the seed tool calls `http://localhost:8080/realms/master/...` from the
host, Docker port-forwarding means Keycloak sees a non-loopback source IP
and rejects with:

```
HTTP 403 — {"error":"invalid_request","error_description":"HTTPS required"}
```

The `keycloak-bootstrap` service in `docker-compose.yml` runs once per
`docker compose up`, waits for Keycloak's admin endpoint to be reachable
(via `kcadm.sh` from inside the docker network where source IP is loopback),
then runs:

```
kcadm.sh update realms/master -s sslRequired=NONE
```

It's idempotent — every `compose up` re-applies it harmlessly. **Local
development only.** Production deploys should not set `sslRequired=NONE`
on `master`.

## Env vars that matter

| Var | Where set | Purpose |
|---|---|---|
| `KEYCLOAK_ADMIN` | `docker-compose.yml` keycloak service | Bootstrap admin username (`admin`). Deprecated alias for `KC_BOOTSTRAP_ADMIN_USERNAME`. |
| `KEYCLOAK_ADMIN_PASSWORD` | same | Bootstrap admin password (`admin`). |
| `KC_DB_*` | same | Connect Keycloak to its dedicated Postgres (`keycloak-db`). |
| `KC_HOSTNAME` / `KC_HTTP_ENABLED` / `KC_PROXY_HEADERS` | same | Allow plain-HTTP access from the host. |

The bootstrap container hard-codes `admin/admin` — if you change the
Keycloak admin credentials you must update `keycloak-bootstrap.command`
as well.

## Recovery: Postgres volume wiped (`docker compose down -v`)

A full `down -v` removes `keycloak_db`, so the realm and all users vanish.
To rebuild from scratch:

```sh
docker compose up -d                            # brings up keycloak + bootstrap
docker logs -f kombats-keycloak-bootstrap       # wait for "[keycloak-bootstrap] done."
cd tests/Kombats.LoadTests
dotnet run -- seed-users                        # recreates the 50 loadbot users + manifest
dotnet run -- single-bot                        # smoke-check auth + onboarding
dotnet run -- smoke                             # smoke-check one full battle
```

Notes:
- `seed-users` is idempotent (`existed=N` on re-run). It overwrites
  `tests/Kombats.LoadTests/users-manifest.json` with fresh `sub` UUIDs —
  the old manifest's `sub`s will no longer be valid after a volume wipe.
- `realm.json` does not embed Keycloak's RSA signing keys. New keys are
  generated on every fresh realm import, which means any cached JWTs from
  prior runs (including pre-existing in-flight battles) will fail
  validation. Restart all `src/Kombats.*` services as well if you want
  them to pick up the new JWKS (the bearer middleware will eventually
  refresh on signature failure, but a restart is faster).

## Recovery: bootstrap failed or `master` realm is stuck on HTTPS again

Re-run the bootstrap manually:

```sh
docker compose up -d --force-recreate keycloak-bootstrap
docker logs kombats-keycloak-bootstrap
```

Or run kcadm.sh directly:

```sh
docker exec -it kombats-keycloak /opt/keycloak/bin/kcadm.sh \
  config credentials --server http://localhost:8080 \
  --realm master --user admin --password admin
docker exec -it kombats-keycloak /opt/keycloak/bin/kcadm.sh \
  update realms/master -s sslRequired=NONE
```

Verify:

```sh
curl -sS -X POST http://localhost:8080/realms/master/protocol/openid-connect/token \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "grant_type=password&client_id=admin-cli&username=admin&password=admin" \
  | head -c 80
```

A 200 with `{"access_token":...}` means master is reachable; a 403 with
`"HTTPS required"` means the bootstrap didn't take.
