# ═══════════════════════════════════════════════════════════════════════════════
# TheWatch.Functions — Multi-Stage Dockerfile (Azure Functions Isolated Worker)
# ═══════════════════════════════════════════════════════════════════════════════
# Containerized Azure Functions host for timer triggers, RabbitMQ triggers,
# and HTTP triggers used by the TheWatch swarm infrastructure.
#
# Build context must be the solution root so ProjectReferences resolve.
#
# Usage:
#   docker build -f TheWatch.Functions/Dockerfile -t thewatch-functions .
#   docker run -p 8081:80 thewatch-functions
#
# Environment variables at runtime:
#   AzureWebJobsStorage            — Azure Storage connection (or "UseDevelopmentStorage=true")
#   FUNCTIONS_WORKER_RUNTIME       — Set to "dotnet-isolated"
#   AZURE_OPENAI_ENDPOINT          — Azure OpenAI endpoint
#   AZURE_OPENAI_API_KEY           — Azure OpenAI API key
#   RabbitMQ__Host                 — RabbitMQ hostname
#   ConnectionStrings__Redis       — Redis connection string
# ═══════════════════════════════════════════════════════════════════════════════

# ── Stage 1: Build ────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src

# Copy solution-level files for restore caching
COPY Directory.Build.props Directory.Packages.props global.json TheWatch.slnx ./
COPY *.props ./

# Copy csproj files for restore layer caching
COPY TheWatch.Functions/TheWatch.Functions.csproj TheWatch.Functions/
COPY TheWatch.Shared/TheWatch.Shared.csproj TheWatch.Shared/
COPY TheWatch.Adapters.Mock/TheWatch.Adapters.Mock.csproj TheWatch.Adapters.Mock/
COPY TheWatch.ServiceDefaults/TheWatch.ServiceDefaults.csproj TheWatch.ServiceDefaults/

# Restore
RUN dotnet restore TheWatch.Functions/TheWatch.Functions.csproj

# Copy source
COPY TheWatch.Functions/ TheWatch.Functions/
COPY TheWatch.Shared/ TheWatch.Shared/
COPY TheWatch.Adapters.Mock/ TheWatch.Adapters.Mock/
COPY TheWatch.ServiceDefaults/ TheWatch.ServiceDefaults/

# Build
RUN dotnet build TheWatch.Functions/TheWatch.Functions.csproj \
    -c Release \
    --no-restore

# ── Stage 2: Publish ──────────────────────────────────────────────────────────
FROM build AS publish
RUN dotnet publish TheWatch.Functions/TheWatch.Functions.csproj \
    -c Release \
    --no-build \
    -o /app/publish \
    /p:UseAppHost=false

# ── Stage 3: Runtime ──────────────────────────────────────────────────────────
# Azure Functions uses a custom base image with the Functions host runtime
FROM mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated10.0-preview AS runtime

WORKDIR /home/site/wwwroot
COPY --from=publish /app/publish .

ENV AzureFunctionsJobHost__Logging__Console__IsEnabled=true
ENV FUNCTIONS_WORKER_RUNTIME=dotnet-isolated

EXPOSE 80

HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
    CMD curl -sf http://localhost:80/api/health || exit 1
