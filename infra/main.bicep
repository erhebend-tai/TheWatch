// ═══════════════════════════════════════════════════════════════════════════════
// TheWatch Swarm Infrastructure — Bicep Template
// ═══════════════════════════════════════════════════════════════════════════════
//
// Deploys the full backing infrastructure for the TheWatch AI agent swarm:
//
//   1. Azure SignalR Service       — real-time dashboard broadcast (SwarmCoordinationService → Dashboard.Web)
//   2. Azure Cache for Redis       — agent state, Hangfire backplane, session cache
//   3. Azure SQL Server + DB       — Hangfire job persistence, swarm metadata
//   4. Container Apps Environment  — hosts RabbitMQ and future microservices
//   5. RabbitMQ Container App      — swarm-tasks exchange, swarm-results queue
//   6. Azure OpenAI (AIServices)   — GPT-4.1, GPT-4o, GPT-4o-mini, embeddings
//   7. Log Analytics Workspace     — centralized logging for all resources
//
// Usage:
//   az deployment group create \
//     --resource-group <rg-name> \
//     --template-file infra/main.bicep \
//     --parameters infra/main.parameters.json
//
// Region strategy:
//   - primaryLocation (default: eastus2)  — SignalR, Redis, Container Apps, RabbitMQ
//   - aiLocation (default: eastus)        — Azure OpenAI (best model availability)
//   - sqlLocation (default: centralus)    — SQL Server (eastus/eastus2 may block provisioning)
// ═══════════════════════════════════════════════════════════════════════════════

targetScope = 'resourceGroup'

// ── Parameters ───────────────────────────────────────────────────────────────

@description('Primary location for infrastructure resources.')
param primaryLocation string = 'eastus2'

@description('Location for Azure OpenAI. East US has the widest model availability.')
param aiLocation string = 'eastus'

@description('Location for Azure SQL Server. Some regions periodically block new server creation.')
param sqlLocation string = 'centralus'

@description('Base name prefix for all resources. Keep short (<=10 chars) to avoid name length limits.')
@minLength(3)
@maxLength(10)
param baseName string = 'thewatch'

@description('SQL Server administrator login name.')
param sqlAdminUser string = 'watchadmin'

@description('SQL Server administrator password. Must meet Azure complexity requirements.')
@secure()
param sqlAdminPassword string

@description('RabbitMQ default username.')
param rabbitUser string = 'thewatch'

@description('RabbitMQ default password.')
@secure()
param rabbitPassword string

@description('Azure OpenAI GPT-4.1 deployment capacity in thousands of tokens per minute.')
param gpt41Capacity int = 30

@description('Azure OpenAI GPT-4o deployment capacity in thousands of tokens per minute.')
param gpt4oCapacity int = 30

@description('Azure OpenAI GPT-4o-mini deployment capacity in thousands of tokens per minute.')
param gpt4oMiniCapacity int = 30

@description('Azure OpenAI text-embedding-3-large deployment capacity in thousands of tokens per minute.')
param embeddingCapacity int = 30

@description('Redis SKU: Basic, Standard, or Premium.')
@allowed(['Basic', 'Standard', 'Premium'])
param redisSku string = 'Basic'

@description('Redis cache size: C0 (250MB) through C6 (53GB).')
@allowed(['C0', 'C1', 'C2', 'C3', 'C4', 'C5', 'C6'])
param redisSize string = 'C0'

@description('SQL Database service objective (SKU). Basic for dev, S0+ for production.')
param sqlDbServiceObjective string = 'Basic'

@description('Tags applied to all resources.')
param tags object = {
  project: 'TheWatch'
  component: 'swarm-infra'
  managedBy: 'bicep'
}

// ── Variables ────────────────────────────────────────────────────────────────

var signalrName = '${baseName}-signalr'
var redisName = '${baseName}-redis'
var sqlServerName = '${baseName}db'
var sqlDbName = 'hangfire'
var cognitiveAccountName = '${baseName}-openai'
var containerEnvName = '${baseName}-cae'
var rabbitAppName = 'rabbitmq'
var logAnalyticsName = '${baseName}-logs'

// ── Log Analytics Workspace ──────────────────────────────────────────────────
// Centralized logging consumed by Container Apps, Redis diagnostics, etc.

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: logAnalyticsName
  location: primaryLocation
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

// ── Azure SignalR Service ────────────────────────────────────────────────────
// Real-time push from SwarmCoordinationService → Dashboard.Web
// Events: SwarmHeartbeat, SwarmGoalsUpdated, SwarmEscalationSweep,
//         SwarmInventoryRefreshRequested

resource signalr 'Microsoft.SignalRService/signalR@2024-03-01' = {
  name: signalrName
  location: primaryLocation
  tags: tags
  sku: {
    name: 'Free_F1'
    capacity: 1
  }
  kind: 'SignalR'
  properties: {
    features: [
      {
        flag: 'ServiceMode'
        value: 'Default'
      }
      {
        flag: 'EnableConnectivityLogs'
        value: 'True'
      }
    ]
    cors: {
      allowedOrigins: ['*']
    }
    tls: {
      clientCertEnabled: false
    }
  }
}

// ── Azure Cache for Redis ────────────────────────────────────────────────────
// Used by: Hangfire distributed lock, agent state cache, SignalR backplane (Phase 2),
//          swarm heartbeat tracking, goal progress aggregation

resource redis 'Microsoft.Cache/redis@2024-03-01' = {
  name: redisName
  location: primaryLocation
  tags: tags
  properties: {
    sku: {
      name: redisSku
      family: 'C'
      capacity: int(replace(redisSize, 'C', ''))
    }
    enableNonSslPort: false
    minimumTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    redisConfiguration: {
      'maxmemory-policy': 'allkeys-lru'
    }
  }
}

// ── Azure SQL Server + Hangfire Database ─────────────────────────────────────
// Hangfire job persistence: agent heartbeat (1m), escalation sweep (5m),
// goal aggregation (10m), inventory refresh (15m)

resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: sqlServerName
  location: sqlLocation
  tags: tags
  properties: {
    administratorLogin: sqlAdminUser
    administratorLoginPassword: sqlAdminPassword
    version: '12.0'
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
  }
}

// Allow Azure services (Container Apps, Functions, etc.) to reach SQL
resource sqlFirewallAzure 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sqlServer
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource sqlDb 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: sqlDbName
  location: sqlLocation
  tags: tags
  sku: {
    name: sqlDbServiceObjective
  }
  properties: {
    collation: 'SQL_Latin1_General_CP1_CI_AS'
    maxSizeBytes: 2147483648 // 2 GB
  }
}

// ── Container Apps Environment ───────────────────────────────────────────────
// Hosts RabbitMQ and future swarm microservices (agent runners, etc.)

resource containerEnv 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: containerEnvName
  location: primaryLocation
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.properties.customerId
        sharedKey: logAnalytics.listKeys().primarySharedKey
      }
    }
    zoneRedundant: false
  }
}

// ── RabbitMQ Container App ───────────────────────────────────────────────────
// Topic exchange: swarm-tasks (routing key = agent.<name>)
// Queue: swarm-results (agent completion messages)
// Management UI available on port 15672

resource rabbitMq 'Microsoft.App/containerApps@2024-03-01' = {
  name: rabbitAppName
  location: primaryLocation
  tags: tags
  properties: {
    environmentId: containerEnv.id
    configuration: {
      ingress: {
        external: false
        targetPort: 5672
        transport: 'tcp'
      }
      secrets: [
        {
          name: 'rabbit-password'
          value: rabbitPassword
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'rabbitmq'
          image: 'rabbitmq:3-management'
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            {
              name: 'RABBITMQ_DEFAULT_USER'
              value: rabbitUser
            }
            {
              name: 'RABBITMQ_DEFAULT_PASS'
              secretRef: 'rabbit-password'
            }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 1
      }
    }
  }
}

// ── Azure OpenAI (AIServices) ────────────────────────────────────────────────
// Model deployment strategy:
//   - gpt-4.1        → Supervisor & Orchestrator agents (complex reasoning)
//   - gpt-4o         → General-purpose agents (balanced cost/capability)
//   - gpt-4o-mini    → Specialist file agents (high volume, cost-efficient)
//   - text-embedding-3-large → RAG/embedding agents (vector search)

resource openai 'Microsoft.CognitiveServices/accounts@2024-10-01' = {
  name: cognitiveAccountName
  location: aiLocation
  tags: tags
  kind: 'AIServices'
  sku: {
    name: 'S0'
  }
  properties: {
    customSubDomainName: cognitiveAccountName
    publicNetworkAccess: 'Enabled'
    disableLocalAuth: false
  }
}

resource deployGpt41 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  parent: openai
  name: 'gpt-4.1'
  sku: {
    name: 'Standard'
    capacity: gpt41Capacity
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: 'gpt-4.1'
      version: '2025-04-14'
    }
  }
}

resource deployGpt4o 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  parent: openai
  name: 'gpt-4o'
  dependsOn: [deployGpt41]
  sku: {
    name: 'Standard'
    capacity: gpt4oCapacity
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: 'gpt-4o'
      version: '2024-08-06'
    }
  }
}

resource deployGpt4oMini 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  parent: openai
  name: 'gpt-4o-mini'
  dependsOn: [deployGpt4o]
  sku: {
    name: 'Standard'
    capacity: gpt4oMiniCapacity
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: 'gpt-4o-mini'
      version: '2024-07-18'
    }
  }
}

resource deployEmbedding 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  parent: openai
  name: 'text-embedding-3-large'
  dependsOn: [deployGpt4oMini]
  sku: {
    name: 'Standard'
    capacity: embeddingCapacity
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: 'text-embedding-3-large'
      version: '1'
    }
  }
}

// ── Outputs ──────────────────────────────────────────────────────────────────
// Used by deploy.sh to populate .env and dotnet user-secrets automatically.

@description('SignalR connection string for Dashboard.Api')
output signalrConnectionString string = 'Endpoint=https://${signalr.properties.hostName};AccessKey=${signalr.listKeys().primaryKey};Version=1.0;'

@description('SignalR hostname')
output signalrHostName string = signalr.properties.hostName

@description('Redis hostname')
output redisHostName string = redis.properties.hostName

@description('Redis SSL port')
output redisSslPort int = redis.properties.sslPort

@description('Redis connection string (key retrieved post-deploy via deploy.sh)')
output redisHost string = '${redis.properties.hostName}:${redis.properties.sslPort}'

@description('SQL Server FQDN')
output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName

@description('SQL connection string (password not included — use Key Vault or user-secrets)')
output sqlConnectionStringTemplate string = 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Initial Catalog=${sqlDbName};Persist Security Info=False;User ID=${sqlAdminUser};MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;'

@description('RabbitMQ internal FQDN within Container Apps environment')
output rabbitMqFqdn string = rabbitMq.properties.configuration.ingress.fqdn

@description('Azure OpenAI endpoint')
output openaiEndpoint string = openai.properties.endpoint

@description('Azure OpenAI resource name')
output openaiAccountName string = openai.name

@description('Container Apps environment name')
output containerEnvName string = containerEnv.name

@description('Log Analytics workspace ID')
output logAnalyticsWorkspaceId string = logAnalytics.properties.customerId
