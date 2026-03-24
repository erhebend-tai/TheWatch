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
using TheWatch.Data.Adapters.CosmosDb;
using TheWatch.Data.Adapters.Firebase;
using TheWatch.Data.Adapters.Firestore;
using TheWatch.Data.Adapters.Mock;
using TheWatch.Data.Adapters.PostgreSql;
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
        // In Live mode, register multiple embedding ports for multi-provider fan-out:
        //   "AzureOpenAI" → AzureOpenAIEmbeddingAdapter (text-embedding-3-large, 3072 dims)
        //   "Gemini"      → GeminiEmbeddingAdapter (text-embedding-004, 768 dims)
        //   "VoyageAI"    → VoyageAIEmbeddingAdapter (voyage-3, 1024 dims)
        //   "All"         → Register all three for full multi-provider RAG
        switch (registry.Embedding)
        {
            // Future: case "AzureOpenAI": services.AddSingleton<IEmbeddingPort, AzureOpenAIEmbeddingAdapter>(); break;
            // Future: case "Gemini": services.AddSingleton<IEmbeddingPort, GeminiEmbeddingAdapter>(); break;
            // Future: case "VoyageAI": services.AddSingleton<IEmbeddingPort, VoyageAIEmbeddingAdapter>(); break;
            // Future: case "All":
            //     services.AddSingleton<IEmbeddingPort, AzureOpenAIEmbeddingAdapter>();
            //     services.AddSingleton<IEmbeddingPort, GeminiEmbeddingAdapter>();
            //     services.AddSingleton<IEmbeddingPort, VoyageAIEmbeddingAdapter>();
            //     break;
            default: // "Mock"
                services.AddSingleton<IEmbeddingPort, MockEmbeddingAdapter>();
                break;
        }

        // --- IVectorSearchPort ---
        // In Live mode, register multiple vector stores for multi-provider fan-out:
        //   "CosmosDB"   → CosmosDB DiskANN vector index (paired with Azure OpenAI)
        //   "Firestore"  → Firestore KNN vector search (paired with Gemini)
        //   "Qdrant"     → Qdrant HNSW vector search (paired with VoyageAI/Claude)
        //   "All"        → Register all three for full multi-provider RAG
        switch (registry.VectorSearch)
        {
            // Future: case "CosmosDB": services.AddSingleton<IVectorSearchPort, CosmosDBVectorAdapter>(); break;
            // Future: case "Firestore": services.AddSingleton<IVectorSearchPort, FirestoreVectorAdapter>(); break;
            // Future: case "Qdrant": services.AddSingleton<IVectorSearchPort, QdrantVectorAdapter>(); break;
            // Future: case "All":
            //     services.AddSingleton<IVectorSearchPort, CosmosDBVectorAdapter>();
            //     services.AddSingleton<IVectorSearchPort, FirestoreVectorAdapter>();
            //     services.AddSingleton<IVectorSearchPort, QdrantVectorAdapter>();
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
}
