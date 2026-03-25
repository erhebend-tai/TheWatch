# TheWatch Deployment Configuration

## Provisioned Azure Resources (March 2026)

| Resource | SKU / Tier | Region | Endpoint |
|----------|-----------|--------|----------|
| Azure SignalR Service | Free | East US 2 | `thewatchsignalr.service.signalr.net` |
| Azure Cache for Redis | Basic C0 | East US 2 | `thewatchredis.redis.cache.windows.net:6380` |
| RabbitMQ (Container App) | Custom | East US 2 | `thewatch-rabbitmq.internal:5672` |
| SQL Server | Basic | Central US | `thewatchsqlserver.database.windows.net` |
| Azure OpenAI (GPT-4.1) | S0 | East US 2 | `thewatchopenai.openai.azure.com` |
| Azure OpenAI (GPT-4o) | S0 | East US 2 | same endpoint, deployment: `gpt-4o` |
| Azure OpenAI (GPT-4o-mini) | S0 | East US 2 | same endpoint, deployment: `gpt-4o-mini` |
| Azure OpenAI (text-embedding-3-large) | S0 | East US 2 | same endpoint, deployment: `text-embedding-3-large` |
| Firebase (Auth + Firestore) | Spark | — | project: `gen-lang-client-0590872284` |

### Not Yet Provisioned

| Resource | Notes |
|----------|-------|
| Azure Container Registry | Needed before first deployment |
| PostgreSQL / PostGIS | Mock adapter active; provision when spatial queries go live |
| Cosmos DB | Mock adapter active; provision for DiskANN vector store |
| Anthropic API key | Empty in production config |
| Gemini API key | Empty in production config |
| VoyageAI API key | Empty in production config |
| Cloudflare TURN server | Needed for Watch Call WebRTC NAT traversal in production |

## Container Apps Environment

- **Name**: thewatch-cae (wonderfuldune-4c34e3da)
- **Region**: East US 2
- **Bicep template**: `deploy/container-apps.bicep`

## Azure Container Registry

Provision before first deployment:

```bash
az acr create --name thewatchacr --resource-group Project --location eastus2 --sku Basic
az acr login --name thewatchacr
```

## Build & Push

```bash
# From solution root
docker build -f deploy/Dockerfile.dashboard-api -t thewatchacr.azurecr.io/dashboard-api:latest .
docker build -f deploy/Dockerfile.dashboard-web -t thewatchacr.azurecr.io/dashboard-web:latest .
docker push thewatchacr.azurecr.io/dashboard-api:latest
docker push thewatchacr.azurecr.io/dashboard-web:latest
```

## Deploy

```bash
# Deploy via Bicep (preferred)
az deployment group create \
  --resource-group Project \
  --template-file deploy/container-apps.bicep \
  --parameters acrName=thewatchacr

# Or quick deploy a single container
az containerapp up --name dashboard-api --source . --environment thewatch-cae --resource-group Project
```

## Credential Management

Credentials are stored in:

- `.env` in project root (gitignored)
- `dotnet user-secrets` for Dashboard.Api (8 secrets) and Dashboard.Web (4 secrets)

Production config is in `appsettings.Production.json` — API keys are placeholders, actual values come from user-secrets or environment variables in Container Apps.

## Firebase Setup

Firebase CLI login and deploy must be run from the Windows host machine:

```bash
cd C:\Users\erheb\source\repos\TheWatch
firebase login
firebase init firestore
firebase deploy
```

Firebase config files in repo: `.firebaserc`, `firebase.json`, `firestore.rules`, `firestore.indexes.json`.

## SQL Server Note

East US and East US 2 were blocking new SQL Server provisioning at the time of setup, so it landed in Central US. This adds minor latency for the Container Apps in East US 2. Consider migrating when the region restriction lifts.
