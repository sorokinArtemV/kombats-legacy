// main.bicep
// Subscription-scope orchestrator. Creates the Resource Group from
// scratch and deploys the full Kombats demo stack inside it.
// After `az group delete -g rg-kombats-demo`, this template recreates
// everything. Nothing is expected to pre-exist.
//
// Deploy command:
//   az deployment sub create --location westeurope `
//     --template-file infra/main.bicep `
//     --parameters infra/main.bicepparam `
//     --name kombats-stack-deploy
//
// `--location` here is the location of the deployment metadata record
// (subscription-scope deployments must live somewhere); resource
// locations are set inside the workload module via `param location`.

targetScope = 'subscription'

// === PARAMETERS ===

@description('Name of the Resource Group to create (and put everything into).')
param resourceGroupName string = 'rg-kombats-demo'

@description('Azure region for the RG and all resources inside it.')
param location string = 'westeurope'

@description('Image tag for backend services. Comes from Build.BuildId in CD, or "latest" for local deploys.')
param imageTag string = 'latest'

@description('GHCR username — package owner.')
param ghcrUsername string

@description('GHCR Personal Access Token with read:packages scope.')
@secure()
param ghcrToken string

@description('Migrator image reference, e.g. ghcr.io/sorokinartemv/kombats-migrator:42')
param migratorImage string

@description('Postgres superuser password.')
@secure()
param postgresPassword string

@description('Password for the keycloak DB user.')
@secure()
param keycloakDbPassword string

@description('Keycloak master admin password.')
@secure()
param keycloakAdminPassword string

// === RESOURCE GROUP ===

resource rg 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: resourceGroupName
  location: location
}

// === WORKLOAD ===

module workload 'workload.bicep' = {
  scope: rg
  name: 'kombats-workload'
  params: {
    location: location
    imageTag: imageTag
    ghcrUsername: ghcrUsername
    ghcrToken: ghcrToken
    migratorImage: migratorImage
    postgresPassword: postgresPassword
    keycloakDbPassword: keycloakDbPassword
    keycloakAdminPassword: keycloakAdminPassword
  }
}

// === OUTPUTS (proxied from workload) ===

output resourceGroupName string = rg.name
output bffUrl string = workload.outputs.bffUrl
output keycloakUrl string = workload.outputs.keycloakUrl
output keycloakIssuer string = workload.outputs.keycloakIssuer
output swaUrl string = workload.outputs.swaUrl
output swaName string = workload.outputs.swaName
output storageAccountName string = workload.outputs.storageAccountName
output shareName string = workload.outputs.shareName
output environmentName string = workload.outputs.environmentName
