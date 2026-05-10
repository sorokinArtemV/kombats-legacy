// main.bicepparam
// Parameter values for main.bicep (subscription-scope orchestrator).
// Non-secret values are inline; secrets come from environment variables
// via readEnvironmentVariable, so this file can be safely committed to git.

using './main.bicep'

// === NON-SECRET PARAMETERS ===

param resourceGroupName = 'rg-kombats-demo'
param location = 'westeurope'
param ghcrUsername = 'sorokinartemv'

// === IMAGE TAGS ===

// Defaults to 'latest' for local deploys. CD overrides via env to Build.BuildId.
param imageTag = readEnvironmentVariable('IMAGE_TAG', 'latest')

// Migrator image — separate from backend services, built from src/Kombats.Migrator/Dockerfile.
param migratorImage = readEnvironmentVariable(
  'MIGRATOR_IMAGE',
  'ghcr.io/sorokinartemv/kombats-migrator:latest'
)

// === SECRETS ===

// All secrets come from env vars. No defaults — deploy will fail if any are missing.
// Locally: export the variable before running `az deployment`.
// In CD: pipeline passes them via env from a Variable Group.

param ghcrToken = readEnvironmentVariable('GHCR_TOKEN')
param postgresPassword = readEnvironmentVariable('POSTGRES_PASSWORD')
param keycloakDbPassword = readEnvironmentVariable('KEYCLOAK_DB_PASSWORD')
param keycloakAdminPassword = readEnvironmentVariable('KEYCLOAK_ADMIN_PASSWORD')
