# ═══════════════════════════════════════════════════════════════════════════════
# TheWatch.Dashboard.Api — Multi-Stage Dockerfile
# ═══════════════════════════════════════════════════════════════════════════════
# Builds the ASP.NET Core Web API for the TheWatch command center.
#
# Build context must be the solution root (not the project directory) so that
# ProjectReferences to Shared, Data, Adapters.Mock, ServiceDefaults resolve.
#
# Usage:
#   docker build -f TheWatch.Dashboard.Api/Dockerfile -t thewatch-api .
#   docker run -p 8080:8080 thewatch-api
#
# Environment variables at runtime:
#   ASPNETCORE_URLS              — Defaults to http://+:8080
#   ASPNETCORE_ENVIRONMENT       — Development | Staging | Production
#   ConnectionStrings__Redis     — Redis connection string
#   ConnectionStrings__Hangfire  — SQL Server connection string
#   Azure__SignalR__ConnectionString — Azure SignalR connection string
#   Azure__OpenAI__Endpoint      — Azure OpenAI endpoint URL
#   Azure__OpenAI__ApiKey        — Azure OpenAI API key
#   RabbitMQ__Host               — RabbitMQ hostname
#
# Health check: GET /health (returns 200 when healthy)
# ═══════════════════════════════════════════════════════════════════════════════

# ── Stage 1: Build ────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src

# Copy solution-level files first for layer caching
COPY Directory.Build.props Directory.Packages.props global.json TheWatch.slnx ./
COPY *.props ./

# Copy csproj files for restore (layer caching — only re-restore when csproj changes)
COPY TheWatch.Dashboard.Api/TheWatch.Dashboard.Api.csproj TheWatch.Dashboard.Api/
COPY TheWatch.Shared/TheWatch.Shared.csproj TheWatch.Shared/
COPY TheWatch.Data/TheWatch.Data.csproj TheWatch.Data/
COPY TheWatch.Adapters.Mock/TheWatch.Adapters.Mock.csproj TheWatch.Adapters.Mock/
COPY TheWatch.ServiceDefaults/TheWatch.ServiceDefaults.csproj TheWatch.ServiceDefaults/

# Restore dependencies (cached unless csproj/props change)
RUN dotnet restore TheWatch.Dashboard.Api/TheWatch.Dashboard.Api.csproj

# Copy remaining source (invalidates cache only when source changes)
COPY TheWatch.Dashboard.Api/ TheWatch.Dashboard.Api/
COPY TheWatch.Shared/ TheWatch.Shared/
COPY TheWatch.Data/ TheWatch.Data/
COPY TheWatch.Adapters.Mock/ TheWatch.Adapters.Mock/
COPY TheWatch.ServiceDefaults/ TheWatch.ServiceDefaults/

# Build
RUN dotnet build TheWatch.Dashboard.Api/TheWatch.Dashboard.Api.csproj \
    -c Release \
    --no-restore

# ── Stage 2: Publish ──────────────────────────────────────────────────────────
FROM build AS publish
RUN dotnet publish TheWatch.Dashboard.Api/TheWatch.Dashboard.Api.csproj \
    -c Release \
    --no-build \
    -o /app/publish \
    /p:UseAppHost=false

# ── Stage 3: Runtime ──────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS runtime

# Security: run as non-root user
RUN groupadd -r thewatch && useradd -r -g thewatch -s /sbin/nologin thewatch

WORKDIR /app
COPY --from=publish /app/publish .

# Expose the default ASP.NET Core port
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# Health check — curl the /health endpoint every 30s
# Container orchestrators (Docker Compose, Kubernetes, Azure Container Apps) use this
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD curl -sf http://localhost:8080/health || exit 1

# Run as non-root
USER thewatch

ENTRYPOINT ["dotnet", "TheWatch.Dashboard.Api.dll"]
