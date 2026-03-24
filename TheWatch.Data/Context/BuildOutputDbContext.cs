// BuildOutputDbContext — EF Core DbContext for build output persistence.
// Used by the SQLite adapter (and optionally SQL Server / PostgreSQL adapters).
// Separate from TheWatchDbContext to keep build data isolated and portable.
//
// SQLite: stores to a local file (Data/builds.db by default).
// SQL Server/PostgreSQL: uses the existing Aspire-injected connection strings.
//
// Example:
//   services.AddDbContext<BuildOutputDbContext>(opt => opt.UseSqlite("Data Source=builds.db"));

using Microsoft.EntityFrameworkCore;
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Enums;

namespace TheWatch.Data.Context;

public class BuildOutputDbContext : DbContext
{
    public DbSet<BuildOutputEntity> BuildOutputs { get; set; }
    public DbSet<BuildDiagnosticEntity> Diagnostics { get; set; }
    public DbSet<BuildArtifactEntity> Artifacts { get; set; }

    public BuildOutputDbContext(DbContextOptions<BuildOutputDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BuildOutputEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ProjectName);
            entity.HasIndex(e => e.StartedAt);
            entity.HasIndex(e => e.Succeeded);
            entity.HasIndex(e => new { e.ProjectName, e.StartedAt });

            entity.HasMany(e => e.Diagnostics)
                .WithOne()
                .HasForeignKey(d => d.BuildOutputId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.Artifacts)
                .WithOne()
                .HasForeignKey(a => a.BuildOutputId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BuildDiagnosticEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Severity);
            entity.HasIndex(e => e.Code);
        });

        modelBuilder.Entity<BuildArtifactEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
        });
    }
}

// EF Core entities — flattened for relational storage (no nested lists of complex objects)
public class BuildOutputEntity
{
    public string Id { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public string Configuration { get; set; } = "Debug";
    public string? TargetFramework { get; set; }
    public string? Command { get; set; }
    public string? TriggerSource { get; set; }
    public string? Branch { get; set; }
    public string? CommitSha { get; set; }
    public int ExitCode { get; set; }
    public bool Succeeded { get; set; }
    public int Result { get; set; } // BuildResult enum stored as int
    public string? Stdout { get; set; }
    public string? Stderr { get; set; }
    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public long DurationMs { get; set; }
    public string? MachineName { get; set; }
    public string? OsVersion { get; set; }
    public string? DotNetVersion { get; set; }
    public int Store { get; set; } // BuildOutputStore enum

    public List<BuildDiagnosticEntity> Diagnostics { get; set; } = new();
    public List<BuildArtifactEntity> Artifacts { get; set; } = new();
}

public class BuildDiagnosticEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string BuildOutputId { get; set; } = string.Empty;
    public int Severity { get; set; } // BuildOutputSeverity enum
    public string? Code { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? FilePath { get; set; }
    public int? Line { get; set; }
    public int? Column { get; set; }
    public string? ProjectName { get; set; }
}

public class BuildArtifactEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string BuildOutputId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Path { get; set; }
    public long SizeBytes { get; set; }
    public string? ArtifactType { get; set; }
    public string? ContentHash { get; set; }
}
