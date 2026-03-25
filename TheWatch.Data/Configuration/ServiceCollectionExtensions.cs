// =============================================================================
// ServiceCollectionExtensions.cs — Single entry point for data layer DI registration.
// =============================================================================
// Usage:
//   builder.Services.AddTheWatchDataLayer(builder.Configuration);
//
// Aspire Integration:
//   When running under Aspire, the DbContext and CosmosClient are already registered
//   by Aspire client integrations (e.g., builder.AddSqlServerDbContext<TheWatchDbContext>).
//   This method detects pre-existing registrations and skips re-registration.
//
//   Connection string resolution order:
//     1. Aspire-injected ConnectionStrings:thewatch-sqlserver (set by AppHost)
//     2. DatabaseSettings:SqlServerConnectionString (manual config)
//     3. Fallback to Mock adapter (no database needed)
//
// WAL: Adapter selection is logged at startup via ILogger. Check logs for
//      "[WAL-DI] Registered IStorageService = SqlServerStorageAdapter" etc.
//
// Example — standalone (no Aspire):
//   "DatabaseSettings": {
//     "SqlServerConnectionString": "Server=localhost;Database=TheWatch;Trusted_Connection=True"
//   }
//
// Example — with Aspire (connection strings injected automatically):
//   // No manual connection strings needed — AppHost provides them.
//   builder.AddSqlServerDbContext<TheWatchDbContext>("thewatch-sqlserver");
//   builder.Services.AddTheWatchDataLayer(builder.Configuration);
// =============================================================================

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
// Microsoft.Extensions.Configuration.Binder provides Bind() extension methods via package reference
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
// Microsoft.Extensions.Http provides AddHttpClient() extension methods via package reference
using Azure;
using Azure.AI.OpenAI;
using Qdrant.Client;
using TheWatch.Data.Adapters.AzureOpenAI;
using TheWatch.Data.Adapters.CosmosDb;
using TheWatch.Data.Adapters.Firebase;
using TheWatch.Data.Adapters.Firestore;
using TheWatch.Data.Adapters.Mock;
using TheWatch.Data.Adapters.PostgreSql;
using TheWatch.Data.Adapters.Qdrant;
using TheWatch.Data.Adapters.Sqlite;
using TheWatch.Data.Adapters.SqlServer;
using TheWatch.Data.Context;
using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Data.Configuration;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers data-layer adapters (IStorageService, IAuditTrail, ISpatialIndex) based on
    /// the AdapterRegistry configuration section. Aspire-aware: skips DbContext registration
    /// when Aspire has already provided it.
    /// </summary>
    public static IServiceCollection AddTheWatchDataLayer(this IServiceCollection services, IConfiguration configuration)
    {
        var registry = new TheWatch.Shared.Configuration.AdapterRegistry();
        configuration.GetSection(TheWatch.Shared.Configuration.AdapterRegistry.SectionName).Bind(registry);

        var dbSettings = new DatabaseSettings();
        configuration.GetSection("DatabaseSettings").Bind(dbSettings);

        // Resolve connection strings: Aspire ConnectionStrings > DatabaseSettings > empty
        var sqlConnStr = configuration.GetConnectionString("thewatch-sqlserver")
            ?? dbSettings.SqlServerConnectionString;
        var pgConnStr = configuration.GetConnectionString("thewatch-postgresql")
            ?? dbSettings.PostgresConnectionString;
        var cosmosConnStr = configuration.GetConnectionString("thewatch-cosmos")
            ?? dbSettings.CosmosDbConnectionString;

        // --- IStorageService ---
        switch (registry.PrimaryStorage)
        {
            case "SqlServer":
                EnsureDbContext(services, sqlConnStr, "SqlServer");
                services.AddScoped<IStorageService, SqlServerStorageAdapter>();
                break;
            case "PostgreSql":
                EnsureDbContext(services, pgConnStr, "PostgreSql");
                services.AddScoped<IStorageService, PostgreSqlStorageAdapter>();
                break;
            case "CosmosDb":
                EnsureCosmosClient(services, cosmosConnStr);
                services.AddScoped<IStorageService>(sp =>
                    new CosmosDbStorageAdapter(sp.GetRequiredService<Microsoft.Azure.Cosmos.CosmosClient>()));
                break;
            case "Firebase":
                services.AddHttpClient<FirebaseStorageAdapter>();
                services.AddScoped<IStorageService>(sp =>
                    new FirebaseStorageAdapter(sp.GetRequiredService<HttpClient>(), dbSettings.FirebaseProjectId));
                break;
            case "Firestore":
                EnsureFirestore(services, dbSettings);
                services.AddScoped<IStorageService, FirestoreStorageAdapter>();
                break;
            default: // "Mock"
                services.AddSingleton<IStorageService, MockStorageAdapter>();
                break;
        }

        // --- IAuditTrail ---
        switch (registry.AuditTrail)
        {
            case "SqlServer":
                EnsureDbContext(services, sqlConnStr, "SqlServer");
                services.AddScoped<IAuditTrail, SqlServerAuditTrailAdapter>();
                break;
            case "PostgreSql":
                EnsureDbContext(services, pgConnStr, "PostgreSql");
                services.AddScoped<IAuditTrail, PostgreSqlAuditTrailAdapter>();
                break;
            case "CosmosDb":
                EnsureCosmosClient(services, cosmosConnStr);
                services.AddScoped<IAuditTrail>(sp =>
                    new CosmosDbAuditTrailAdapter(sp.GetRequiredService<Microsoft.Azure.Cosmos.CosmosClient>()));
                break;
            default: // "Mock"
                services.AddSingleton<IAuditTrail, MockAuditTrailAdapter>();
                break;
        }

        // --- ISpatialIndex ---
        switch (registry.SpatialIndex)
        {
            case "PostgreSql":
                EnsureDbContext(services, pgConnStr, "PostgreSql");
                services.AddScoped<ISpatialIndex, PostgreSqlSpatialAdapter>();
                break;
            default: // "Mock"
                services.AddSingleton<ISpatialIndex, MockSpatialIndexAdapter>();
                break;
        }

        // --- IBlobStoragePort ---
        switch (registry.BlobStorage)
        {
            // Future: case "AzureBlob": services.AddScoped<IBlobStoragePort, AzureBlobStorageAdapter>(); break;
            // Future: case "S3": services.AddScoped<IBlobStoragePort, S3BlobStorageAdapter>(); break;
            // Future: case "GCS": services.AddScoped<IBlobStoragePort, GcsBlobStorageAdapter>(); break;
            default: // "Mock"
                services.AddSingleton<IBlobStoragePort, MockBlobStorageAdapter>();
                break;
        }

        // --- IEvidencePort ---
        switch (registry.Evidence)
        {
            // Future: case "SqlServer": services.AddScoped<IEvidencePort, SqlServerEvidenceAdapter>(); break;
            default: // "Mock"
                services.AddSingleton<IEvidencePort, MockEvidenceAdapter>();
                break;
        }

        // --- ISurveyPort ---
        switch (registry.Survey)
        {
            // Future: case "SqlServer": services.AddScoped<ISurveyPort, SqlServerSurveyAdapter>(); break;
            default: // "Mock"
                services.AddSingleton<ISurveyPort, MockSurveyAdapter>();
                break;
        }

        // --- IFeatureTrackingPort ---
        switch (registry.FeatureTracking)
        {
            // Future: case "Firestore": services.AddScoped<IFeatureTrackingPort, FirestoreFeatureTrackingAdapter>(); break;
            default: // "Mock"
                services.AddSingleton<IFeatureTrackingPort, MockFeatureTrackingAdapter>();
                break;
        }

        // --- IDevWorkPort ---
        switch (registry.DevWork)
        {
            // Future: case "Firestore": services.AddScoped<IDevWorkPort, FirestoreDevWorkAdapter>(); break;
            default: // "Mock"
                services.AddSingleton<IDevWorkPort, MockDevWorkAdapter>();
                break;
        }

        // --- IEmbeddingPort ---
        // Multi-provider fan-out: each embedding provider pairs with a native vector store.
        //   "AzureOpenAI" → text-embedding-3-large (3072 dims) → CosmosDB DiskANN
        //   "Gemini"      → text-embedding-004 (768 dims) → Firestore KNN [future]
        //   "VoyageAI"    → voyage-3 (1024 dims) → Qdrant HNSW [future]
        //   "All"         → Register all providers for full multi-provider RAG
        switch (registry.Embedding)
        {
            case "AzureOpenAI":
                EnsureAzureOpenAIEmbedding(services, configuration);
                break;
            // Future: case "Gemini": EnsureGeminiEmbedding(services, configuration); break;
            // Future: case "VoyageAI": EnsureVoyageAIEmbedding(services, configuration); break;
            // Future: case "All":
            //     EnsureAzureOpenAIEmbedding(services, configuration);
            //     EnsureGeminiEmbedding(services, configuration);
            //     EnsureVoyageAIEmbedding(services, configuration);
            //     break;
            default: // "Mock"
                services.AddSingleton<IEmbeddingPort, MockEmbeddingAdapter>();
                break;
        }

        // --- IWatchCallPort ---
        switch (registry.WatchCall)
        {
            // Future: case "Firestore": EnsureFirestore(services, dbSettings);
            //     services.AddScoped<IWatchCallPort, FirestoreWatchCallAdapter>(); break;
            default: // "Mock" — registered by TheWatch.Adapters.Mock.AddMockAdapters()
                break;
        }

        // --- ISceneNarrationPort ---
        switch (registry.SceneNarration)
        {
            // Future: case "AzureOpenAI": EnsureAzureOpenAISceneNarration(services, configuration); break;
            // Future: case "Gemini": EnsureGeminiSceneNarration(services, configuration); break;
            default: // "Mock" — registered by TheWatch.Adapters.Mock.AddMockAdapters()
                break;
        }

        // --- ISwarmAgentPort ---
        switch (registry.SwarmAgent)
        {
            // Future: case "AzureOpenAI": EnsureAzureOpenAISwarmAgent(services, configuration); break;
            default: // "Mock" — registered by TheWatch.Adapters.Mock.AddMockAdapters()
                break;
        }

        // --- IVectorSearchPort ---
        // Multi-provider vector stores, each paired with its embedding provider:
        //   "CosmosDB"   → CosmosDB DiskANN vector index (paired with Azure OpenAI) [future]
        //   "Firestore"  → Firestore KNN vector search (paired with Gemini) [future]
        //   "Qdrant"     → Qdrant HNSW vector search (paired with VoyageAI/Claude)
        //   "All"        → Register all three for full multi-provider RAG
        switch (registry.VectorSearch)
        {
            // Future: case "CosmosDB": EnsureCosmosDBVector(services, cosmosConnStr); break;
            // Future: case "Firestore": EnsureFirestoreVector(services, dbSettings); break;
            case "Qdrant":
                EnsureQdrantVector(services, configuration);
                break;
            // Future: case "All":
            //     EnsureCosmosDBVector(services, cosmosConnStr);
            //     EnsureFirestoreVector(services, dbSettings);
            //     EnsureQdrantVector(services, configuration);
            //     break;
            default: // "Mock"
                services.AddSingleton<IVectorSearchPort, MockVectorSearchAdapter>();
                break;
        }

        return services;
    }

    /// <summary>
    /// Registers TheWatchDbContext only if Aspire hasn't already registered it.
    /// Aspire's AddSqlServerDbContext / AddNpgsqlDbContext register DbContext before
    /// this method runs, so we check for existing registration first.
    /// </summary>
    private static void EnsureDbContext(IServiceCollection services, string connectionString, string provider)
    {
        // If Aspire (or a previous call) already registered the DbContext, skip.
        if (services.Any(s => s.ServiceType == typeof(DbContextOptions<TheWatchDbContext>)))
            return;

        // Standalone mode — register DbContext with the resolved connection string.
        if (!string.IsNullOrEmpty(connectionString))
        {
            services.AddDbContext<TheWatchDbContext>(opt =>
            {
                if (provider == "PostgreSql")
                    opt.UseNpgsql(connectionString);
                else
                    opt.UseSqlServer(connectionString);
            });
        }
    }

    /// <summary>
    /// Registers CosmosClient only if not already registered by Aspire.
    /// </summary>
    private static void EnsureCosmosClient(IServiceCollection services, string connectionString)
    {
        if (services.Any(s => s.ServiceType == typeof(Microsoft.Azure.Cosmos.CosmosClient)))
            return;

        if (!string.IsNullOrEmpty(connectionString))
            services.AddSingleton(new Microsoft.Azure.Cosmos.CosmosClient(connectionString));
    }

    /// <summary>
    /// Registers FirestoreDb with emulator support. When UseFirestoreEmulator is true,
    /// sets the FIRESTORE_EMULATOR_HOST environment variable before creating the client,
    /// which tells the Google Cloud SDK to skip authentication and connect to the local emulator.
    /// </summary>
    private static void EnsureFirestore(IServiceCollection services, DatabaseSettings dbSettings)
    {
        if (services.Any(s => s.ServiceType == typeof(Google.Cloud.Firestore.FirestoreDb)))
            return;

        if (dbSettings.UseFirestoreEmulator && !string.IsNullOrEmpty(dbSettings.FirestoreEmulatorHost))
        {
            // The Google Cloud Firestore SDK checks this env var on client creation.
            // When set, it bypasses authentication and connects to the emulator.
            Environment.SetEnvironmentVariable("FIRESTORE_EMULATOR_HOST", dbSettings.FirestoreEmulatorHost);
        }

        var projectId = !string.IsNullOrEmpty(dbSettings.FirestoreProjectId)
            ? dbSettings.FirestoreProjectId
            : "thewatch-dev"; // Default project ID for emulator

        services.AddSingleton(Google.Cloud.Firestore.FirestoreDb.Create(projectId));
    }

    /// <summary>
    /// Registers the Azure OpenAI embedding adapter using AIProviders:AzureOpenAI config.
    /// Creates and registers AzureOpenAIClient singleton if not already present.
    /// </summary>
    private static void EnsureAzureOpenAIEmbedding(IServiceCollection services, IConfiguration configuration)
    {
        if (services.Any(s => s.ServiceType == typeof(IEmbeddingPort) &&
            s.ImplementationType == typeof(AzureOpenAIEmbeddingAdapter)))
            return;

        // Register AzureOpenAIClient singleton (shared by embedding + chat)
        if (!services.Any(s => s.ServiceType == typeof(AzureOpenAIClient)))
        {
            var endpoint = configuration["AIProviders:AzureOpenAI:Endpoint"];
            var apiKey = configuration["AIProviders:AzureOpenAI:ApiKey"];

            if (!string.IsNullOrEmpty(endpoint) && !string.IsNullOrEmpty(apiKey))
            {
                services.AddSingleton(new AzureOpenAIClient(
                    new Uri(endpoint),
                    new AzureKeyCredential(apiKey)));
            }
        }

        var deploymentName = configuration["AIProviders:AzureOpenAI:EmbeddingModel"] ?? "text-embedding-3-large";
        var dims = int.TryParse(configuration["AIProviders:AzureOpenAI:EmbeddingDimensions"], out var d) ? d : 3072;

        services.AddSingleton<IEmbeddingPort>(sp =>
            new AzureOpenAIEmbeddingAdapter(
                sp.GetRequiredService<AzureOpenAIClient>(),
                deploymentName,
                dims,
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AzureOpenAIEmbeddingAdapter>>()));
    }

    /// <summary>
    /// Registers the Qdrant vector search adapter using AIProviders:Qdrant config.
    /// Creates QdrantClient singleton if not already present.
    /// </summary>
    private static void EnsureQdrantVector(IServiceCollection services, IConfiguration configuration)
    {
        if (services.Any(s => s.ServiceType == typeof(IVectorSearchPort) &&
            s.ImplementationType == typeof(QdrantVectorAdapter)))
            return;

        // Register QdrantClient singleton
        if (!services.Any(s => s.ServiceType == typeof(QdrantClient)))
        {
            var endpoint = configuration["AIProviders:Qdrant:Endpoint"] ?? "http://localhost:6333";
            var grpcEndpoint = configuration["AIProviders:Qdrant:GrpcEndpoint"] ?? "http://localhost:6334";

            // QdrantClient uses gRPC by default
            var uri = new Uri(grpcEndpoint);
            services.AddSingleton(new QdrantClient(uri.Host, uri.Port));
        }

        var collectionName = configuration["AIProviders:Qdrant:CollectionName"] ?? "thewatch-vectors";

        services.AddSingleton<IVectorSearchPort>(sp =>
            new QdrantVectorAdapter(
                sp.GetRequiredService<QdrantClient>(),
                sp.GetRequiredService<IEmbeddingPort>(),
                collectionName,
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<QdrantVectorAdapter>>()));
    }

    /// <summary>
    /// Registers the Firestore-backed ISwarmInventoryPort adapter.
    /// Call this in addition to AddTheWatchDataLayer when the swarm dashboard is enabled.
    /// Works with both emulator and production Firestore (uses the same FirestoreDb singleton).
    /// </summary>
    public static IServiceCollection AddSwarmInventoryFirestore(this IServiceCollection services, IConfiguration configuration)
    {
        var dbSettings = new DatabaseSettings();
        configuration.GetSection("DatabaseSettings").Bind(dbSettings);

        // Ensure FirestoreDb is registered (idempotent)
        EnsureFirestore(services, dbSettings);

        services.AddScoped<ISwarmInventoryPort, TheWatch.Data.Adapters.Firestore.FirestoreSwarmInventoryAdapter>();

        return services;
    }

    /// <summary>
    /// Registers IAuthPort — Firebase in production, Mock in development.
    /// Firebase Auth uses the same FirebaseApp as Firestore (shared credential).
    /// The AdapterRegistry doesn't yet have an "Auth" field, so we check Firebase config directly.
    /// </summary>
    public static IServiceCollection AddAuthPort(this IServiceCollection services, IConfiguration configuration)
    {
        var dbSettings = new DatabaseSettings();
        configuration.GetSection("DatabaseSettings").Bind(dbSettings);

        // If we have a Firebase/Firestore project configured and we're NOT on the emulator,
        // use real Firebase Auth. Otherwise, use Mock.
        if (!dbSettings.UseFirestoreEmulator && !string.IsNullOrEmpty(dbSettings.FirestoreProjectId)
            && dbSettings.FirestoreProjectId != "thewatch-dev")
        {
            EnsureFirebaseApp(services, dbSettings);
            services.AddSingleton<IAuthPort, TheWatch.Data.Adapters.Firebase.FirebaseAuthAdapter>();
        }
        else
        {
            services.AddSingleton<IAuthPort, TheWatch.Data.Adapters.Mock.MockAuthAdapter>();
        }

        return services;
    }

    /// <summary>
    /// Registers FirebaseApp singleton (shared between Auth and other Firebase services).
    /// Uses GOOGLE_APPLICATION_CREDENTIALS env var or the credential file from config.
    /// </summary>
    private static void EnsureFirebaseApp(IServiceCollection services, DatabaseSettings dbSettings)
    {
        if (services.Any(s => s.ServiceType == typeof(FirebaseAdmin.FirebaseApp)))
            return;

        FirebaseAdmin.FirebaseApp app;

        if (!string.IsNullOrEmpty(dbSettings.FirestoreCredentialPath)
            && System.IO.File.Exists(dbSettings.FirestoreCredentialPath))
        {
            app = FirebaseAdmin.FirebaseApp.Create(new FirebaseAdmin.AppOptions
            {
                Credential = Google.Apis.Auth.OAuth2.GoogleCredential.FromFile(dbSettings.FirestoreCredentialPath),
                ProjectId = dbSettings.FirestoreProjectId
            });
        }
        else
        {
            // Falls back to GOOGLE_APPLICATION_CREDENTIALS env var or default credentials
            app = FirebaseAdmin.FirebaseApp.Create(new FirebaseAdmin.AppOptions
            {
                ProjectId = dbSettings.FirestoreProjectId
            });
        }

        services.AddSingleton(app);
    }
}
