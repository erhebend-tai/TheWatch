// IBuildOutputPort — domain port for persisting build outputs, errors, and artifacts.
// NO database SDK imports allowed in this file. Adapters implement per-store:
//   - SqliteBuildOutputAdapter    → SQLite (default, zero infrastructure)
//   - SqlServerBuildOutputAdapter → SQL Server
//   - PostgreSqlBuildOutputAdapter → PostgreSQL
//   - CosmosDBBuildOutputAdapter  → CosmosDB
//   - FirestoreBuildOutputAdapter → Firestore
//   - MockBuildOutputAdapter      → In-memory for testing
//
// Build outputs flow: dotnet build → parse stdout/stderr → IBuildOutputPort.SaveAsync
// The user selects which store(s) via AdapterRegistry.BuildOutput in appsettings.json.
//
// Example:
//   await buildPort.SaveAsync(buildOutput, ct);
//   var errors = await buildPort.GetErrorsAsync("TheWatch.Dashboard.Api", last24h, ct);

using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Enums;

namespace TheWatch.Shared.Domain.Ports;

public interface IBuildOutputPort
{
    /// <summary>Which store this adapter persists to.</summary>
    BuildOutputStore Store { get; }

    /// <summary>Save a complete build output record.</summary>
    Task<StorageResult<BuildOutput>> SaveAsync(BuildOutput output, CancellationToken ct = default);

    /// <summary>Get a build output by ID.</summary>
    Task<StorageResult<BuildOutput>> GetByIdAsync(string buildId, CancellationToken ct = default);

    /// <summary>Get recent build outputs, optionally filtered by project.</summary>
    Task<StorageResult<List<BuildOutput>>> GetRecentAsync(int limit = 50, string? projectName = null, CancellationToken ct = default);

    /// <summary>Get all builds that failed within a date range.</summary>
    Task<StorageResult<List<BuildOutput>>> GetFailedAsync(DateTime since, string? projectName = null, CancellationToken ct = default);

    /// <summary>Get all diagnostics (errors/warnings) across builds within a date range.</summary>
    Task<StorageResult<List<BuildDiagnostic>>> GetDiagnosticsAsync(DateTime since, BuildOutputSeverity? severity = null, string? projectName = null, CancellationToken ct = default);

    /// <summary>Get build statistics: success rate, avg duration, error frequency.</summary>
    Task<StorageResult<BuildOutputStats>> GetStatsAsync(DateTime since, CancellationToken ct = default);

    /// <summary>Delete build outputs older than the specified date (retention cleanup).</summary>
    Task<StorageResult<int>> PurgeAsync(DateTime olderThan, CancellationToken ct = default);
}

/// <summary>Aggregated build statistics for the dashboard.</summary>
public class BuildOutputStats
{
    public int TotalBuilds { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public double SuccessRate { get; set; }
    public double AverageDurationMs { get; set; }
    public int TotalErrors { get; set; }
    public int TotalWarnings { get; set; }
    public Dictionary<string, int> ErrorsByProject { get; set; } = new();
    public Dictionary<string, int> TopErrorCodes { get; set; } = new();
}
