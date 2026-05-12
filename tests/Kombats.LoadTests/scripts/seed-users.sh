#!/usr/bin/env bash
# Convenience wrapper around tests/Kombats.LoadTests/SeedUsers.
#
# Run from the repo root:
#   ./tests/Kombats.LoadTests/scripts/seed-users.sh --count 50
#
# All flags are forwarded to the dotnet project. See:
#   dotnet run --project tests/Kombats.LoadTests/SeedUsers -- --help
#
# Why the kcadm prelude:
#   Keycloak's `master` realm defaults to `sslRequired=external`, which rejects
#   plain-HTTP admin auth from anything Keycloak doesn't see as 127.0.0.1.
#   When SeedUsers runs on the host and hits `http://localhost:8080`, the
#   container sees the request as coming from the Docker bridge gateway, which
#   trips the check. The simplest local-stack fix is to set master's
#   sslRequired to NONE — this is purely a Keycloak-admin convenience, services
#   keep validating tokens with the same defaults. Idempotent.

set -euo pipefail

cd "$(dirname "$0")/.."

KEYCLOAK_CONTAINER="${KEYCLOAK_CONTAINER:-kombats-keycloak}"

if docker ps --format '{{.Names}}' | grep -qx "$KEYCLOAK_CONTAINER"; then
    docker exec "$KEYCLOAK_CONTAINER" /opt/keycloak/bin/kcadm.sh config credentials \
        --server http://localhost:8080 --realm master --user admin --password admin >/dev/null 2>&1 || true
    docker exec "$KEYCLOAK_CONTAINER" /opt/keycloak/bin/kcadm.sh update realms/master \
        -s sslRequired=NONE >/dev/null 2>&1 || true
fi

exec dotnet run --project SeedUsers -- "$@"
