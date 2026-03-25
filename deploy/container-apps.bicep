// =============================================================================
// TheWatch Container Apps — Bicep deployment template
// =============================================================================
// Deploys Dashboard API and Dashboard Web as Azure Container Apps.
// Expects ACR, Container Apps Environment, and all backing services already provisioned.
//
// Usage:
//   az deployment group create -g Project -f deploy/container-apps.bicep \
//     --parameters acrName=thewatchacr environmentId=<cae-resource-id>
// =============================================================================

@description('Azure Container Registry name')
param acrName string = 'thewatchacr'

@description('Container Apps Environment resource ID')
param environmentId string

@description('Docker image tag')
param imageTag string = 'latest'

// ── Secrets (from Key Vault or parameters) ──────────────────────────────────

@secure()
param sqlConnectionString string
@secure()
param redisConnectionString string
@secure()
param cosmosConnectionString string
@secure()
param signalrConnectionString string
@secure()
param azureOpenAIApiKey string
@secure()
param rabbitMqPassword string

// ── Dashboard API ───────────────────────────────────────────────────────────

resource dashboardApi 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'dashboard-api'
  location: resourceGroup().location
  properties: {
    managedEnvironmentId: environmentId
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'http'
        corsPolicy: {
          allowedOrigins: ['*']
          allowedMethods: ['*']
          allowedHeaders: ['*']
        }
      }
      registries: [
        {
          server: '${acrName}.azurecr.io'
          identity: 'system'
        }
      ]
      secrets: [
        { name: 'sql-connection', value: sqlConnectionString }
        { name: 'redis-connection', value: redisConnectionString }
        { name: 'cosmos-connection', value: cosmosConnectionString }
        { name: 'signalr-connection', value: signalrConnectionString }
        { name: 'aoai-api-key', value: azureOpenAIApiKey }
        { name: 'rabbitmq-password', value: rabbitMqPassword }
      ]
    }
    template: {
      containers: [
        {
          name: 'dashboard-api'
          image: '${acrName}.azurecr.io/dashboard-api:${imageTag}'
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
            { name: 'ConnectionStrings__thewatch-sqlserver', secretRef: 'sql-connection' }
            { name: 'ConnectionStrings__thewatch-redis', secretRef: 'redis-connection' }
            { name: 'ConnectionStrings__thewatch-cosmos', secretRef: 'cosmos-connection' }
            { name: 'Azure__SignalR__ConnectionString', secretRef: 'signalr-connection' }
            { name: 'AIProviders__AzureOpenAI__ApiKey', secretRef: 'aoai-api-key' }
            { name: 'AIProviders__AzureOpenAI__Endpoint', value: 'https://watch-project-resource.cognitiveservices.azure.com/' }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 5
        rules: [
          {
            name: 'http-scaling'
            http: {
              metadata: {
                concurrentRequests: '50'
              }
            }
          }
        ]
      }
    }
  }
  identity: {
    type: 'SystemAssigned'
  }
}

// ── Dashboard Web ───────────────────────────────────────────────────────────

resource dashboardWeb 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'dashboard-web'
  location: resourceGroup().location
  properties: {
    managedEnvironmentId: environmentId
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'http'
      }
      registries: [
        {
          server: '${acrName}.azurecr.io'
          identity: 'system'
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'dashboard-web'
          image: '${acrName}.azurecr.io/dashboard-web:${imageTag}'
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          env: [
            { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
            { name: 'services__dashboard-api__https__0', value: 'https://${dashboardApi.properties.configuration.ingress.fqdn}' }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 3
      }
    }
  }
  identity: {
    type: 'SystemAssigned'
  }
}

// ── Qdrant Sidecar ──────────────────────────────────────────────────────────

resource qdrant 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'thewatch-qdrant'
  location: resourceGroup().location
  properties: {
    managedEnvironmentId: environmentId
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: false
        targetPort: 6333
        transport: 'http'
      }
    }
    template: {
      containers: [
        {
          name: 'qdrant'
          image: 'qdrant/qdrant:latest'
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          volumeMounts: [
            {
              volumeName: 'qdrant-data'
              mountPath: '/qdrant/storage'
            }
          ]
        }
      ]
      volumes: [
        {
          name: 'qdrant-data'
          storageType: 'EmptyDir'
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 1
      }
    }
  }
}

// ── Outputs ─────────────────────────────────────────────────────────────────

output dashboardApiFqdn string = dashboardApi.properties.configuration.ingress.fqdn
output dashboardWebFqdn string = dashboardWeb.properties.configuration.ingress.fqdn
output qdrantFqdn string = qdrant.properties.configuration.ingress.fqdn
