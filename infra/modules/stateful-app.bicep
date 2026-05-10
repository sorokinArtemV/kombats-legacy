// modules/stateful-app.bicep
// Reusable module for stateful services in Kombats: Postgres, Redis, RabbitMQ.
// All three pull public images from Docker Hub and expose internal TCP ingress.
// NOT used for Keycloak — see keycloak-app.bicep.
//
// Persistent volume is OPTIONAL — for the demo we skip it because Postgres
// on AzureFile (SMB) fails on chown. For prod, switch to an NFS-share-backed
// Storage Account and set usePersistentVolume=true.

// === PARAMETERS ===

@description('Container App name. Becomes the internal DNS hostname: <name>.internal.<env-domain>')
param name string

@description('Azure region.')
param location string

@description('Resource ID of the Container Apps Environment.')
param environmentId string

@description('Public image reference, e.g. postgres:16-alpine. No registry credentials needed.')
param image string

@description('Port the stateful service listens on. 5432 for Postgres, 6379 for Redis, 5672 for RabbitMQ.')
param port int

@description('Whether to mount a persistent volume. False for demo (data lost on restart), true for prod with NFS-backed share.')
param usePersistentVolume bool = false

@description('Logical name of the registered storage in the Container Apps Environment. Required when usePersistentVolume=true.')
param storageName string = ''

@description('Subpath inside the File Share where this service data lives. Required when usePersistentVolume=true.')
param mountSubPath string = ''

@description('Mount path inside the container. Required when usePersistentVolume=true.')
param mountPath string = ''

@description('Plain env vars. Array of { name, value }.')
param envVars array = []

@description('Secret env vars. Array of { name, value }. Same convention as backend-app.bicep: secret name "foo-bar" exposed as env FOO_BAR.')
param secretEnvVars array = []

@description('CPU allocation. Statefuls often want more than backend services.')
param cpu string = '0.5'

@description('Memory allocation. Must follow allowed CPU:memory ratios (1:2).')
param memory string = '1Gi'

// === VARIABLES ===

var allSecrets = [for s in secretEnvVars: {
  name: s.name
  value: s.value
}]

var secretEnvRefs = [for s in secretEnvVars: {
  name: toUpper(replace(s.name, '-', '_'))
  secretRef: s.name
}]

var volumeMounts = usePersistentVolume ? [
  {
    volumeName: 'data'
    mountPath: mountPath
    subPath: mountSubPath
  }
] : []

var volumes = usePersistentVolume ? [
  {
    name: 'data'
    storageType: 'AzureFile'
    storageName: storageName
  }
] : []

// === RESOURCE ===

resource app 'Microsoft.App/containerApps@2024-03-01' = {
  name: name
  location: location
  properties: {
    managedEnvironmentId: environmentId
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: false
        targetPort: port
        exposedPort: port
        transport: 'tcp'
      }
      secrets: allSecrets
    }
    template: {
      containers: [
        {
          name: name
          image: image
          resources: {
            cpu: json(cpu)
            memory: memory
          }
          env: concat(envVars, secretEnvRefs)
          volumeMounts: volumeMounts
        }
      ]
      volumes: volumes
      scale: {
        minReplicas: 1
        maxReplicas: 1
      }
    }
  }
}

// === OUTPUTS ===

output name string = app.name
