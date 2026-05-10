# Kombats - Turn-based browser fighting game with East Asian aesthetic

keycloak setup: add 127.0.0.1 keycloak to /etc/hosts
echo "127.0.0.1 keycloak" | sudo tee -a /etc/hosts

$env:GHCR_USERNAME = 'sorokinartemv'
$env:GHCR_TOKEN = ''
$env:POSTGRES_PASSWORD = 'StrongPwd1!'
$env:KEYCLOAK_DB_PASSWORD = 'StrongPwd2!'
$env:KEYCLOAK_ADMIN_PASSWORD = 'AdminPwd3!'

az deployment group what-if `
  --resource-group kombats-rg `
--template-file infra/main.bicep `
--parameters infra/main.bicepparam

---

# Подписка верная
az account show --query name -o tsv

# RG жив
az group show -n rg-kombats-demo --query "{name:name, location:location}" -o table

# Все 5 секретов выставлены (показывает только имена, не значения)
Get-ChildItem env: | Where-Object Name -in @('GHCR_USERNAME','GHCR_TOKEN','POSTGRES_PASSWORD','KEYCLOAK_DB_PASSWORD','KEYCLOAK_ADMIN_PASSWORD') | Select-Object Name

# Образы в GHCR доступны
docker pull ghcr.io/sorokinartemv/kombats-bff:latest
docker pull ghcr.io/sorokinartemv/kombats-migrator:latest
