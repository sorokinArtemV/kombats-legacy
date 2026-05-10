// workload.bicep
// All resources for the Kombats demo stack live here:
//   - Log Analytics workspace (logs sink)
//   - Container Apps Environment
//   - Storage Account + File Share + env-storage binding
//   - Statefuls (Postgres, Redis, RabbitMQ)
//   - Keycloak (with realm.json from File Share)
//   - Migrator job
//   - 5 backend services (bff, battle, players, matchmaking, chat)
//   - Static Web App (content deployed separately via swa CLI)
//
// Called from main.bicep as a module with resourceGroup() scope. The
// Resource Group itself is created by main.bicep at subscription scope.
// Nothing here is `existing` — this template can deploy from a clean RG.

targetScope = 'resourceGroup'

// === PARAMETERS ===

@description('Azure region. Set by main.bicep to match the RG location.')
param location string

@description('Image tag for backend services. Comes from Build.BuildId in CD, or "latest" for local deploys.')
param imageTag string = 'latest'

@description('GHCR username — package owner.')
param ghcrUsername string

@description('GHCR Personal Access Token with read:packages scope.')
@secure()
param ghcrToken string

@description('Migrator image reference, e.g. ghcr.io/sorokinartemv/kombats-migrator:42')
param migratorImage string

@description('Postgres password for the kombats superuser. Used by all backend services and as the bootstrap user for migrator.')
@secure()
param postgresPassword string

@description('Password for the keycloak DB user (created by migrator, used by Keycloak app).')
@secure()
param keycloakDbPassword string

@description('Initial admin password for Keycloak master realm.')
@secure()
param keycloakAdminPassword string

// === VARIABLES ===

var logAnalyticsName = 'log-kombats-demo'
var environmentName = 'cae-kombats-demo'

// Storage Account name must be globally unique across Azure. Use a stable
// suffix derived from the Resource Group ID so the same name is generated
// on every deploy in this RG.
var storageAccountName = 'kombats${uniqueString(resourceGroup().id)}'
var shareName = 'kombats-share'
var envStorageName = 'kombats-storage'

var swaName = 'swa-kombats-demo'

var imagePrefix = 'ghcr.io/${ghcrUsername}/kombats'

// Placeholder origin for non-BFF services. RFC 2606 reserves .invalid
// (cannot resolve), so it can never accidentally match a real Origin
// header. The 4 internal services fail-closed if Cors:AllowedOrigins is
// empty, so this satisfies the bootstrap requirement without exposing
// CORS to anything real. BFF gets the real SWA origin set via
// `az containerapp update` after deploy (see scripts/deploy-stack.ps1).
var corsPlaceholderOrigin = 'https://placeholder.invalid'

// === LOG ANALYTICS ===

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: logAnalyticsName
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

// === CONTAINER APPS ENVIRONMENT ===

resource env 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: environmentName
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.properties.customerId
        sharedKey: logAnalytics.listKeys().primarySharedKey
      }
    }
  }
}

// === STORAGE LAYER ===

module storage 'modules/storage.bicep' = {
  name: 'deploy-storage'
  params: {
    storageAccountName: storageAccountName
    shareName: shareName
    location: location
  }
}

module envStorage 'modules/env-storage.bicep' = {
  name: 'deploy-env-storage'
  params: {
    environmentName: env.name
    storageName: envStorageName
    storageAccountName: storage.outputs.storageAccountName
    shareName: storage.outputs.shareName
    storageAccountKey: storage.outputs.storageAccountKey
  }
}

// === STATEFULS ===
// No persistent volume — see Session 5 grabli #7 (Postgres + AzureFile/SMB
// chown limitation). Demo data is ephemeral; for prod, switch the share to
// NFS and flip usePersistentVolume=true.

module postgres 'modules/stateful-app.bicep' = {
  name: 'deploy-postgres'
  params: {
    name: 'postgres'
    location: location
    environmentId: env.id
    image: 'postgres:16-alpine'
    port: 5432
    usePersistentVolume: false
    cpu: '0.5'
    memory: '1Gi'
    envVars: [
      {
        name: 'POSTGRES_USER'
        value: 'kombats'
      }
      {
        name: 'POSTGRES_DB'
        value: 'kombats'
      }
    ]
    secretEnvVars: [
      {
        name: 'postgres-password'
        value: postgresPassword
      }
    ]
  }
}

module redis 'modules/stateful-app.bicep' = {
  name: 'deploy-redis'
  params: {
    name: 'redis'
    location: location
    environmentId: env.id
    image: 'redis:7-alpine'
    port: 6379
    usePersistentVolume: false
    cpu: '0.25'
    memory: '0.5Gi'
  }
}

module rabbitmq 'modules/stateful-app.bicep' = {
  name: 'deploy-rabbitmq'
  params: {
    name: 'rabbitmq'
    location: location
    environmentId: env.id
    image: 'rabbitmq:3-management-alpine'
    port: 5672
    usePersistentVolume: false
    cpu: '0.5'
    memory: '1Gi'
  }
}

// === BUILD INTERNAL HOSTNAMES ===

// Short app names: TCP via env-VIP doesn't route on consumption-only
// environments without VNet. Cluster-internal DNS resolves short names
// to k8s ClusterIP and bypasses the env-VIP entirely. See Session 5
// grabli #12.
var postgresHost = 'postgres'
var redisHost = 'redis'
var rabbitmqHost = 'rabbitmq'

// === KEYCLOAK ===

module keycloak 'modules/keycloak-app.bicep' = {
  name: 'deploy-keycloak'
  params: {
    location: location
    environmentId: env.id
    image: '${imagePrefix}-keycloak:${imageTag}'
    ghcrUsername: ghcrUsername
    ghcrToken: ghcrToken
    postgresHost: postgresHost
    postgresPassword: keycloakDbPassword
    keycloakAdminPassword: keycloakAdminPassword
    storageName: envStorage.outputs.storageName
  }
  dependsOn: [
    postgres
  ]
}

// === MIGRATOR JOB ===

module migrator 'modules/migrator-job.bicep' = {
  name: 'deploy-migrator'
  params: {
    location: location
    environmentId: env.id
    image: migratorImage
    ghcrUsername: ghcrUsername
    ghcrToken: ghcrToken
    postgresHost: postgresHost
    postgresPassword: postgresPassword
    keycloakDbPassword: keycloakDbPassword
  }
  dependsOn: [
    postgres
  ]
}

// === BACKEND SERVICES ===
//
// Cors:AllowedOrigins is required (non-Development fail-closed). For the
// 4 internal services we satisfy that with `corsPlaceholderOrigin` — the
// frontend never calls them directly. For BFF we also use the placeholder
// here; the real SWA origin is patched in after deploy via
// `az containerapp update`, once the SWA defaultHostname is known.
//
// minReplicas: 0 by default (scale-to-zero, cold start on first request).
// BFF overrides to 1 below — SignalR UX cannot tolerate the cold-start
// gap on the first user interaction.

var commonBackendEnv = [
  {
    name: 'Keycloak__Authority'
    value: keycloak.outputs.issuerUrl
  }
  {
    name: 'Keycloak__Audience'
    value: 'kombats-api'
  }
  {
    name: 'Messaging__RabbitMq__Host'
    value: rabbitmqHost
  }
  {
    name: 'Messaging__RabbitMq__Port'
    value: '5672'
  }
  {
    name: 'Messaging__RabbitMq__Username'
    value: 'guest'
  }
  {
    name: 'Messaging__RabbitMq__Password'
    value: 'guest'
  }
  {
    name: 'Messaging__RabbitMq__VirtualHost'
    value: '/'
  }
  {
    name: 'ConnectionStrings__Redis'
    value: '${redisHost}:6379,abortConnect=false'
  }
  {
    name: 'Cors__AllowedOrigins__0'
    value: corsPlaceholderOrigin
  }
]

// Postgres connection string built with the password interpolated. .NET
// services read the full DSN from ConnectionStrings:PostgresConnection —
// there's no separate password knob — so we bake the password in and pass
// it as a Container App secret (not a plain env value). secretRef-based
// env name set via the `envName` field on backend-app.bicep, since
// 'ConnectionStrings__PostgresConnection' doesn't fit UPPER_SNAKE.
var postgresConnectionString = 'Host=${postgresHost};Port=5432;Database=kombats;Username=kombats;Password=${postgresPassword};Maximum Pool Size=20;Minimum Pool Size=5;Connection Idle Lifetime=300'

var commonBackendSecrets = [
  {
    name: 'postgres-connection'
    envName: 'ConnectionStrings__PostgresConnection'
    value: postgresConnectionString
  }
]

module battle 'modules/backend-app.bicep' = {
  name: 'deploy-battle'
  params: {
    name: 'battle'
    location: location
    environmentId: env.id
    image: '${imagePrefix}-battle:${imageTag}'
    external: false
    ghcrUsername: ghcrUsername
    ghcrToken: ghcrToken
    envVars: commonBackendEnv
    secretEnvVars: commonBackendSecrets
  }
  dependsOn: [
    postgres
    redis
    rabbitmq
  ]
}

module players 'modules/backend-app.bicep' = {
  name: 'deploy-players'
  params: {
    name: 'players'
    location: location
    environmentId: env.id
    image: '${imagePrefix}-players:${imageTag}'
    external: false
    ghcrUsername: ghcrUsername
    ghcrToken: ghcrToken
    envVars: commonBackendEnv
    secretEnvVars: commonBackendSecrets
  }
  dependsOn: [
    postgres
    redis
    rabbitmq
  ]
}

module matchmaking 'modules/backend-app.bicep' = {
  name: 'deploy-matchmaking'
  params: {
    name: 'matchmaking'
    location: location
    environmentId: env.id
    image: '${imagePrefix}-matchmaking:${imageTag}'
    external: false
    ghcrUsername: ghcrUsername
    ghcrToken: ghcrToken
    envVars: commonBackendEnv
    secretEnvVars: commonBackendSecrets
  }
  dependsOn: [
    postgres
    redis
    rabbitmq
  ]
}

module chat 'modules/backend-app.bicep' = {
  name: 'deploy-chat'
  params: {
    name: 'chat'
    location: location
    environmentId: env.id
    image: '${imagePrefix}-chat:${imageTag}'
    external: false
    ghcrUsername: ghcrUsername
    ghcrToken: ghcrToken
    envVars: concat(commonBackendEnv, [
      {
        name: 'Players__BaseUrl'
        value: 'http://players'
      }
    ])
    secretEnvVars: commonBackendSecrets
  }
  dependsOn: [
    postgres
    redis
    rabbitmq
  ]
}

// BFF — adds Services__*__BaseUrl on top of commonBackendEnv. minReplicas=1
// keeps the container warm so SignalR negotiate doesn't pay cold-start on
// the first user. maxReplicas=1 because SignalR is sticky-session and we
// have no Redis/Service-Bus backplane (Session 10 territory).
var bffEnv = concat(
  commonBackendEnv,
  [
    {
      name: 'Services__Battle__BaseUrl'
      value: 'http://battle'
    }
    {
      name: 'Services__Players__BaseUrl'
      value: 'http://players'
    }
    {
      name: 'Services__Matchmaking__BaseUrl'
      value: 'http://matchmaking'
    }
    {
      name: 'Services__Chat__BaseUrl'
      value: 'http://chat'
    }
  ]
)

module bff 'modules/backend-app.bicep' = {
  name: 'deploy-bff'
  params: {
    name: 'bff'
    location: location
    environmentId: env.id
    image: '${imagePrefix}-bff:${imageTag}'
    external: true
    ghcrUsername: ghcrUsername
    ghcrToken: ghcrToken
    envVars: bffEnv
    secretEnvVars: commonBackendSecrets
    minReplicas: 1
    maxReplicas: 1
  }
  dependsOn: [
    battle
    players
    matchmaking
    chat
  ]
}

// === STATIC WEB APP ===
// Content (dist/) is uploaded separately via the SWA CLI in the deploy
// script — Bicep only provisions the resource and the deployment token.

module swa 'modules/static-web-app.bicep' = {
  name: 'deploy-swa'
  params: {
    name: swaName
    location: location
  }
}

// === OUTPUTS ===

output bffUrl string = 'https://${bff.outputs.fqdn}'
output keycloakUrl string = 'https://${keycloak.outputs.fqdn}'
output keycloakIssuer string = keycloak.outputs.issuerUrl
output swaUrl string = swa.outputs.url
output swaName string = swa.outputs.name
output storageAccountName string = storage.outputs.storageAccountName
output shareName string = storage.outputs.shareName
output environmentName string = env.name
