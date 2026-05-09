// modules/migrator-job.bicep
// Container Apps Job that creates the Keycloak database and runs EF Core
// migrations for all four backend services with a database (players,
// matchmaking, battle, chat).
//
// Trigger: Manual. Started explicitly with `az containerapp job start`
// after Postgres is up. NOT auto-started on creation.
//
// Image: built separately by CI as ghcr.io/sorokinartemv/kombats-migrator:<tag>.
// The image bundles the run-migrations.sh script, .NET SDK, dotnet-ef tool,
// and the four service projects.

// === PARAMETERS ===

@description('Job name. Defaults to "migrator".')
param name string = 'migrator'

@description('Azure region.')
param location string

@description('Resource ID of the Container Apps Environment.')
param environmentId string

@description('Migrator image reference, e.g. ghcr.io/sorokinartemv/kombats-migrator:42')
param image string

@description('GHCR username.')
param ghcrUsername string

@description('GHCR Personal Access Token.')
@secure()
param ghcrToken string

@description('Internal hostname of Postgres, e.g. postgres.internal.<env-domain>. No scheme, no port.')
param postgresHost string

@description('Postgres superuser password (used to create the keycloak database and user).')
@secure()
param postgresPassword string

@description('Password for the keycloak DB user (created by the migrator and used by the Keycloak app).')
@secure()
param keycloakDbPassword string

// === RESOURCE ===

resource job 'Microsoft.App/jobs@2024-03-01' = {
  name: name
  location: location
  properties: {
    environmentId: environmentId
    configuration: {
      triggerType: 'Manual'
      replicaTimeout: 600
      replicaRetryLimit: 1
      manualTriggerConfig: {
        parallelism: 1
        replicaCompletionCount: 1
      }
      registries: [
        {
          server: 'ghcr.io'
          username: ghcrUsername
          passwordSecretRef: 'ghcr-token'
        }
      ]
      secrets: [
        {
          name: 'ghcr-token'
          value: ghcrToken
        }
        {
          name: 'postgres-password'
          value: postgresPassword
        }
        {
          name: 'keycloak-db-password'
          value: keycloakDbPassword
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'migrator'
          image: image
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          env: [
            {
              name: 'POSTGRES_HOST'
              value: postgresHost
            }
            {
              name: 'POSTGRES_PORT'
              value: '5432'
            }
            {
              name: 'POSTGRES_DB'
              value: 'kombats'
            }
            {
              name: 'POSTGRES_USER'
              value: 'kombats'
            }
            {
              name: 'POSTGRES_PASSWORD'
              secretRef: 'postgres-password'
            }
            {
              name: 'KEYCLOAK_DB_PASSWORD'
              secretRef: 'keycloak-db-password'
            }
          ]
        }
      ]
    }
  }
}

// === OUTPUTS ===

output name string = job.name
