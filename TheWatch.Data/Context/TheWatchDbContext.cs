// =============================================================================
// TheWatchDbContext.cs — EF Core DbContext for SQL Server and PostgreSQL.
// =============================================================================
// Write-Ahead Log (WAL) Pattern:
//   EF Core change tracker acts as an in-memory WAL. SQL Server uses its
//   transaction log; PostgreSQL uses pg_wal. Both are configured via the
//   provider-specific UseSqlServer / UseNpgsql calls in DI registration.
//
// Example:
//   services.AddDbContext<TheWatchDbContext>(opt => opt.UseSqlServer(connStr));
//   // or
//   services.AddDbContext<TheWatchDbContext>(opt => opt.UseNpgsql(connStr));
//
// Providers checked:
//   - ABP Framework vNext DbContext — similar pattern, no gap
//   - Clean Architecture templates (Jason Taylor, Ardalis) — same approach
//   - Orchard Core — multi-tenant DbContext, more complex but same base
// =============================================================================

using Microsoft.EntityFrameworkCore;
using TheWatch.Shared.Domain.Models;

namespace TheWatch.Data.Context;

/// <summary>
/// Entity Framework Core database context for TheWatch application.
/// Supports both SQL Server and PostgreSQL via provider-specific configuration.
/// </summary>
public class TheWatchDbContext : DbContext
{
    /// <summary>
    /// Initializes a new instance of <see cref="TheWatchDbContext"/>.
    /// </summary>
    /// <param name="options">Context options configured with the target provider.</param>
    public TheWatchDbContext(DbContextOptions<TheWatchDbContext> options) : base(options) { }

    /// <summary>Project milestones with progress tracking.</summary>
    public DbSet<Milestone> Milestones => Set<Milestone>();

    /// <summary>Work items (issues, tasks, bugs) across all platforms.</summary>
    public DbSet<WorkItem> WorkItems => Set<WorkItem>();

    /// <summary>Agent activity audit log.</summary>
    public DbSet<AgentActivity> AgentActivities => Set<AgentActivity>();

    /// <summary>CI/CD build status records.</summary>
    public DbSet<BuildStatus> BuildStatuses => Set<BuildStatus>();

    /// <summary>Git branch metadata.</summary>
    public DbSet<BranchInfo> BranchInfos => Set<BranchInfo>();

    /// <summary>Simulation events for testing and demo scenarios.</summary>
    public DbSet<SimulationEvent> SimulationEvents => Set<SimulationEvent>();

    /// <summary>Tamper-evident audit trail with Merkle hash chain.</summary>
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Milestone
        modelBuilder.Entity<Milestone>(e =>
        {
            e.HasKey(m => m.Id);
            e.Property(m => m.Name).HasMaxLength(256).IsRequired();
            e.Property(m => m.Description).HasMaxLength(2048);
            e.Ignore(m => m.PercentComplete); // computed property
        });

        // WorkItem
        modelBuilder.Entity<WorkItem>(e =>
        {
            e.HasKey(w => w.Id);
            e.Property(w => w.Title).HasMaxLength(512).IsRequired();
            e.Property(w => w.Description).HasMaxLength(4096);
            e.HasIndex(w => w.Status);
            e.HasIndex(w => w.Platform);
            e.HasIndex(w => w.Milestone);
        });

        // AgentActivity — use a shadow Id property since the model lacks one
        modelBuilder.Entity<AgentActivity>(e =>
        {
            e.Property<string>("Id").HasDefaultValueSql("NEWID()");
            e.HasKey("Id");
            e.HasIndex(a => a.Timestamp);
        });

        // BuildStatus
        modelBuilder.Entity<BuildStatus>(e =>
        {
            e.HasKey(b => b.RunId);
            e.HasIndex(b => b.Platform);
        });

        // BranchInfo
        modelBuilder.Entity<BranchInfo>(e =>
        {
            e.HasKey(b => b.Name);
            e.HasIndex(b => b.IsActive);
        });

        // SimulationEvent
        modelBuilder.Entity<SimulationEvent>(e =>
        {
            e.HasKey(s => s.Id);
            e.HasIndex(s => s.EventType);
            e.HasIndex(s => s.Timestamp);
        });

        // AuditEntry — Merkle-chained tamper-evident log
        modelBuilder.Entity<AuditEntry>(e =>
        {
            e.HasKey(a => a.Id);
            e.Property(a => a.Hash).HasMaxLength(64).IsRequired();
            e.Property(a => a.PreviousHash).HasMaxLength(64);
            e.Property(a => a.UserId).HasMaxLength(256).IsRequired();
            e.Property(a => a.EntityType).HasMaxLength(256);
            e.Property(a => a.EntityId).HasMaxLength(256);
            e.Property(a => a.CorrelationId).HasMaxLength(256);
            e.HasIndex(a => a.Timestamp);
            e.HasIndex(a => a.UserId);
            e.HasIndex(a => new { a.EntityType, a.EntityId });
        });
    }
}
