// ============================================================================
// Kombats Demo — Main Bicep file
// Scope: Resource Group (rg-kombats-demo)
// ============================================================================

@description('Azure region for all resources')
param location string = resourceGroup().location

@description('Project name, used in resource naming')
param projectName string = 'kombats'

@description('Environment name')
param env string = 'demo'

// --- Tags applied to every resource ---
var tags = {
  project: projectName
  environment: env
  managedBy: 'bicep'
}

// --- Log Analytics Workspace (required by Container Apps Environment) ---
resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-${projectName}-${env}'
  location: location
  tags: tags
  properties: {
    retentionInDays: 30
    sku: {
      name: 'PerGB2018'
    }
  }
}

// --- Container Apps Environment ---
resource containerAppsEnv 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: 'cae-${projectName}-${env}'
  location: location
  tags: tags
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

// --- Outputs ---
output logAnalyticsName string = logAnalytics.name
output containerAppsEnvName string = containerAppsEnv.name
output containerAppsEnvId string = containerAppsEnv.id

// --- Budget alert (subscription-level, but scoped to this RG) ---
resource budget 'Microsoft.Consumption/budgets@2024-08-01' = {
  name: 'budget-${projectName}-${env}'
  properties: {
    timePeriod: {
      startDate: '2026-05-01'
      endDate: '2027-05-01'
    }
    timeGrain: 'Monthly'
    amount: 50
    category: 'Cost'
    notifications: {
      actual80: {
        enabled: true
        operator: 'GreaterThanOrEqualTo'
        threshold: 80
        contactEmails: ['artem.sorokin.va@gmail.com']
      }
      actual100: {
        enabled: true
        operator: 'GreaterThanOrEqualTo'
        threshold: 100
        contactEmails: ['artem.sorokin.va@gmail.com']
      }
    }
  }
}
