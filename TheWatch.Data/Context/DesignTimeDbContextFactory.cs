// ============================================================================
// DesignTimeDbContextFactory.cs — Enables EF Core CLI migrations without
// running the full application host.
//
// Write-Ahead Log Note:
//   Migration files themselves form a human-readable WAL of schema changes.
//   Each migration snapshot captures the "before" state, and the Up/Down
//   methods capture the delta. Always review generated migrations before
//   applying to production.
//
// Usage (from repo root):
//   # Generate a migration (defaults to SQL Server via THEWATCH_SQLSERVER_CONN):
//   dotnet ef migrations add InitialCreate -p TheWatch.Data -s TheWatch.Dashboard.Api
//
//   # Apply migrations:
//   dotnet ef database update -p TheWatch.Data -s TheWatch.Dashboard.Api
//
// Environment variable:
//   THEWATCH_SQLSERVER_CONN — override the default connection string.
//   Falls back to localdb if not set.
// ============================================================================

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TheWatch.Data.Context;

/// <summary>
/// Design-time factory consumed by <c>dotnet ef</c> CLI tooling to create
/// <see cref="TheWatchDbContext"/> instances for migration scaffolding.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<TheWatchDbContext>
{
    /// <summary>
    /// Default SQL Server connection string used when the
    /// <c>THEWATCH_SQLSERVER_CONN</c> environment variable is not set.
    /// Targets SQL Server LocalDB, which is available in Visual Studio installs.
    /// </summary>
    private const string DefaultConnectionString =
        "Server=(localdb)\\mssqllocaldb;Database=TheWatch;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";

    /// <inheritdoc />
    public TheWatchDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("THEWATCH_SQLSERVER_CONN")
                               ?? DefaultConnectionString;

        var optionsBuilder = new DbContextOptionsBuilder<TheWatchDbContext>();

        optionsBuilder.UseSqlServer(connectionString, sqlOptions =>
        {
            sqlOptions.MigrationsAssembly(typeof(TheWatchDbContext).Assembly.GetName().Name);
            sqlOptions.MigrationsHistoryTable("__EFMigrationsHistory", "thewatch");
        });

        return new TheWatchDbContext(optionsBuilder.Options);
    }
}
