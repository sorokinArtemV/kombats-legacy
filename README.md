# Kombats - Turn-based browser fighting game with East Asian aesthetic

keycloak setup: add 127.0.0.1 keycloak to /etc/hosts
echo "127.0.0.1 keycloak" | sudo tee -a /etc/hosts

$env:GHCR_TOKEN = "ghp_token"
$env:POSTGRES_PASSWORD = "TempPass123!"
$env:KEYCLOAK_DB_PASSWORD = "TempPass456!"
$env:KEYCLOAK_ADMIN_PASSWORD = "TempPass789!"

az deployment group what-if `
  --resource-group kombats-rg `
--template-file infra/main.bicep `
--parameters infra/main.bicepparam
