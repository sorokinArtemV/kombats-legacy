// modules/keycloak-app.bicep
// Keycloak Container App with external HTTPS ingress, realm.json import on
// startup, and a Postgres backend hosted in another Container App in the
// same environment.
//
// Key Container Apps gotchas baked in:
//   - KC_PROXY=edge          — Container Apps terminate TLS on the L7 load
//                              balancer, the container sees plain HTTP.
//   - KC_HOSTNAME_STRICT=false — hostname is taken from request headers
//                                (we don't know the FQDN ahead of deploy).
//   - subPath: 'keycloak'    — only the keycloak/ folder of the shared
//                              File Share is mounted, so other services'
//                              data isn't visible.

// === PARAMETERS ===

@description('Container App name. Defaults to "keycloak". Becomes the public subdomain.')
param name string = 'keycloak'

@description('Azure region.')
param location string

@description('Resource ID of the Container Apps Environment.')
param environmentId string

@description('Keycloak image. Use the Quarkus-based image, NOT the legacy jboss/keycloak.')
param image string = 'quay.io/keycloak/keycloak:24.0'

@description('Internal hostname of the Postgres Container App, e.g. postgres.internal.<env-domain>. No scheme, no port.')
param postgresHost string

@description('Postgres password for the keycloak DB user.')
@secure()
param postgresPassword string

@description('Initial admin password for the Keycloak master realm.')
@secure()
param keycloakAdminPassword string

@description('Logical name of the registered storage in the environment (output from env-storage.bicep). Used to mount realm.json.')
param storageName string

// === RESOURCE ===

resource app 'Microsoft.App/containerApps@2024-03-01' = {
  name: name
  location: location
  properties: {
    managedEnvironmentId: environmentId
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
      }
      secrets: [
        {
          name: 'postgres-password'
          value: postgresPassword
        }
        {
          name: 'admin-password'
          value: keycloakAdminPassword
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'keycloak'
          image: image
          command: [
            '/opt/keycloak/bin/kc.sh'
          ]
          args: [
            'start'
            '--import-realm'
            '--hostname-strict=false'
          ]
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            {
              name: 'KC_DB'
              value: 'postgres'
            }
            {
              name: 'KC_DB_URL'
              value: 'jdbc:postgresql://${postgresHost}:5432/keycloak'
            }
            {
              name: 'KC_DB_USERNAME'
              value: 'keycloak'
            }
            {
              name: 'KC_DB_PASSWORD'
              secretRef: 'postgres-password'
            }
            {
              name: 'KEYCLOAK_ADMIN'
              value: 'admin'
            }
            {
              name: 'KEYCLOAK_ADMIN_PASSWORD'
              secretRef: 'admin-password'
            }
            {
              name: 'KC_PROXY'
              value: 'edge'
            }
            {
              name: 'KC_HOSTNAME_STRICT'
              value: 'false'
            }
            {
              name: 'KC_HOSTNAME_STRICT_HTTPS'
              value: 'false'
            }
          ]
          volumeMounts: [
            {
              volumeName: 'realm-import'
              mountPath: '/opt/keycloak/data/import'
              subPath: 'keycloak'
            }
          ]
        }
      ]
      volumes: [
        {
          name: 'realm-import'
          storageType: 'AzureFile'
          storageName: storageName
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
output issuerUrl string = 'https://${app.properties.configuration.ingress.fqdn}/realms/kombats'
