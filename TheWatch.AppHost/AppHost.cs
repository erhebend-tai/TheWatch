// =============================================================================
// TheWatch.AppHost — Aspire Orchestration Entry Point
// =============================================================================
// Provisions all infrastructure resources and wires connection strings into
// application projects. Running this single project starts the entire stack.
//
// Resource Topology:
//   ┌─────────────┐  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐
//   │  SQL Server  │  │ PostgreSQL  │  │    Redis     │  │  Cosmos DB  │  │  RabbitMQ   │
//   │ (primary DB) │  │ (PostGIS)   │  │   (cache)    │  │(audit trail)│  │ (messaging) │
//   └──────┬───────┘  └──────┬──────┘  └──────┬───────┘  └──────┬──────┘  └──────┬──────┘
//          │                 │                 │                 │                 │
//          └────────┬────────┴────────┬────────┘                │                 │
//                   ▼                 ▼                          │                 │
//          ┌────────────────┐  ┌─────────────────┐              │                 │
//          │ Dashboard API  │←─│  Dashboard Web   │              │                 │
//          │ (ASP.NET Core) │  │  (Blazor SSR)    │              │                 │
//          └────────────────┘  └─────────────────┘              │                 │
//                                                               │                 │
//          ┌────────────────────────────────────────────────────┴─────────────────┘
//          │ DocGen Worker (Hangfire + Roslyn)
//          │  - Watches .cs files → publishes to RabbitMQ
//          │  - Consumes file changes → Hangfire jobs
//          │  - Full scan every 15 min
//          └────────────────────────────────────────────────────────────────────────
//
// WAL: Resource creation order is declarative — Aspire resolves the dependency
//      graph from .WaitFor() calls and starts resources in topological order.
//
// Example — running the host:
//   cd TheWatch.AppHost && dotnet run
//   # Opens https://localhost:15000 (Aspire Dashboard)
//   # SQL Server: localhost,14330 | PostgreSQL: localhost,5432
//   # Redis: localhost:6379 | Cosmos DB emulator: https://localhost:8081
//   # RabbitMQ: localhost:5672 (management UI: localhost:15672)
// =============================================================================

var builder = DistributedApplication.CreateBuilder(args);

// ─────────────────────────────────────────────────────────────
// Infrastructure Resources
// ─────────────────────────────────────────────────────────────

// SQL Server — primary relational store for WorkItems, Milestones, etc.
var sqlServer = builder.AddSqlServer("thewatch-sqlserver")
    .WithDataVolume("thewatch-sqlserver-data")
    .AddDatabase("TheWatchDb");

// PostgreSQL — spatial queries via PostGIS extension for volunteer proximity.
var postgres = builder.AddPostgres("thewatch-postgresql")
    .WithDataVolume("thewatch-postgresql-data")
    .WithPgAdmin()
    .AddDatabase("TheWatchSpatialDb");

// Redis — distributed cache for session state, SignalR backplane, geohash lookups.
var redis = builder.AddRedis("thewatch-redis")
    .WithDataVolume("thewatch-redis-data")
    .WithRedisInsight();

// Azure Cosmos DB — audit trail with hash-chain integrity, change feed for real-time.
var cosmos = builder.AddAzureCosmosDB("thewatch-cosmos")
    .RunAsEmulator()
    .AddDatabase("TheWatchAuditDb");

// RabbitMQ — message broker for file-change events (DocGen), SOS alert fanout,
// volunteer notification dispatch, and offline queue replay.
var rabbitmq = builder.AddRabbitMQ("thewatch-rabbitmq")
    .WithDataVolume("thewatch-rabbitmq-data")
    .WithManagementPlugin();

// Firestore Emulator — local Firestore for swarm inventory, agent activities.
// Uses the gcloud CLI image with the emulator component.
// Port 8080 is the Firestore emulator default.
var firestoreEmulator = builder.AddContainer("thewatch-firestore", "gcr.io/google.com/cloudsdktool/google-cloud-cli", "latest")
    .WithEntrypoint("gcloud")
    .WithArgs("emulators", "firestore", "start", "--host-port=0.0.0.0:8080", "--project=thewatch-dev")
    .WithHttpEndpoint(port: 8080, targetPort: 8080, name: "firestore");

// Qdrant — open-source vector database for Claude/VoyageAI embeddings.
var qdrant = builder.AddContainer("thewatch-qdrant", "qdrant/qdrant")
    .WithVolume("thewatch-qdrant-data", "/qdrant/storage")
    .WithHttpEndpoint(port: 6333, targetPort: 6333, name: "rest")
    .WithEndpoint(port: 6334, targetPort: 6334, name: "grpc");

// ─────────────────────────────────────────────────────────────
// Original Aspire Template Projects
// ─────────────────────────────────────────────────────────────

// API Service — original template backend with health checks.
var apiService = builder.AddProject<Projects.TheWatch_ApiService>("apiservice")
    .WithReference(sqlServer)
    .WithReference(redis)
    .WaitFor(sqlServer)
    .WaitFor(redis)
    .WithHttpHealthCheck("/health");

// Web Frontend — Blazor SSR with cache and API references.
builder.AddProject<Projects.TheWatch_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(redis)
    .WaitFor(redis)
    .WithReference(apiService)
    .WaitFor(apiService);

// Worker Services — background processing.
builder.AddProject<Projects.TheWatch_WorkerServices>("thewatch-workerservices")
    .WithReference(rabbitmq)
    .WaitFor(rabbitmq);

// ─────────────────────────────────────────────────────────────
// Agent-Delivered Projects
// ─────────────────────────────────────────────────────────────

// Dashboard API — the main backend. Receives all infrastructure connections.
// Hosts SignalR hub, REST controllers, and adapter registry.
var dashboardApi = builder
    .AddProject<Projects.TheWatch_Dashboard_Api>("dashboard-api")
    .WithReference(sqlServer)
    .WithReference(postgres)
    .WithReference(redis)
    .WithReference(cosmos)
    .WithReference(rabbitmq)
    .WithReference(qdrant.GetEndpoint("rest"))
    .WithReference(firestoreEmulator.GetEndpoint("firestore"))
    .WaitFor(sqlServer)
    .WaitFor(postgres)
    .WaitFor(redis)
    .WaitFor(cosmos)
    .WaitFor(rabbitmq)
    .WaitFor(qdrant)
    .WaitFor(firestoreEmulator)
    .WithEnvironment("DatabaseSettings__UseFirestoreEmulator", "true")
    .WithEnvironment("DatabaseSettings__FirestoreEmulatorHost", firestoreEmulator.GetEndpoint("firestore"))
    .WithEnvironment("DatabaseSettings__FirestoreProjectId", "thewatch-dev")
    .WithExternalHttpEndpoints();

// Dashboard Web — Blazor SSR frontend. References the API for service discovery.
builder
    .AddProject<Projects.TheWatch_Dashboard_Web>("dashboard-web")
    .WithReference(dashboardApi)
    .WithReference(redis)
    .WaitFor(dashboardApi)
    .WithExternalHttpEndpoints();

// DocGen Worker — XML documentation generator.
// Watches .cs files via FileSystemWatcher → publishes to RabbitMQ → Hangfire processes.
builder
    .AddProject<Projects.TheWatch_DocGen>("docgen-worker")
    .WithReference(rabbitmq)
    .WaitFor(rabbitmq)
    .WithEnvironment("DocGen__SolutionRoot", builder.AppHostDirectory + "/..");

// Azure Functions — event-driven response coordination.
builder
    .AddAzureFunctionsProject<Projects.TheWatch_Functions>("response-functions")
    .WithReference(rabbitmq)
    .WithReference(redis)
    .WaitFor(rabbitmq)
    .WithExternalHttpEndpoints();

// Build Server — LSIF code intelligence + LSP protocol + build orchestration.
var buildServer = builder
    .AddProject<Projects.TheWatch_BuildServer>("build-server")
    .WithReference(dashboardApi)
    .WaitFor(dashboardApi)
    .WithExternalHttpEndpoints();

// ─────────────────────────────────────────────────────────────
// Notes on Non-Orchestrated Clients
// ─────────────────────────────────────────────────────────────
// TheWatch.Maui — MAUI apps cannot be orchestrated by Aspire directly.
//   They connect to Dashboard API via HTTP/SignalR at runtime.
//
// TheWatch.Cli — Terminal.Gui TUI dashboard with embedded terminals.
//   Not orchestrated by Aspire (it IS the developer's terminal).
//   Run separately: dotnet run --project TheWatch.Cli
//
// AI Provider Stacks (embedding → vector store):
//   - Azure OpenAI text-embedding-3-large → CosmosDB vector search (DiskANN)
//   - Google Gemini text-embedding-004 → Firestore KNN vector search
//   - Voyage AI voyage-3 → Qdrant HNSW vector search (for Claude context)
//
// API key configuration (set via environment or user secrets):
//   AZURE_OPENAI_ENDPOINT, AZURE_OPENAI_API_KEY
//   GOOGLE_AI_API_KEY (Gemini)
//   VOYAGE_API_KEY
//   ANTHROPIC_API_KEY (Claude chat, not embeddings)

builder.Build().Run();
