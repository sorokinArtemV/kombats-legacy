#!/usr/bin/env bash
# run-migrations.sh — CI/CD migration runner for Kombats services
#
# Applies EF Core migrations for all Kombats services against the target database.
# Run this as a CI/CD step or init container BEFORE deploying service containers.
# Per AD-13: migrations must NOT run on application startup.
#
# Usage:
#   ./scripts/run-migrations.sh [CONNECTION_STRING]
#
# If CONNECTION_STRING is not provided, defaults to the development connection string.
# In CI/CD, pass the production connection string as an argument or via
# the KOMBATS_POSTGRES_CONNECTION environment variable.

set -euo pipefail

CONNECTION_STRING="${1:-${KOMBATS_POSTGRES_CONNECTION:-Host=localhost;Port=5432;Database=kombats;Username=postgres;Password=postgres}}"

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"

echo "=== Kombats Migration Runner ==="
echo "Repo root: $REPO_ROOT"
echo ""

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
