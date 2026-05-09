#!/usr/bin/env bash
# scripts/run-migrations.sh — CI/CD migration runner for Kombats services
#
# Designed to run as a Container Apps Job entrypoint.
# Reads Postgres connection details from environment variables, waits for
# Postgres to be ready, creates the Keycloak database and user, then runs
# EF Core migrations for all backend services that have a database.
#
# Required env vars:
#   POSTGRES_HOST          — internal hostname, e.g. postgres.internal.<env-domain>
#   POSTGRES_PORT          — typically 5432
#   POSTGRES_DB            — main DB for backend services, e.g. "kombats"
#   POSTGRES_USER          — superuser, e.g. "kombats"
#   POSTGRES_PASSWORD      — password for POSTGRES_USER
#   KEYCLOAK_DB_PASSWORD   — password to assign to the new "keycloak" DB user

set -euo pipefail

: "${POSTGRES_HOST:?POSTGRES_HOST is required}"
: "${POSTGRES_PORT:?POSTGRES_PORT is required}"
: "${POSTGRES_DB:?POSTGRES_DB is required}"
: "${POSTGRES_USER:?POSTGRES_USER is required}"
: "${POSTGRES_PASSWORD:?POSTGRES_PASSWORD is required}"
: "${KEYCLOAK_DB_PASSWORD:?KEYCLOAK_DB_PASSWORD is required}"

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"

CONNECTION_STRING="Host=${POSTGRES_HOST};Port=${POSTGRES_PORT};Database=${POSTGRES_DB};Username=${POSTGRES_USER};Password=${POSTGRES_PASSWORD}"

export PGPASSWORD="$POSTGRES_PASSWORD"

echo "=== Kombats Migration Runner ==="
echo "Postgres: ${POSTGRES_HOST}:${POSTGRES_PORT}, DB: ${POSTGRES_DB}, User: ${POSTGRES_USER}"
echo ""

# ----- 1. Wait for Postgres to be ready -----

echo "--- Waiting for Postgres ---"
for i in $(seq 1 60); do
    if pg_isready -h "$POSTGRES_HOST" -p "$POSTGRES_PORT" -U "$POSTGRES_USER" -d postgres -q; then
        echo "Postgres is ready."
        break
    fi
    if [ "$i" -eq 60 ]; then
        echo "ERROR: Postgres did not become ready in 60 seconds."
        exit 1
    fi
    echo "  attempt $i: not ready, retrying in 2s..."
    sleep 2
done
echo ""

# ----- 2. Bootstrap Keycloak database and user -----

echo "--- Bootstrapping Keycloak DB and user ---"

# Create database (idempotent — skip if exists)
DB_EXISTS=$(psql -h "$POSTGRES_HOST" -p "$POSTGRES_PORT" -U "$POSTGRES_USER" -d postgres -tAc \
    "SELECT 1 FROM pg_database WHERE datname='keycloak'")
if [ "$DB_EXISTS" = "1" ]; then
    echo "Database 'keycloak' already exists — skipping CREATE."
else
    psql -h "$POSTGRES_HOST" -p "$POSTGRES_PORT" -U "$POSTGRES_USER" -d postgres -c \
        "CREATE DATABASE keycloak"
    echo "Database 'keycloak' created."
fi

# Create user (idempotent)
USER_EXISTS=$(psql -h "$POSTGRES_HOST" -p "$POSTGRES_PORT" -U "$POSTGRES_USER" -d postgres -tAc \
    "SELECT 1 FROM pg_roles WHERE rolname='keycloak'")
if [ "$USER_EXISTS" = "1" ]; then
    echo "User 'keycloak' already exists — updating password."
    psql -h "$POSTGRES_HOST" -p "$POSTGRES_PORT" -U "$POSTGRES_USER" -d postgres -c \
        "ALTER USER keycloak WITH PASSWORD '${KEYCLOAK_DB_PASSWORD}'"
else
    psql -h "$POSTGRES_HOST" -p "$POSTGRES_PORT" -U "$POSTGRES_USER" -d postgres -c \
        "CREATE USER keycloak WITH PASSWORD '${KEYCLOAK_DB_PASSWORD}'"
    echo "User 'keycloak' created."
fi

# Grant privileges (idempotent)
psql -h "$POSTGRES_HOST" -p "$POSTGRES_PORT" -U "$POSTGRES_USER" -d postgres -c \
    "GRANT ALL PRIVILEGES ON DATABASE keycloak TO keycloak"
psql -h "$POSTGRES_HOST" -p "$POSTGRES_PORT" -U "$POSTGRES_USER" -d keycloak -c \
    "GRANT ALL ON SCHEMA public TO keycloak"

echo "Keycloak bootstrap complete."
echo ""

# ----- 3. Apply EF Core migrations -----

apply_migrations() {
    local service_name="$1"
    local bootstrap_project="$2"
    local infrastructure_project="$3"

    echo "--- Applying migrations for $service_name ---"
    dotnet ef database update \
        --startup-project "$REPO_ROOT/$bootstrap_project" \
        --project "$REPO_ROOT/$infrastructure_project" \
        --connection "$CONNECTION_STRING" \
        --verbose
    echo "--- $service_name migrations complete ---"
    echo ""
}

apply_migrations "Players" \
    "src/Kombats.Players/Kombats.Players.Bootstrap" \
    "src/Kombats.Players/Kombats.Players.Infrastructure"

apply_migrations "Matchmaking" \
    "src/Kombats.Matchmaking/Kombats.Matchmaking.Bootstrap" \
    "src/Kombats.Matchmaking/Kombats.Matchmaking.Infrastructure"

apply_migrations "Battle" \
    "src/Kombats.Battle/Kombats.Battle.Bootstrap" \
    "src/Kombats.Battle/Kombats.Battle.Infrastructure"

apply_migrations "Chat" \
    "src/Kombats.Chat/Kombats.Chat.Bootstrap" \
    "src/Kombats.Chat/Kombats.Chat.Infrastructure"

echo "=== All migrations applied successfully ==="
