// Starter production mapping. This file is intentionally non-production and requires environment-specific
// names, SKUs, networking and identity configuration before deployment.
targetScope = 'resourceGroup'

@description('Environment name')
param environment string = 'dev'

@description('Azure region')
param location string = resourceGroup().location

@description('Globally unique storage account name')
param storageAccountName string

@description('Service Bus namespace name')
param serviceBusName string

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageAccountName
  location: location
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    supportsHttpsTrafficOnly: true
  }
}

resource serviceBus 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' = {
  name: serviceBusName
  location: location
  sku: { name: 'Standard', tier: 'Standard' }
}

resource workflowQueue 'Microsoft.ServiceBus/namespaces/queues@2022-10-01-preview' = {
  name: 'claim-workflow'
  parent: serviceBus
  properties: {
    maxDeliveryCount: 10
    deadLetteringOnMessageExpiration: true
  }
}

resource notificationQueue 'Microsoft.ServiceBus/namespaces/queues@2022-10-01-preview' = {
  name: 'claim-notifications'
  parent: serviceBus
  properties: {
    maxDeliveryCount: 10
    deadLetteringOnMessageExpiration: true
  }
}

output storageAccountId string = storage.id
output serviceBusId string = serviceBus.id
