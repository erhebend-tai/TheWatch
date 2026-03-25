// =============================================================================
// CodeIntelligenceDbContext.cs — Separate EF Core context for code-intelligence graph
// =============================================================================
// Uses SQL Server graph tables via raw SQL for NODE/EDGE semantics while
// providing standard EF Core DbSet access for CRUD and bulk operations.
//
// Hybrid approach rationale:
//   EF Core has no first-class support for SQL Server graph tables (AS NODE /
//   AS EDGE / MATCH syntax). This context defines standard entities that EF Core
//   can query and insert into. The actual SQL Server tables are created as graph
//   tables via the InitialCodeIntelligence.sql migration script, which uses
//   CREATE TABLE ... AS NODE and CREATE TABLE ... AS EDGE.
//
// Graph traversal queries use FromSqlRaw with MATCH syntax — see GraphQueries.cs.
//
// Connection string convention:
//   Aspire:  builder.AddSqlServerDbContext<CodeIntelligenceDbContext>("thewatch-sqlserver");
//   Manual:  options.UseSqlServer("Server=...;Database=TheWatch;...")
//
// Example:
//   services.AddDbContext<CodeIntelligenceDbContext>(opt =>
//       opt.UseSqlServer(config.GetConnectionString("thewatch-sqlserver")));
//
//   // Insert a symbol
//   ctx.Symbols.Add(new SymbolNode { ... });
//   await ctx.SaveChangesAsync();
//
//   // Graph query via raw SQL
//   var callers = await ctx.Symbols
//       .FromSqlRaw(GraphQueries.FindCallers("MyMethod"))
//       .ToListAsync();
//
// WAL: EF Core change tracker handles in-memory WAL; SQL Server transaction log
//      handles durability. Graph MATCH queries bypass the change tracker (read-only).
// =============================================================================

using Microsoft.EntityFrameworkCore;

namespace TheWatch.Data.CodeIntelligence;

/// <summary>
/// Entity Framework Core database context for the code-intelligence graph store.
/// Separate from <see cref="Context.TheWatchDbContext"/> to keep application data
/// and code-analysis data in independent schema namespaces.
/// </summary>
public class CodeIntelligenceDbContext : DbContext
{
    /// <summary>
    /// Initializes a new instance of <see cref="CodeIntelligenceDbContext"/>.
    /// </summary>
    /// <param name="options">Context options configured with SQL Server provider.</param>
    public CodeIntelligenceDbContext(DbContextOptions<CodeIntelligenceDbContext> options)
        : base(options) { }

    /// <summary>Code symbols (classes, interfaces, methods, enums, etc.) as graph NODEs.</summary>
    public DbSet<SymbolNode> Symbols => Set<SymbolNode>();

    /// <summary>Source documents (files) as graph NODEs.</summary>
    public DbSet<DocumentNode> Documents => Set<DocumentNode>();

    /// <summary>Symbol-to-symbol references as graph EDGEs.</summary>
    public DbSet<ReferenceEdge> References => Set<ReferenceEdge>();

    /// <summary>Symbol-to-tag associations as graph EDGEs.</summary>
    public DbSet<TagEdge> Tags => Set<TagEdge>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── SymbolNode (NODE) ────────────────────────────────────────
        modelBuilder.Entity<SymbolNode>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.Repo).HasMaxLength(256).IsRequired();
            e.Property(s => s.Project).HasMaxLength(256).IsRequired();
            e.Property(s => s.File).HasMaxLength(1024).IsRequired();
            e.Property(s => s.Kind).HasMaxLength(64).IsRequired();
            e.Property(s => s.Language).HasMaxLength(64).IsRequired();
            e.Property(s => s.FullName).HasMaxLength(1024).IsRequired();
            e.Property(s => s.Signature).HasMaxLength(2048).IsRequired();
            e.Property(s => s.BodyHash).HasMaxLength(64);
            e.Property(s => s.IndexedAt).HasDefaultValueSql("SYSUTCDATETIME()");

            // Indexes for common query patterns
            e.HasIndex(s => s.FullName);
            e.HasIndex(s => s.Repo);
            e.HasIndex(s => s.Kind);
            e.HasIndex(s => s.Language);
            e.HasIndex(s => new { s.Repo, s.File, s.FullName }).IsUnique();
        });

        // ── DocumentNode (NODE) ──────────────────────────────────────
        modelBuilder.Entity<DocumentNode>(e =>
        {
            e.HasKey(d => d.Id);
            e.Property(d => d.Repo).HasMaxLength(256).IsRequired();
            e.Property(d => d.Project).HasMaxLength(256).IsRequired();
            e.Property(d => d.FilePath).HasMaxLength(1024).IsRequired();
            e.Property(d => d.Language).HasMaxLength(64).IsRequired();
            e.Property(d => d.IndexedAt).HasDefaultValueSql("SYSUTCDATETIME()");

            e.HasIndex(d => new { d.Repo, d.FilePath }).IsUnique();
        });

        // ── ReferenceEdge (EDGE) ─────────────────────────────────────
        modelBuilder.Entity<ReferenceEdge>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.Kind).HasMaxLength(64).IsRequired();
            e.Property(r => r.SourceFile).HasMaxLength(1024);

            e.HasIndex(r => r.SourceId);
            e.HasIndex(r => r.TargetId);
            e.HasIndex(r => r.Kind);
        });

        // ── TagEdge (EDGE) ───────────────────────────────────────────
        modelBuilder.Entity<TagEdge>(e =>
        {
            e.HasKey(t => t.Id);
            e.Property(t => t.Tag).HasMaxLength(128).IsRequired();

            e.HasIndex(t => t.SymbolId);
            e.HasIndex(t => t.Tag);
        });
    }
}
