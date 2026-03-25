#!/usr/bin/env bash
# ═══════════════════════════════════════════════════════════════════════════════
# TheWatch Swarm Infrastructure — Deploy & Wire Credentials
# ═══════════════════════════════════════════════════════════════════════════════
#
# Deploys the Bicep template, retrieves all keys/connection strings,
# writes .env, and populates dotnet user-secrets for Dashboard.Api and Web.
#
# Usage:
#   ./infra/deploy.sh                          # uses defaults
#   ./infra/deploy.sh -g MyResourceGroup       # custom resource group
#   ./infra/deploy.sh -g MyRG -n myprefix      # custom RG + name prefix
#   ./infra/deploy.sh --dry-run                # validate only, no deploy
#   ./infra/deploy.sh --environment staging    # deploy to staging environment
#   ./infra/deploy.sh --environment production # deploy to production environment
#   ./infra/deploy.sh --containers             # build & push container images to ACR
#
# Prerequisites:
#   - az cli logged in (az login)
#   - dotnet SDK installed
# ═══════════════════════════════════════════════════════════════════════════════
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"

# ── Defaults ──────────────────────────────────────────────────────────────────
RESOURCE_GROUP="Watch-Init"
BASE_NAME="thewatch"
SQL_ADMIN_USER="watchadmin"
SQL_ADMIN_PASS=""
RABBIT_USER="thewatch"
RABBIT_PASS=""
DRY_RUN=false
DEPLOY_CONTAINERS=false
ENVIRONMENT="dev"
PARAMS_FILE="$SCRIPT_DIR/main.parameters.dev.json"
ACR_LOGIN_SERVER=""

# ── Parse Arguments ───────────────────────────────────────────────────────────
while [[ $# -gt 0 ]]; do
  case "$1" in
    -g|--resource-group) RESOURCE_GROUP="$2"; shift 2;;
    -n|--name)           BASE_NAME="$2"; shift 2;;
    --sql-password)      SQL_ADMIN_PASS="$2"; shift 2;;
    --rabbit-password)   RABBIT_PASS="$2"; shift 2;;
    --params)            PARAMS_FILE="$2"; shift 2;;
    --environment|-e)    ENVIRONMENT="$2"; shift 2;;
    --containers)        DEPLOY_CONTAINERS=true; shift;;
    --acr)               ACR_LOGIN_SERVER="$2"; shift 2;;
    --dry-run)           DRY_RUN=true; shift;;
    -h|--help)
      echo "Usage: $0 [-g resource-group] [-n base-name] [--environment staging|production] [--containers] [--acr server] [--sql-password pw] [--rabbit-password pw] [--dry-run]"
      exit 0;;
    *) echo "Unknown arg: $1"; exit 1;;
  esac
done

# ── Resolve environment-specific parameters file ────────────────────────────
if [[ "$ENVIRONMENT" != "dev" && -f "$SCRIPT_DIR/main.parameters.${ENVIRONMENT}.json" ]]; then
  PARAMS_FILE="$SCRIPT_DIR/main.parameters.${ENVIRONMENT}.json"
  echo "Using environment-specific parameters: $PARAMS_FILE"
fi

# ── Resolve ACR login server ────────────────────────────────────────────────
if [[ -z "$ACR_LOGIN_SERVER" ]]; then
  ACR_LOGIN_SERVER="${BASE_NAME}.azurecr.io"
fi

# ── Prompt for secrets if not provided ────────────────────────────────────────
if [[ -z "$SQL_ADMIN_PASS" ]]; then
  read -sp "SQL admin password: " SQL_ADMIN_PASS; echo
fi
if [[ -z "$RABBIT_PASS" ]]; then
  read -sp "RabbitMQ password: " RABBIT_PASS; echo
fi

echo "══════════════════════════════════════════════════════════════"
echo "  TheWatch Swarm Infrastructure Deployment"
echo "══════════════════════════════════════════════════════════════"
echo "  Resource Group : $RESOURCE_GROUP"
echo "  Base Name      : $BASE_NAME"
echo "  Template       : $SCRIPT_DIR/main.bicep"
echo "  Parameters     : $PARAMS_FILE"
echo "  Dry Run        : $DRY_RUN"
echo "══════════════════════════════════════════════════════════════"

# ── Ensure resource group exists ──────────────────────────────────────────────
if ! az group show --name "$RESOURCE_GROUP" &>/dev/null; then
  echo "Creating resource group $RESOURCE_GROUP in eastus2..."
  az group create --name "$RESOURCE_GROUP" --location eastus2 --output none
fi

# ── Deploy or Validate ────────────────────────────────────────────────────────
DEPLOY_ARGS=(
  --resource-group "$RESOURCE_GROUP"
  --template-file "$SCRIPT_DIR/main.bicep"
  --parameters "$PARAMS_FILE"
  --parameters "sqlAdminPassword=$SQL_ADMIN_PASS"
  --parameters "rabbitPassword=$RABBIT_PASS"
  --parameters "baseName=$BASE_NAME"
  --output json
)

if $DRY_RUN; then
  echo "Validating template (dry run)..."
  az deployment group validate "${DEPLOY_ARGS[@]}"
  echo "Validation passed."
  exit 0
fi

echo "Deploying infrastructure (this may take 15-25 minutes)..."
DEPLOY_OUTPUT=$(az deployment group create \
  --name "${BASE_NAME}-swarm-$(date +%Y%m%d%H%M)" \
  "${DEPLOY_ARGS[@]}")

echo "Deployment complete. Extracting outputs..."

# ── Extract Bicep Outputs ─────────────────────────────────────────────────────
get_output() { echo "$DEPLOY_OUTPUT" | jq -r ".properties.outputs.$1.value // empty"; }

SIGNALR_CONN=$(get_output signalrConnectionString)
SIGNALR_HOST=$(get_output signalrHostName)
REDIS_HOST=$(get_output redisHostName)
REDIS_PORT=$(get_output redisSslPort)
RABBIT_FQDN=$(get_output rabbitMqFqdn)
SQL_FQDN=$(get_output sqlServerFqdn)
SQL_CONN_TEMPLATE=$(get_output sqlConnectionStringTemplate)
OPENAI_ENDPOINT=$(get_output openaiEndpoint)
OPENAI_ACCOUNT=$(get_output openaiAccountName)

# ── Retrieve secrets that require list-keys ───────────────────────────────────
echo "Retrieving Redis access key..."
REDIS_KEY=$(az redis list-keys --name "${BASE_NAME}-redis" --resource-group "$RESOURCE_GROUP" \
  --query "primaryKey" -o tsv 2>/dev/null || echo "PENDING")

REDIS_CONN="${REDIS_HOST}:${REDIS_PORT},password=${REDIS_KEY},ssl=True,abortConnect=False"

echo "Retrieving OpenAI API key..."
# OpenAI may be in a different resource group; try the deployment RG first, then Project
OPENAI_KEY=$(az cognitiveservices account keys list --name "$OPENAI_ACCOUNT" \
  --resource-group "$RESOURCE_GROUP" --query "key1" -o tsv 2>/dev/null || \
  az cognitiveservices account keys list --name "$OPENAI_ACCOUNT" \
  --resource-group "Project" --query "key1" -o tsv 2>/dev/null || echo "RETRIEVE_MANUALLY")

SQL_CONN="${SQL_CONN_TEMPLATE}Password=${SQL_ADMIN_PASS};"

# ── Write .env ────────────────────────────────────────────────────────────────
ENV_FILE="$PROJECT_ROOT/.env"
echo "Writing $ENV_FILE..."

cat > "$ENV_FILE" << ENVEOF
# ═══════════════════════════════════════════════════════════════════
# TheWatch Swarm Infrastructure — Azure Resource Credentials
# Generated: $(date -u +"%Y-%m-%dT%H:%M:%SZ") by infra/deploy.sh
# ═══════════════════════════════════════════════════════════════════

# ── Azure Subscription ────────────────────────────────────────────
AZURE_SUBSCRIPTION_ID=$(az account show --query id -o tsv)
AZURE_TENANT_ID=$(az account show --query tenantId -o tsv)
AZURE_RESOURCE_GROUP=${RESOURCE_GROUP}

# ── Azure OpenAI ──────────────────────────────────────────────────
AZURE_OPENAI_ENDPOINT=${OPENAI_ENDPOINT}
AZURE_OPENAI_API_KEY=${OPENAI_KEY}
AZURE_OPENAI_DEPLOYMENT_GPT41=gpt-4.1
AZURE_OPENAI_DEPLOYMENT_GPT4O=gpt-4o
AZURE_OPENAI_DEPLOYMENT_GPT4O_MINI=gpt-4o-mini
AZURE_OPENAI_DEPLOYMENT_EMBEDDING=text-embedding-3-large
AZURE_OPENAI_API_VERSION=2024-12-01-preview

# ── Azure SignalR Service ─────────────────────────────────────────
AZURE_SIGNALR_CONNECTION_STRING=${SIGNALR_CONN}
AZURE_SIGNALR_HOSTNAME=${SIGNALR_HOST}

# ── RabbitMQ on Container Apps ────────────────────────────────────
RABBITMQ_HOST=${RABBIT_FQDN}
RABBITMQ_PORT=5672
RABBITMQ_USER=${RABBIT_USER}
RABBITMQ_PASSWORD=${RABBIT_PASS}
RABBITMQ_VHOST=/
RABBITMQ_EXCHANGE=swarm-tasks
RABBITMQ_RESULTS_QUEUE=swarm-results

# ── Azure Cache for Redis ────────────────────────────────────────
REDIS_HOST=${REDIS_HOST}
REDIS_PORT=${REDIS_PORT}
REDIS_PASSWORD=${REDIS_KEY}
REDIS_SSL=true
REDIS_CONNECTION_STRING=${REDIS_CONN}

# ── Azure SQL Server ─────────────────────────────────────────────
SQL_SERVER=${SQL_FQDN}
SQL_DATABASE=hangfire
SQL_USER=${SQL_ADMIN_USER}
SQL_PASSWORD=${SQL_ADMIN_PASS}
SQL_CONNECTION_STRING=${SQL_CONN}

# ── Aspire / Local Dev Overrides ─────────────────────────────────
ASPIRE_DASHBOARD_URL=https://localhost:17037
DOTNET_ENVIRONMENT=Development
ENVEOF

echo ".env written."

# ── Dotnet User-Secrets ───────────────────────────────────────────────────────
wire_secrets() {
  local proj_dir="$1"
  local proj_name="$(basename "$proj_dir")"

  if [[ ! -f "$proj_dir"/*.csproj ]]; then
    echo "  Skipping $proj_name (no .csproj found)"
    return
  fi

  echo "  Wiring secrets for $proj_name..."
  pushd "$proj_dir" > /dev/null

  dotnet user-secrets init 2>/dev/null || true
  dotnet user-secrets set "Azure:OpenAI:Endpoint" "$OPENAI_ENDPOINT" 2>/dev/null
  dotnet user-secrets set "Azure:OpenAI:ApiKey" "$OPENAI_KEY" 2>/dev/null
  dotnet user-secrets set "Azure:SignalR:ConnectionString" "$SIGNALR_CONN" 2>/dev/null
  dotnet user-secrets set "ConnectionStrings:Redis" "$REDIS_CONN" 2>/dev/null

  # API-specific secrets
  if [[ "$proj_name" == *"Api"* ]]; then
    dotnet user-secrets set "ConnectionStrings:Hangfire" "$SQL_CONN" 2>/dev/null
    dotnet user-secrets set "RabbitMQ:Host" "$RABBIT_FQDN" 2>/dev/null
    dotnet user-secrets set "RabbitMQ:User" "$RABBIT_USER" 2>/dev/null
    dotnet user-secrets set "RabbitMQ:Password" "$RABBIT_PASS" 2>/dev/null
  fi

  popd > /dev/null
}

echo "Wiring dotnet user-secrets..."
wire_secrets "$PROJECT_ROOT/TheWatch.Dashboard.Api"
wire_secrets "$PROJECT_ROOT/TheWatch.Dashboard.Web"

# ── Container Build & Push (optional) ─────────────────────────────────────────
if $DEPLOY_CONTAINERS; then
  echo ""
  echo "Building and pushing container images to ACR: $ACR_LOGIN_SERVER"
  echo ""

  # Login to ACR
  az acr login --name "${BASE_NAME}" 2>/dev/null || {
    echo "::warning::ACR login failed. Ensure the ACR exists and you have push permissions."
    echo "Trying docker login fallback..."
    ACR_PASSWORD=$(az acr credential show --name "${BASE_NAME}" --query "passwords[0].value" -o tsv 2>/dev/null || echo "")
    if [[ -n "$ACR_PASSWORD" ]]; then
      echo "$ACR_PASSWORD" | docker login "$ACR_LOGIN_SERVER" --username "${BASE_NAME}" --password-stdin
    else
      echo "::error::Cannot authenticate to ACR. Skipping container deployment."
      DEPLOY_CONTAINERS=false
    fi
  }

  if $DEPLOY_CONTAINERS; then
    IMAGE_TAG="$(date +%Y%m%d%H%M)-$(git rev-parse --short HEAD 2>/dev/null || echo 'local')"

    echo "Image tag: $IMAGE_TAG"
    echo "Environment: $ENVIRONMENT"

    # Build and push Dashboard.Api
    echo "Building Dashboard.Api..."
    docker build \
      -f "$PROJECT_ROOT/TheWatch.Dashboard.Api/Dockerfile" \
      -t "${ACR_LOGIN_SERVER}/thewatch/dashboard-api:${IMAGE_TAG}" \
      -t "${ACR_LOGIN_SERVER}/thewatch/dashboard-api:${ENVIRONMENT}" \
      -t "${ACR_LOGIN_SERVER}/thewatch/dashboard-api:latest" \
      "$PROJECT_ROOT"
    docker push "${ACR_LOGIN_SERVER}/thewatch/dashboard-api" --all-tags

    # Build and push Functions
    echo "Building Functions..."
    docker build \
      -f "$PROJECT_ROOT/TheWatch.Functions/Dockerfile" \
      -t "${ACR_LOGIN_SERVER}/thewatch/functions:${IMAGE_TAG}" \
      -t "${ACR_LOGIN_SERVER}/thewatch/functions:${ENVIRONMENT}" \
      -t "${ACR_LOGIN_SERVER}/thewatch/functions:latest" \
      "$PROJECT_ROOT"
    docker push "${ACR_LOGIN_SERVER}/thewatch/functions" --all-tags

    # Build and push WorkerServices
    echo "Building WorkerServices..."
    docker build \
      -f "$PROJECT_ROOT/TheWatch.WorkerServices/Dockerfile" \
      -t "${ACR_LOGIN_SERVER}/thewatch/worker:${IMAGE_TAG}" \
      -t "${ACR_LOGIN_SERVER}/thewatch/worker:${ENVIRONMENT}" \
      -t "${ACR_LOGIN_SERVER}/thewatch/worker:latest" \
      "$PROJECT_ROOT"
    docker push "${ACR_LOGIN_SERVER}/thewatch/worker" --all-tags

    echo "All container images pushed to ${ACR_LOGIN_SERVER}"

    # Deploy to Container Apps if environment is staging or production
    if [[ "$ENVIRONMENT" == "staging" || "$ENVIRONMENT" == "production" ]]; then
      CONTAINER_ENV="${BASE_NAME}-cae"

      echo "Deploying containers to Azure Container Apps (${ENVIRONMENT})..."

      # Dashboard.Api — external ingress
      az containerapp update \
        --name "${BASE_NAME}-api" \
        --resource-group "$RESOURCE_GROUP" \
        --image "${ACR_LOGIN_SERVER}/thewatch/dashboard-api:${IMAGE_TAG}" \
        --set-env-vars "ASPNETCORE_ENVIRONMENT=$(echo "$ENVIRONMENT" | sed 's/./\U&/' | sed 's/staging/Staging/;s/production/Production/')" \
        2>/dev/null || \
      az containerapp create \
        --name "${BASE_NAME}-api" \
        --resource-group "$RESOURCE_GROUP" \
        --environment "$CONTAINER_ENV" \
        --image "${ACR_LOGIN_SERVER}/thewatch/dashboard-api:${IMAGE_TAG}" \
        --registry-server "$ACR_LOGIN_SERVER" \
        --target-port 8080 \
        --ingress external \
        --min-replicas 1 \
        --max-replicas 3 \
        --cpu 0.5 \
        --memory 1Gi \
        --env-vars "ASPNETCORE_ENVIRONMENT=Staging" "ASPNETCORE_URLS=http://+:8080"

      # Functions — internal ingress
      az containerapp update \
        --name "${BASE_NAME}-functions" \
        --resource-group "$RESOURCE_GROUP" \
        --image "${ACR_LOGIN_SERVER}/thewatch/functions:${IMAGE_TAG}" \
        2>/dev/null || \
      az containerapp create \
        --name "${BASE_NAME}-functions" \
        --resource-group "$RESOURCE_GROUP" \
        --environment "$CONTAINER_ENV" \
        --image "${ACR_LOGIN_SERVER}/thewatch/functions:${IMAGE_TAG}" \
        --registry-server "$ACR_LOGIN_SERVER" \
        --target-port 80 \
        --ingress internal \
        --min-replicas 1 \
        --max-replicas 3 \
        --cpu 0.5 \
        --memory 1Gi

      # WorkerServices — internal ingress
      az containerapp update \
        --name "${BASE_NAME}-worker" \
        --resource-group "$RESOURCE_GROUP" \
        --image "${ACR_LOGIN_SERVER}/thewatch/worker:${IMAGE_TAG}" \
        2>/dev/null || \
      az containerapp create \
        --name "${BASE_NAME}-worker" \
        --resource-group "$RESOURCE_GROUP" \
        --environment "$CONTAINER_ENV" \
        --image "${ACR_LOGIN_SERVER}/thewatch/worker:${IMAGE_TAG}" \
        --registry-server "$ACR_LOGIN_SERVER" \
        --target-port 8080 \
        --ingress internal \
        --min-replicas 1 \
        --max-replicas 2 \
        --cpu 0.5 \
        --memory 1Gi

      # Health check verification
      API_FQDN=$(az containerapp show \
        --name "${BASE_NAME}-api" \
        --resource-group "$RESOURCE_GROUP" \
        --query "properties.configuration.ingress.fqdn" -o tsv 2>/dev/null || echo "")

      if [[ -n "$API_FQDN" ]]; then
        echo "Verifying health at https://${API_FQDN}/health ..."
        for i in $(seq 1 10); do
          HTTP_STATUS=$(curl -s -o /dev/null -w "%{http_code}" "https://${API_FQDN}/health" 2>/dev/null || echo "000")
          if [[ "$HTTP_STATUS" == "200" ]]; then
            echo "Health check passed (HTTP $HTTP_STATUS) on attempt $i."
            break
          fi
          echo "Attempt $i/10: HTTP $HTTP_STATUS. Waiting 15s..."
          sleep 15
        done
      fi

      echo "Container Apps deployment complete for ${ENVIRONMENT}."
    fi
  fi
fi

# ── Summary ───────────────────────────────────────────────────────────────────
echo ""
echo "══════════════════════════════════════════════════════════════"
echo "  Deployment Complete"
echo "══════════════════════════════════════════════════════════════"
echo "  SignalR     : $SIGNALR_HOST"
echo "  Redis       : $REDIS_HOST:$REDIS_PORT"
echo "  RabbitMQ    : $RABBIT_FQDN"
echo "  SQL Server  : $SQL_FQDN"
echo "  OpenAI      : $OPENAI_ENDPOINT"
echo "  .env        : $ENV_FILE"
echo "  Secrets     : Dashboard.Api, Dashboard.Web"
echo "  Environment : $ENVIRONMENT"
if $DEPLOY_CONTAINERS; then
echo "  ACR         : $ACR_LOGIN_SERVER"
echo "  Containers  : dashboard-api, functions, worker"
fi
echo "══════════════════════════════════════════════════════════════"
