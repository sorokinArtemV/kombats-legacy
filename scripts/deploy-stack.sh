#!/usr/bin/env bash
# scripts/deploy-stack.sh
# One-shot deploy of the Kombats demo stack to Azure.
# From a clean state (no RG) to a working URL where you can log in
# and play. Requires: az CLI logged in, Node.js 18+, npm, swa CLI, jq.
#
# Steps:
#   1. Bicep deploy at subscription scope -> creates RG + everything inside
#   2. Build frontend with `npm run build`
#   3. Generate dist/config.json with real Keycloak/BFF URLs
#   4. Deploy SPA to SWA via swa CLI
#   5. Update BFF Cors__AllowedOrigins__0 with real SWA origin
#   6. Substitute __SWA_ORIGIN__ in realm.json and upload to File Share
#   7. Run migrator job
#   8. Restart Keycloak so it re-imports realm.json
#   9. Smoke tests
#
# After this script the stack is reachable at $swa_url. Log in as
# artem/111111 (or any other user from realm.json).
#
# Usage:
#   bash scripts/deploy-stack.sh
#   bash scripts/deploy-stack.sh rg-kombats-demo westeurope latest
#
# Runs on Linux, macOS, and Windows (Git Bash or WSL).

set -euo pipefail

resource_group="${1:-rg-kombats-demo}"
location="${2:-westeurope}"
image_tag="${3:-latest}"

# Resolve repo root (parent of scripts/).
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/.." && pwd)"

# Color helpers (ANSI). Plain text fallback if not a TTY.
if [[ -t 1 ]]; then
    cyan='\033[0;36m'
    green='\033[0;32m'
    nc='\033[0m'
else
    cyan='' green='' nc=''
fi

step() { echo -e "${cyan}=== $* ===${nc}"; }
done_msg() { echo -e "${green}=== $* ===${nc}"; }

# === 0. Sanity checks ===

step "Step 0: Sanity checks"

az account show --query name -o tsv

required_vars=(GHCR_USERNAME GHCR_TOKEN POSTGRES_PASSWORD KEYCLOAK_DB_PASSWORD KEYCLOAK_ADMIN_PASSWORD)
for v in "${required_vars[@]}"; do
    if [[ -z "${!v:-}" ]]; then
        echo "ERROR: Required env var not set: $v" >&2
        exit 1
    fi
done

# Check tooling presence.
for tool in az npm node jq; do
    if ! command -v "$tool" &> /dev/null; then
        echo "ERROR: $tool not found in PATH" >&2
        exit 1
    fi
done

# === 1. Bicep deploy (creates RG + everything) ===

step "Step 1: Bicep deploy (~10-15 min)"

export IMAGE_TAG="$image_tag"

deployment_json="$(az deployment sub create \
    --location "$location" \
    --template-file "$repo_root/infra/main.bicep" \
    --parameters "$repo_root/infra/main.bicepparam" \
    --name kombats-stack-deploy \
    -o json)"

provisioning_state="$(echo "$deployment_json" | jq -r '.properties.provisioningState')"
if [[ "$provisioning_state" != "Succeeded" ]]; then
    echo "ERROR: Bicep deploy failed: $provisioning_state" >&2
    exit 1
fi

bff_url="$(echo "$deployment_json" | jq -r '.properties.outputs.bffUrl.value')"
kc_url="$(echo "$deployment_json" | jq -r '.properties.outputs.keycloakUrl.value')"
swa_url="$(echo "$deployment_json" | jq -r '.properties.outputs.swaUrl.value')"
swa_name="$(echo "$deployment_json" | jq -r '.properties.outputs.swaName.value')"
sa_name="$(echo "$deployment_json" | jq -r '.properties.outputs.storageAccountName.value')"
share_name="$(echo "$deployment_json" | jq -r '.properties.outputs.shareName.value')"

# Strip trailing slashes so URL composition is deterministic.
bff_url="${bff_url%/}"
kc_url="${kc_url%/}"
swa_url="${swa_url%/}"

echo "  BFF:      $bff_url"
echo "  Keycloak: $kc_url"
echo "  SWA:      $swa_url"

# === 2. Build frontend with runtime config ===

step "Step 2: Build frontend"

client_dir="$repo_root/src/Kombats.Client"

(
    cd "$client_dir"
    npm install
    npm run build

    # Overwrite dist/config.json with real URLs (the public/config.json
    # baked into the bundle has localhost defaults for local dev).
    jq -n \
        --arg authority "$kc_url/realms/kombats" \
        --arg clientId  "kombats-web" \
        --arg bffBase   "$bff_url" \
        '{keycloakAuthority: $authority, keycloakClientId: $clientId, bffBaseUrl: $bffBase}' \
        > dist/config.json
)

# === 3. Deploy frontend to SWA ===

step "Step 3: Deploy SWA content"

# Retry SWA token fetch — the secrets endpoint can lag the deployment by
# 5-30 sec while internal SWA APIs warm up.
swa_token=""
for _ in 1 2 3 4 5 6; do
    swa_token="$(az staticwebapp secrets list \
        -n "$swa_name" -g "$resource_group" \
        --query properties.apiKey -o tsv 2>/dev/null || true)"
    [[ -n "$swa_token" ]] && break
    sleep 5
done

if [[ -z "$swa_token" ]]; then
    echo "ERROR: Failed to retrieve SWA deployment token after 30 sec" >&2
    exit 1
fi

# Ensure swa CLI is available (idempotent).
if ! npm list -g '@azure/static-web-apps-cli' &> /dev/null; then
    npm install -g '@azure/static-web-apps-cli'
fi

swa deploy "$client_dir/dist" \
    --deployment-token "$swa_token" \
    --env production

# === 4. Update BFF CORS with the real SWA origin ===

step "Step 4: Update BFF CORS"

az containerapp update \
    -n bff -g "$resource_group" \
    --set-env-vars "Cors__AllowedOrigins__0=$swa_url" \
    --output none

# === 5. Upload realm.json with substituted SWA origin ===

step "Step 5: Upload realm.json"

sa_key="$(az storage account keys list -n "$sa_name" -g "$resource_group" \
    --query '[0].value' -o tsv)"
if [[ -z "$sa_key" ]]; then
    echo "ERROR: Failed to retrieve storage account key" >&2
    exit 1
fi

# Substitute __SWA_ORIGIN__ in realm.json. Use a delimiter that cannot
# appear in an https URL (|) so sed escaping stays simple.
temp_realm="$(mktemp)"
trap 'rm -f "$temp_realm"' EXIT

sed "s|__SWA_ORIGIN__|$swa_url|g" "$repo_root/infra/keycloak/realm.json" > "$temp_realm"

az storage file upload \
    --account-name "$sa_name" \
    --account-key "$sa_key" \
    --share-name "$share_name" \
    --source "$temp_realm" \
    --path 'keycloak/realm.json' \
    --output none

# === 6. Run migrator job ===

step "Step 6: Run migrator"

az containerapp job start -n migrator -g "$resource_group" --output none

# Poll every 10 sec, timeout 5 min.
status="Running"
deadline=$(( $(date +%s) + 300 ))
while [[ "$status" == "Running" && $(date +%s) -lt $deadline ]]; do
    sleep 10
    status="$(az containerapp job execution list -n migrator -g "$resource_group" \
        --query '[0].properties.status' -o tsv)"
    echo "  migrator status: $status"
done

if [[ "$status" != "Succeeded" ]]; then
    echo "ERROR: Migrator did not succeed within timeout (status: $status)" >&2
    exit 1
fi

# === 7. Restart Keycloak (so it re-imports realm.json) ===

step "Step 7: Restart Keycloak"

rev="$(az containerapp show -n keycloak -g "$resource_group" \
    --query 'properties.latestRevisionName' -o tsv)"
if [[ -z "$rev" ]]; then
    echo "ERROR: Failed to read latest Keycloak revision name" >&2
    exit 1
fi

az containerapp revision restart -n keycloak -g "$resource_group" --revision "$rev" --output none

# === 8. Smoke tests ===

step "Step 8: Smoke tests"

# Wait a bit for Keycloak to come back.
sleep 30

bff_status="$(curl -s -o /dev/null -w '%{http_code}' "$bff_url/health" || echo "000")"
echo "  BFF /health: $bff_status"

kc_status="$(curl -s -o /dev/null -w '%{http_code}' "$kc_url/realms/kombats/.well-known/openid-configuration" || echo "000")"
echo "  Keycloak issuer: $kc_status"

swa_status="$(curl -s -o /dev/null -w '%{http_code}' "$swa_url" || echo "000")"
echo "  SWA index: $swa_status"

echo
done_msg "DONE"
echo "Open $swa_url in a browser, log in as artem / 111111"
