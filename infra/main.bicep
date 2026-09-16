@description('Sample only. Split into modules and add identities, diagnostics and networking')
param location string = resourceGroup().location
param environment string = 'dev'

resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: 'claims-plan-${environment}'
  location: location
  sku: { name: 'B1', tier: 'Basic' }
}

resource claimsApi 'Microsoft.Web/sites@2023-12-01' = {
  name: 'claims-api-${uniqueString(resourceGroup().id, environment)}'
  location: location
  kind: 'app'
  properties: {
    serverFarmId: plan.id
    siteConfig: { alwaysOn: true }
  }
}

resource serviceBus 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' = {
  name: 'claims-bus-${uniqueString(resourceGroup().id, environment)}'
  location: location
  sku: { name: 'Standard', tier: 'Standard' }
}

resource claimsQueue 'Microsoft.ServiceBus/namespaces/queues@2022-10-01-preview' = {
  parent: serviceBus
  name: 'claims'
  properties: {
    maxDeliveryCount: 10
    deadLetteringOnMessageExpiration: true
  }
}

// Add Azure SQL, Storage, Key Vault, Functions, APIM, Front Door,
// VNet/private endpoints, NAT Gateway and diagnostics through dedicated modules.
