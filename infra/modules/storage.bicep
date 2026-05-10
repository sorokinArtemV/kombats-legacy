// modules/storage.bicep
// Storage Account with a single File Share for Keycloak realm.json.
// NOT used for Postgres/Redis/RabbitMQ — they run with no persistent
// volume (see Session 5 notes on Postgres + AzureFile permissions:
// SMB-mounted shares forbid chown, which the postgres image needs at
// init time, causing infinite restart loops).

// === PARAMETERS ===

@description('Storage Account name. Must be globally unique across Azure, lowercase, 3-24 chars, no dashes.')
@minLength(3)
@maxLength(24)
param storageAccountName string

@description('File Share name inside the Storage Account.')
param shareName string

@description('Azure region. Typically inherited from the Resource Group.')
param location string

// === RESOURCES ===

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageAccountName
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
  }
}

resource fileService 'Microsoft.Storage/storageAccounts/fileServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

resource share 'Microsoft.Storage/storageAccounts/fileServices/shares@2023-05-01' = {
  parent: fileService
  name: shareName
  properties: {
    shareQuota: 5
  }
}

// === OUTPUTS ===

output storageAccountName string = storage.name
output shareName string = share.name
output storageAccountKey string = storage.listKeys().keys[0].value
