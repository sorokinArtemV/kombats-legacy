// main.bicep
// Top-level orchestrator for Kombats demo stack.
// Deploys: storage, env-storage, statefuls (Postgres, Redis, RabbitMQ),
// Keycloak, migrator job, 5 backend services (bff, battle, players,
// matchmaking, chat). All inside an existing Container Apps Environment
// from Session 3.

targetScope = 'resourceGroup'

// === PARAMETERS ===

@description('Name of the existing Container Apps Environment (created in Session 3).')
param environmentName string

@description('Azure region. Defaults to the Resource Group region.')
param location string = resourceGroup().location

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

@description('Allowed origins for BFF CORS. Use ["*"] for demo, specific domains for prod. Maps to Cors__AllowedOrigins__N env vars.')
param corsAllowedOrigins array = ['*']

// === VARIABLES ===

// Storage Account name must be globally unique across Azure. Use a stable
// suffix derived from the Resource Group ID so the same name is generated
// on every deploy in this RG.
var storageAccountName = 'kombats${uniqueString(resourceGroup().id)}'
var shareName = 'kombats-share'
var envStorageName = 'kombats-storage'

var imagePrefix = 'ghcr.io/${ghcrUsername}/kombats'

// === EXISTING RESOURCES ===

// The Container Apps Environment was created in Session 3.
// We reference it to get its ID (for managedEnvironmentId in apps) and
// its defaultDomain (for building internal FQDNs).
resource env 'Microsoft.App/managedEnvironments@2024-03-01' existing = {
  name: environmentName
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
    environmentName: environmentName
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

// Short app names: TCP via env-VIP doesn't route on
// consumption-only environments without VNet. Cluster-internal
// DNS resolves short names to k8s ClusterIP and bypasses the
// env-VIP entirely. See Session 5 grabli #12.
var postgresHost = 'postgres'
var redisHost = 'redis'
var rabbitmqHost = 'rabbitmq'

// === KEYCLOAK ===

module keycloak 'modules/keycloak-app.bicep' = {
  name: 'deploy-keycloak'
  params: {
    location: location
    environmentId: env.id
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

// CORS keys map to .NET array config via the __N suffix; required because
// Program.cs throws if Cors:AllowedOrigins is empty in non-Development
// (Session 5 grabli #13 — applies to every .NET service, not just BFF).
var corsEnv = [for (origin, i) in corsAllowedOrigins: {
  name: 'Cors__AllowedOrigins__${i}'
  value: origin
}]

// Common environment variables for all four "headless" backend services
// (battle, players, matchmaking, chat). BFF gets these PLUS service URLs.
var commonBackendEnv = concat([
  {
    name: 'ConnectionStrings__Postgres'
    value: 'Host=${postgresHost};Port=5432;Database=kombats;Username=kombats;'
  }
  {
    name: 'ConnectionStrings__Redis'
    value: '${redisHost}:6379'
  }
  {
    name: 'ConnectionStrings__RabbitMQ'
    value: 'amqp://guest:guest@${rabbitmqHost}:5672/'
  }
  {
    name: 'Authentication__Authority'
    value: keycloak.outputs.issuerUrl
  }
], corsEnv)

var commonBackendSecrets = [
  {
    name: 'postgres-password'
    value: postgresPassword
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
    envVars: commonBackendEnv
    secretEnvVars: commonBackendSecrets
  }
  dependsOn: [
    postgres
    redis
    rabbitmq
  ]
}

// BFF — has Services__*__BaseUrl on top of commonBackendEnv (which already
// includes CORS).
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
  }
  dependsOn: [
    battle
    players
    matchmaking
    chat
    keycloak
  ]
}

// === OUTPUTS ===

output bffUrl string = 'https://${bff.outputs.fqdn}'
output keycloakUrl string = 'https://${keycloak.outputs.fqdn}'
output keycloakIssuer string = keycloak.outputs.issuerUrl
output storageAccountName string = storage.outputs.storageAccountName
output shareName string = storage.outputs.shareName
