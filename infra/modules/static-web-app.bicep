// modules/static-web-app.bicep
// Azure Static Web Apps resource for the Kombats SPA.
// Content (dist/) is deployed separately via the SWA CLI (`swa deploy`) —
// not via Bicep. Bicep only provisions the resource so its
// `defaultHostname` and deployment token become available.

@description('Name of the Static Web App.')
param name string

@description('Location. SWA Free tier is available in West Europe, Central US, East Asia, East US 2, West US 2.')
param location string

resource swa 'Microsoft.Web/staticSites@2024-04-01' = {
  name: name
  location: location
  sku: {
    name: 'Free'
    tier: 'Free'
  }
  properties: {
    // No repositoryUrl/branch/buildProperties — content comes via swa CLI.
    allowConfigFileUpdates: true
    stagingEnvironmentPolicy: 'Disabled'
  }
}

output url string = 'https://${swa.properties.defaultHostname}'
output name string = swa.name
