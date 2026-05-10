// modules/env-storage.bicep
// Registers the Storage Account File Share as a named storage inside an
// existing Container Apps Environment. After this, any Container App in
// the environment can mount the share by referring to `storageName`.

// === PARAMETERS ===

@description('Name of the existing Container Apps Environment.')
param environmentName string

@description('Logical name to register the storage under inside the environment. Container Apps reference this name in their volumes block.')
param storageName string

@description('Storage Account name (output from storage.bicep).')
param storageAccountName string

@description('File Share name (output from storage.bicep).')
param shareName string

@description('Storage Account access key. Marked secure so its value is not logged or exposed in deployment history.')
@secure()
param storageAccountKey string

// === EXISTING RESOURCE REFERENCE ===

resource env 'Microsoft.App/managedEnvironments@2024-03-01' existing = {
  name: environmentName
}

// === RESOURCE ===

resource envStorage 'Microsoft.App/managedEnvironments/storages@2024-03-01' = {
  parent: env
  name: storageName
  properties: {
    azureFile: {
      accountName: storageAccountName
      accountKey: storageAccountKey
      shareName: shareName
      accessMode: 'ReadWrite'
    }
  }
}

// === OUTPUTS ===

output storageName string = envStorage.name
