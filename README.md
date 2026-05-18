# Kombats - Turn-based browser fighting game with East Asian aesthetic

## Running the stack

The project supports three operational modes. Use the right compose chain for your goal — wrong chains have caused silent observability failures (Chapter 3 D6).

### Mode A — Full stack (default for measurement runs, Chapters 0–3, Chapter 2.5 Sustainable Run)

All 5 backend services + supporting infrastructure + observability stack. This is the canonical "everything works" chain.

```bash
docker compose \
  -f docker-compose.yml \
  -f observability/docker-compose.observability.yml \
  -f observability/docker-compose.observability.override.yml \
  -f docker-compose.override.yml \
  up -d --build
```

Required for: load-test measurement, anything where Grafana/Jaeger/Prometheus telemetry must work.

### Mode B — Multi-replica (Chapter 3 Phase II, Chapter 4 capacity test)

Mode A plus a second Battle replica (`battle-2`). Use when testing SignalR backplane behavior or sustained capacity.

```bash
docker compose \
  -f docker-compose.yml \
  -f observability/docker-compose.observability.yml \
  -f observability/docker-compose.observability.override.yml \
  -f docker-compose.override.yml \
  -f docker-compose.multi-replica.yml \
  up -d --build
```

### Mode C — IDE mode (host-side service development)

Only the supporting infrastructure (Postgres, Redis, RabbitMQ, Keycloak). The .NET services run on the host from your IDE or `dotnet run`. Distinct compose project name (`kombats-local`) so it does not clash with Mode A.

```bash
docker compose -f docker-compose.local.yml up -d
```

For observability in IDE mode, additionally start the standalone observability stack — services read `OtlpEndpoint=http://localhost:4317` from `appsettings.Development.json`:

```bash
docker compose -f observability/docker-compose.observability.yml up -d
```

Use Mode C when iterating on a single service via debugger, not for measurement runs.

### Common mistake — missing `observability/docker-compose.observability.override.yml`

Without the override file in the chain, services start with empty `OpenTelemetry:OtlpEndpoint` and silently discard all telemetry. The defensive WARN added in Chapter 2.5 will surface this at service startup — look for `[WARN] Kombats.Observability` in the logs. If you see that line, your compose chain is wrong.

---

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
