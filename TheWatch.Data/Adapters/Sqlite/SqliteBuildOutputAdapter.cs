// SqliteBuildOutputAdapter — EF Core SQLite-backed IBuildOutputPort.
// Zero infrastructure — stores to a local file (builds.db by default).
// Auto-creates the database and tables on first use via EnsureCreated.
//
// This is the default/minimum build output store. Users can switch to
// SQL Server, PostgreSQL, CosmosDB, or Firestore via AdapterRegistry.BuildOutput.
//
// Example:
//   services.AddDbContext<BuildOutputDbContext>(opt => opt.UseSqlite("Data Source=builds.db"));
//   services.AddScoped<IBuildOutputPort, SqliteBuildOutputAdapter>();

using Microsoft.EntityFrameworkCore;
using TheWatch.Data.Context;
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Domain.Ports;
using TheWatch.Shared.Enums;

namespace TheWatch.Data.Adapters.Sqlite;

public class SqliteBuildOutputAdapter : IBuildOutputPort
{
    public BuildOutputStore Store => BuildOutputStore.Sqlite;

    private readonly BuildOutputDbContext _db;

    public SqliteBuildOutputAdapter(BuildOutputDbContext db)
    {
        _db = db;
        _db.Database.EnsureCreated();
    }

    public async Task<StorageResult<BuildOutput>> SaveAsync(BuildOutput output, CancellationToken ct = default)
    {
        var entity = ToEntity(output);
        _db.BuildOutputs.Add(entity);
        await _db.SaveChangesAsync(ct);
        return StorageResult<BuildOutput>.Ok(output);
    }

    public async Task<StorageResult<BuildOutput>> GetByIdAsync(string buildId, CancellationToken ct = default)
    {
        var entity = await _db.BuildOutputs
            .Include(b => b.Diagnostics)
            .Include(b => b.Artifacts)
            .FirstOrDefaultAsync(b => b.Id == buildId, ct);

        if (entity is null)
            return StorageResult<BuildOutput>.Fail($"Build '{buildId}' not found");

        return StorageResult<BuildOutput>.Ok(FromEntity(entity));
    }

    public async Task<StorageResult<List<BuildOutput>>> GetRecentAsync(int limit = 50, string? projectName = null, CancellationToken ct = default)
    {
        var query = _db.BuildOutputs
            .Include(b => b.Diagnostics)
            .Include(b => b.Artifacts)
            .AsQueryable();

        if (!string.IsNullOrEmpty(projectName))
            query = query.Where(b => b.ProjectName == projectName);

        var entities = await query
            .OrderByDescending(b => b.StartedAt)
            .Take(limit)
            .ToListAsync(ct);

        return StorageResult<List<BuildOutput>>.Ok(entities.Select(FromEntity).ToList());
    }

    public async Task<StorageResult<List<BuildOutput>>> GetFailedAsync(DateTime since, string? projectName = null, CancellationToken ct = default)
    {
        var query = _db.BuildOutputs
            .Include(b => b.Diagnostics)
            .Where(b => !b.Succeeded && b.StartedAt >= since);

        if (!string.IsNullOrEmpty(projectName))
            query = query.Where(b => b.ProjectName == projectName);

        var entities = await query.OrderByDescending(b => b.StartedAt).ToListAsync(ct);
        return StorageResult<List<BuildOutput>>.Ok(entities.Select(FromEntity).ToList());
    }

    public async Task<StorageResult<List<BuildDiagnostic>>> GetDiagnosticsAsync(DateTime since, BuildOutputSeverity? severity = null, string? projectName = null, CancellationToken ct = default)
    {
        var query = _db.Diagnostics
            .Join(_db.BuildOutputs, d => d.BuildOutputId, b => b.Id, (d, b) => new { Diag = d, Build = b })
            .Where(x => x.Build.StartedAt >= since);

        if (severity.HasValue)
            query = query.Where(x => x.Diag.Severity == (int)severity.Value);
        if (!string.IsNullOrEmpty(projectName))
            query = query.Where(x => x.Build.ProjectName == projectName);

        var entities = await query.Select(x => x.Diag).OrderByDescending(d => d.Id).ToListAsync(ct);

        return StorageResult<List<BuildDiagnostic>>.Ok(entities.Select(e => new BuildDiagnostic
        {
            Id = e.Id,
            Severity = (BuildOutputSeverity)e.Severity,
            Code = e.Code,
            Message = e.Message,
            FilePath = e.FilePath,
            Line = e.Line,
            Column = e.Column,
            ProjectName = e.ProjectName
        }).ToList());
    }

    public async Task<StorageResult<BuildOutputStats>> GetStatsAsync(DateTime since, CancellationToken ct = default)
    {
        var builds = await _db.BuildOutputs.Where(b => b.StartedAt >= since).ToListAsync(ct);
        var diagnostics = await _db.Diagnostics
            .Join(_db.BuildOutputs, d => d.BuildOutputId, b => b.Id, (d, b) => new { Diag = d, Build = b })
            .Where(x => x.Build.StartedAt >= since)
            .Select(x => x.Diag)
            .ToListAsync(ct);

        var stats = new BuildOutputStats
        {
            TotalBuilds = builds.Count,
            SuccessCount = builds.Count(b => b.Succeeded),
            FailureCount = builds.Count(b => !b.Succeeded),
            SuccessRate = builds.Count > 0 ? (double)builds.Count(b => b.Succeeded) / builds.Count * 100 : 0,
            AverageDurationMs = builds.Count > 0 ? builds.Average(b => b.DurationMs) : 0,
            TotalErrors = diagnostics.Count(d => d.Severity >= (int)BuildOutputSeverity.Error),
            TotalWarnings = diagnostics.Count(d => d.Severity == (int)BuildOutputSeverity.Warning),
            ErrorsByProject = builds.Where(b => !b.Succeeded).GroupBy(b => b.ProjectName).ToDictionary(g => g.Key, g => g.Count()),
            TopErrorCodes = diagnostics.Where(d => d.Code != null).GroupBy(d => d.Code!).OrderByDescending(g => g.Count()).Take(10).ToDictionary(g => g.Key, g => g.Count())
        };

        return StorageResult<BuildOutputStats>.Ok(stats);
    }

    public async Task<StorageResult<int>> PurgeAsync(DateTime olderThan, CancellationToken ct = default)
    {
        var toDelete = await _db.BuildOutputs.Where(b => b.StartedAt < olderThan).ToListAsync(ct);
        _db.BuildOutputs.RemoveRange(toDelete);
        await _db.SaveChangesAsync(ct);
        return StorageResult<int>.Ok(toDelete.Count);
    }

    // ── Entity mapping ─────────────────────────────

    private static BuildOutputEntity ToEntity(BuildOutput output) => new()
    {
        Id = output.Id,
        ProjectName = output.ProjectName,
        Configuration = output.Configuration,
        TargetFramework = output.TargetFramework,
        Command = output.Command,
        TriggerSource = output.TriggerSource,
        Branch = output.Branch,
        CommitSha = output.CommitSha,
        ExitCode = output.ExitCode,
        Succeeded = output.Succeeded,
        Result = (int)output.Result,
        Stdout = output.Stdout,
        Stderr = output.Stderr,
        ErrorCount = output.ErrorCount,
        WarningCount = output.WarningCount,
        StartedAt = output.StartedAt,
        CompletedAt = output.CompletedAt,
        DurationMs = output.DurationMs,
        MachineName = output.MachineName,
        OsVersion = output.OsVersion,
        DotNetVersion = output.DotNetVersion,
        Store = (int)output.Store,
        Diagnostics = output.Diagnostics.Select(d => new BuildDiagnosticEntity
        {
            Id = d.Id, BuildOutputId = output.Id, Severity = (int)d.Severity, Code = d.Code,
            Message = d.Message, FilePath = d.FilePath, Line = d.Line, Column = d.Column, ProjectName = d.ProjectName
        }).ToList(),
        Artifacts = output.Artifacts.Select(a => new BuildArtifactEntity
        {
            BuildOutputId = output.Id, Name = a.Name, Path = a.Path,
            SizeBytes = a.SizeBytes, ArtifactType = a.ArtifactType, ContentHash = a.ContentHash
        }).ToList()
    };

    private static BuildOutput FromEntity(BuildOutputEntity e) => new()
    {
        Id = e.Id, ProjectName = e.ProjectName, Configuration = e.Configuration,
        TargetFramework = e.TargetFramework, Command = e.Command, TriggerSource = e.TriggerSource,
        Branch = e.Branch, CommitSha = e.CommitSha, ExitCode = e.ExitCode, Succeeded = e.Succeeded,
        Result = (BuildResult)e.Result, Stdout = e.Stdout, Stderr = e.Stderr,
        ErrorCount = e.ErrorCount, WarningCount = e.WarningCount, StartedAt = e.StartedAt,
        CompletedAt = e.CompletedAt, DurationMs = e.DurationMs, MachineName = e.MachineName,
        OsVersion = e.OsVersion, DotNetVersion = e.DotNetVersion, Store = (BuildOutputStore)e.Store,
        Diagnostics = e.Diagnostics.Select(d => new BuildDiagnostic
        {
            Id = d.Id, Severity = (BuildOutputSeverity)d.Severity, Code = d.Code,
            Message = d.Message, FilePath = d.FilePath, Line = d.Line, Column = d.Column, ProjectName = d.ProjectName
        }).ToList(),
        Artifacts = e.Artifacts.Select(a => new BuildArtifact
        {
            Name = a.Name, Path = a.Path, SizeBytes = a.SizeBytes,
            ArtifactType = a.ArtifactType, ContentHash = a.ContentHash
        }).ToList()
    };
}
