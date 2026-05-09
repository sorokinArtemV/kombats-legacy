// modules/backend-app.bicep
// Reusable module for a Kombats backend Container App.
// Pulls a private image from GHCR, accepts env vars and secrets, supports
// either external or internal ingress.

// === PARAMETERS ===

@description('Container App name. Becomes the subdomain in the FQDN: <name>.<env-domain>')
param name string

@description('Azure region.')
param location string

@description('Resource ID of the Container Apps Environment.')
param environmentId string

@description('Full image reference with tag, e.g. ghcr.io/sorokinartemv/kombats-bff:42')
param image string

@description('External (public HTTPS URL) or internal (only reachable from inside the environment).')
param external bool

@description('Port the container listens on. ASP.NET Core defaults to 8080.')
param targetPort int = 8080

@description('GHCR username — owner of the package.')
param ghcrUsername string

@description('GHCR Personal Access Token with read:packages scope.')
@secure()
param ghcrToken string

@description('Plain (non-secret) environment variables for the container. Array of { name, value } objects.')
param envVars array = []

@description('Secret environment variables. Array of { name, value }. Each becomes a Container App secret and is exposed as an env var via secretRef.')
param secretEnvVars array = []

// === VARIABLES ===

// Build the secrets array for Container App configuration:
//   ghcr-token (always present) + any user-supplied secrets.
var userSecrets = [for s in secretEnvVars: {
  name: s.name
  value: s.value
}]

var allSecrets = concat(
  [
    {
      name: 'ghcr-token'
      value: ghcrToken
    }
  ],
  userSecrets
)

// Build env entries that reference secrets via secretRef.
// Convention: secret name 'postgres-password' becomes env var POSTGRES_PASSWORD.
var secretEnvRefs = [for s in secretEnvVars: {
  name: toUpper(replace(s.name, '-', '_'))
  secretRef: s.name
}]

// === RESOURCE ===

resource app 'Microsoft.App/containerApps@2024-03-01' = {
  name: name
  location: location
  properties: {
    managedEnvironmentId: environmentId
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: external
        targetPort: targetPort
        transport: 'auto'
        allowInsecure: false
      }
      registries: [
        {
          server: 'ghcr.io'
          username: ghcrUsername
          passwordSecretRef: 'ghcr-token'
        }
      ]
      secrets: allSecrets
    }
    template: {
      containers: [
        {
          name: name
          image: image
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          env: concat(envVars, secretEnvRefs)
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 1
      }
    }
  }
}

// === OUTPUTS ===

output name string = app.name
output fqdn string = app.properties.configuration.ingress.fqdn
