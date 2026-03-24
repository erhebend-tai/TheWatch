// ============================================================================
// DatabaseSettings.cs — Unified configuration POCO for every data provider.
//
// Write-Ahead Log Note:
//   Each provider has its own WAL strategy. This settings class controls
//   which providers are active and whether they point to emulators or
//   production endpoints. Changing these values at runtime is NOT safe
//   without re-initialising the corresponding provider.
//
// Example (appsettings.json):
//   {
//     "DatabaseSettings": {
//       "Environment": "Development",
//       "FirebaseProjectId": "thewatch-dev",
//       "FirebaseCredentialPath": "./secrets/firebase-sa.json",
//       "FirestoreProjectId": "thewatch-dev",
//       "FirestoreCredentialPath": "./secrets/firestore-sa.json",
//       "CosmosDbConnectionString": "AccountEndpoint=https://localhost:8081/;...",
//       "CosmosDbDatabaseName": "TheWatchDb",
//       "SqlServerConnectionString": "Server=localhost;Database=TheWatch;...",
//       "PostgresConnectionString": "Host=localhost;Database=thewatch;...",
//       "UseFirebaseEmulator": true,
//       "FirebaseEmulatorHost": "localhost:9099",
//       "UseFirestoreEmulator": true,
//       "FirestoreEmulatorHost": "localhost:8080",
//       "UseCosmosDbEmulator": true
//     }
//   }
//
// Binding example (Program.cs / Startup.cs):
//   builder.Services.Configure<DatabaseSettings>(
//       builder.Configuration.GetSection("DatabaseSettings"));
// ============================================================================

namespace TheWatch.Data.Configuration;

/// <summary>
/// Holds all connection strings, project identifiers, credential paths, and
/// emulator flags for every supported data provider (Firebase, Firestore,
/// Cosmos DB, SQL Server, PostgreSQL).
/// Bind from <c>IConfiguration</c> section <c>"DatabaseSettings"</c>.
/// </summary>
public class DatabaseSettings
{
    /// <summary>
    /// The target deployment environment (Development, Test, Production).
    /// </summary>
    public DatabaseEnvironment Environment { get; set; } = DatabaseEnvironment.Development;

    // ── Firebase Authentication / Realtime Database ──────────────────────

    /// <summary>
    /// Google Cloud project ID used by Firebase Admin SDK.
    /// </summary>
    public string FirebaseProjectId { get; set; } = string.Empty;

    /// <summary>
    /// File-system path to the Firebase service-account JSON credential file.
    /// </summary>
    public string FirebaseCredentialPath { get; set; } = string.Empty;

    // ── Google Cloud Firestore ───────────────────────────────────────────

    /// <summary>
    /// Google Cloud project ID for the Firestore database.
    /// May differ from <see cref="FirebaseProjectId"/> in multi-project setups.
    /// </summary>
    public string FirestoreProjectId { get; set; } = string.Empty;

    /// <summary>
    /// File-system path to the Firestore service-account JSON credential file.
    /// </summary>
    public string FirestoreCredentialPath { get; set; } = string.Empty;

    // ── Azure Cosmos DB ──────────────────────────────────────────────────

    /// <summary>
    /// Full Cosmos DB connection string (AccountEndpoint + AccountKey or AAD token).
    /// </summary>
    public string CosmosDbConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Cosmos DB database name (e.g., <c>"TheWatchDb"</c>).
    /// </summary>
    public string CosmosDbDatabaseName { get; set; } = string.Empty;

    // ── SQL Server (EF Core) ─────────────────────────────────────────────

    /// <summary>
    /// ADO.NET connection string for SQL Server / Azure SQL.
    /// </summary>
    public string SqlServerConnectionString { get; set; } = string.Empty;

    // ── PostgreSQL (EF Core + Npgsql) ────────────────────────────────────

    /// <summary>
    /// Npgsql connection string for PostgreSQL / Azure Database for PostgreSQL.
    /// </summary>
    public string PostgresConnectionString { get; set; } = string.Empty;

    // ── Emulator Flags ───────────────────────────────────────────────────

    /// <summary>
    /// When <c>true</c>, the Firebase Admin SDK targets the local Auth emulator.
    /// </summary>
    public bool UseFirebaseEmulator { get; set; }

    /// <summary>
    /// Host and port of the Firebase Auth emulator (e.g., <c>"localhost:9099"</c>).
    /// </summary>
    public string FirebaseEmulatorHost { get; set; } = "localhost:9099";

    /// <summary>
    /// When <c>true</c>, the Firestore client targets the local Firestore emulator.
    /// </summary>
    public bool UseFirestoreEmulator { get; set; }

    /// <summary>
    /// Host and port of the Firestore emulator (e.g., <c>"localhost:8080"</c>).
    /// </summary>
    public string FirestoreEmulatorHost { get; set; } = "localhost:8080";

    /// <summary>
    /// When <c>true</c>, the Cosmos DB client targets the local Cosmos emulator
    /// (https://localhost:8081 with well-known key).
    /// </summary>
    public bool UseCosmosDbEmulator { get; set; }
}
