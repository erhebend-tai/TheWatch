// BuildOutputStore — which database the build outputs are persisted to.
// SQLite is the minimum / default. User selects via AdapterRegistry.BuildOutput.
// Example: AdapterRegistry.BuildOutput = "Sqlite" in appsettings.json

namespace TheWatch.Shared.Enums;

public enum BuildOutputStore
{
    /// <summary>SQLite — local file database. Zero infrastructure. Default.</summary>
    Sqlite = 0,

    /// <summary>SQL Server — via Aspire thewatch-sqlserver resource.</summary>
    SqlServer = 1,

    /// <summary>PostgreSQL — via Aspire thewatch-postgresql resource.</summary>
    PostgreSql = 2,

    /// <summary>Azure CosmosDB — via Aspire thewatch-cosmos resource.</summary>
    CosmosDB = 3,

    /// <summary>Google Firestore — via Firestore adapter.</summary>
    Firestore = 4,

    /// <summary>In-memory mock for testing.</summary>
    Mock = 99
}
