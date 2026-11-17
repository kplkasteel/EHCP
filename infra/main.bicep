// Sandbox deployment: Queue Storage, Cosmos DB, Application Insights and four Function Apps.
// No private endpoints, no VNet, no managed identity. Deploy with `rake deploy`.

param location string = resourceGroup().location

var suffix = uniqueString(resourceGroup().id)
var storageName = 'stehcp${suffix}'
var cosmosName = 'cosmos-ehcp-${suffix}'
var databaseName = 'ehcp-team-shared-1'
var queueName = 'ehcp-processing'
var emailsContainer = 'ehcp-team-emails'
var casesContainer = 'ehcp-team-cases'

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageName
  location: location
  kind: 'StorageV2'
  sku: { name: 'Standard_LRS' }
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    supportsHttpsTrafficOnly: true
  }
}

resource queueService 'Microsoft.Storage/storageAccounts/queueServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

resource processingQueue 'Microsoft.Storage/storageAccounts/queueServices/queues@2023-05-01' = {
  parent: queueService
  name: queueName
}

resource cosmos 'Microsoft.DocumentDB/databaseAccounts@2024-05-15' = {
  name: cosmosName
  location: location
  kind: 'GlobalDocumentDB'
  properties: {
    databaseAccountOfferType: 'Standard'
    locations: [
      { locationName: location, failoverPriority: 0 }
    ]
    capabilities: [
      { name: 'EnableServerless' }
    ]
    publicNetworkAccess: 'Enabled'
  }
}

resource database 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2024-05-15' = {
  parent: cosmos
  name: databaseName
  properties: {
    resource: { id: databaseName }
  }
}

resource emailsContainerResource 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-05-15' = {
  parent: database
  name: emailsContainer
  properties: {
    resource: {
      id: emailsContainer
      partitionKey: { paths: [ '/id' ], kind: 'Hash' }
      defaultTtl: 2592000
    }
  }
}

resource casesContainerResource 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-05-15' = {
  parent: database
  name: casesContainer
  properties: {
    resource: {
      id: casesContainer
      partitionKey: { paths: [ '/entityId' ], kind: 'Hash' }
    }
  }
}

resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: 'plan-ehcp-${suffix}'
  location: location
  kind: 'linux'
  sku: { name: 'B1', tier: 'Basic' }
  properties: {
    reserved: true
  }
}

resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-ehcp-${suffix}'
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

resource insights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appi-ehcp-${suffix}'
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logs.id
  }
}

var storageConnection = 'DefaultEndpointsProtocol=https;AccountName=${storage.name};AccountKey=${storage.listKeys().keys[0].value};EndpointSuffix=${environment().suffixes.storage}'
var cosmosKey = cosmos.listKeys().primaryMasterKey

var commonSettings = [
  { name: 'AzureWebJobsStorage', value: storageConnection }
  { name: 'FUNCTIONS_EXTENSION_VERSION', value: '~4' }
  { name: 'FUNCTIONS_WORKER_RUNTIME', value: 'dotnet-isolated' }
  { name: 'AZURE_FUNCTIONS_ENVIRONMENT', value: 'Production' }
  { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: insights.properties.ConnectionString }
]

// Mailbox container names are not set here: App Service rejects setting names with hyphens, and the
// mailbox key 'ehcp-team-shared-1' contains them. They come from appsettings.json in each published app.
var cosmosSettings = [
  { name: 'Cosmos__Endpoint', value: cosmos.properties.documentEndpoint }
  { name: 'Cosmos__Key', value: cosmosKey }
  { name: 'Cosmos__Database', value: databaseName }
  { name: 'Cosmos__Initialize', value: 'false' }
]

resource mockGraph 'Microsoft.Web/sites@2023-12-01' = {
  name: 'func-mock-graph-${suffix}'
  location: location
  kind: 'functionapp,linux'
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNET-ISOLATED|10'
      alwaysOn: true
      appSettings: concat(commonSettings, [
        { name: 'OTEL_SERVICE_NAME', value: 'ehcp-mock-graph' }
      ])
    }
  }
}

resource mockExtraction 'Microsoft.Web/sites@2023-12-01' = {
  name: 'func-mock-extraction-${suffix}'
  location: location
  kind: 'functionapp,linux'
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNET-ISOLATED|10'
      alwaysOn: true
      appSettings: concat(commonSettings, [
        { name: 'OTEL_SERVICE_NAME', value: 'ehcp-mock-extraction' }
      ])
    }
  }
}

resource api 'Microsoft.Web/sites@2023-12-01' = {
  name: 'func-api-${suffix}'
  location: location
  kind: 'functionapp,linux'
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNET-ISOLATED|10'
      alwaysOn: true
      appSettings: concat(commonSettings, cosmosSettings, [
        { name: 'OTEL_SERVICE_NAME', value: 'ehcp-api' }
      ])
    }
  }
}

resource processing 'Microsoft.Web/sites@2023-12-01' = {
  name: 'func-processing-${suffix}'
  location: location
  kind: 'functionapp,linux'
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNET-ISOLATED|10'
      alwaysOn: true
      appSettings: concat(commonSettings, cosmosSettings, [
        { name: 'OTEL_SERVICE_NAME', value: 'ehcp-processing' }
        { name: 'ProcessingQueueName', value: queueName }
        { name: 'Processing__Graph__BaseUrl', value: 'https://${mockGraph.properties.defaultHostName}' }
        { name: 'Processing__Extraction__Endpoint', value: 'https://${mockExtraction.properties.defaultHostName}/api/extract' }
      ])
    }
  }
}

output storageAccountName string = storage.name
output cosmosAccountName string = cosmos.name
output cosmosEndpoint string = cosmos.properties.documentEndpoint
output databaseName string = databaseName
output queueName string = queueName
output processingAppName string = processing.name
output apiAppName string = api.name
output mockGraphAppName string = mockGraph.name
output mockExtractionAppName string = mockExtraction.name
output apiUrl string = 'https://${api.properties.defaultHostName}'
output mockGraphUrl string = 'https://${mockGraph.properties.defaultHostName}'
output mockExtractionUrl string = 'https://${mockExtraction.properties.defaultHostName}'
output appInsightsName string = insights.name
