// ============================================================================
// DatabaseEnvironment.cs — Target deployment environment for data providers.
//
// Write-Ahead Log Note:
//   Environment selection drives WAL behaviour. Development uses emulators
//   (Firestore emulator, Cosmos emulator) whose WAL is ephemeral.
//   Production targets durable, replicated WAL (SQL Server transaction log,
//   Postgres pg_wal, Cosmos change feed, Firestore real-time listeners).
//
// Example:
//   var env = DatabaseEnvironment.Development;
//   if (env == DatabaseEnvironment.Development) { /* use emulators */ }
// ============================================================================

namespace TheWatch.Data.Configuration;

/// <summary>
/// Represents the target deployment environment for database configuration.
/// Controls connection strings, emulator usage, and logging verbosity.
/// </summary>
public enum DatabaseEnvironment
{
    /// <summary>
    /// Local development — emulators enabled, verbose logging, seed data loaded.
    /// </summary>
    Development = 0,

    /// <summary>
    /// Integration / QA testing — isolated databases, test fixtures, CI-friendly.
    /// </summary>
    Test = 1,

    /// <summary>
    /// Live production — durable storage, encrypted connections, minimal logging.
    /// </summary>
    Production = 2
}
