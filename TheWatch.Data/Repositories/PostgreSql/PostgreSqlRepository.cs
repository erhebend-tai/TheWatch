// =============================================================================
// PostgreSqlRepository.cs — Generic EF Core Repository for PostgreSQL (Npgsql)
// =============================================================================
// Write-Ahead Log (WAL) Pattern:
//   Identical to SqlServerRepository — every mutating operation is logged
//   BEFORE execution via ILogger for auditability and crash diagnostics.
//
// PostgreSQL-Specific Features (via Npgsql EF Core Provider):
//   - JSONB columns: Map C# objects to jsonb for semi-structured data.
//       Example: builder.Property(e => e.Metadata).HasColumnType("jsonb");
//   - Array types: Native support for int[], string[], etc.
//       Example: builder.Property(e => e.Tags).HasColumnType("text[]");
//   - Full-text search: Use EF.Functions.ToTsVector / ToTsQuery
//       Example: .Where(e => EF.Functions.ToTsVector("english", e.Title)
//                   .Matches(EF.Functions.ToTsQuery("english", searchTerm)))
//   - Range types: daterange, tsrange, int4range, etc.
//   - Enum types: Map C# enums to PostgreSQL enums.
//       Example: builder.HasPostgresEnum<StatusEnum>();
//   - Citext: Case-insensitive text type.
//   - Hstore: Key-value pairs in a single column.
//   - Network types: inet, macaddr natively supported.
//   - Spatial types via PostGIS: geography, geometry.
//
// Providers with superior open-source processes checked:
//   - Marten (PostgreSQL document DB for .NET) — uses Npgsql directly,
//     superior for event-sourcing workloads but different paradigm
//   - Npgsql raw — lower-level, no expression tree support
//   - Dapper + Npgsql — micro-ORM, manual SQL
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TheWatch.Data.Context;
using TheWatch.Data.Repositories;

namespace TheWatch.Data.Repositories.PostgreSql
{
    /// <summary>
    /// Generic repository implementation backed by PostgreSQL via Entity Framework Core
    /// and the Npgsql provider. Structurally identical to <c>SqlServerRepository</c> because
    /// EF Core abstracts provider differences; PostgreSQL-specific optimizations are
    /// configured at the DbContext/model-builder level (see header comments).
    /// </summary>
    /// <typeparam name="T">The entity type. Must be a reference type.</typeparam>
    public class PostgreSqlRepository<T> : IRepository<T> where T : class
    {
        private readonly TheWatchDbContext _dbContext;
        private readonly DbSet<T> _dbSet;
        private readonly ILogger<PostgreSqlRepository<T>> _logger;

        /// <summary>
        /// Initializes a new instance of <see cref="PostgreSqlRepository{T}"/>.
        /// </summary>
        /// <param name="dbContext">The EF Core database context bound to PostgreSQL (Npgsql).</param>
        /// <param name="logger">Logger for write-ahead log entries and diagnostics.</param>
        public PostgreSqlRepository(TheWatchDbContext dbContext, ILogger<PostgreSqlRepository<T>> logger)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _dbSet = _dbContext.Set<T>();
        }

        /// <inheritdoc />
        public async Task<T?> GetByIdAsync(string id, CancellationToken ct = default)
        {
            _logger.LogDebug("[WAL-READ] [PostgreSQL] GetById {EntityType} Id={Id}", typeof(T).Name, id);
            return await _dbSet.FindAsync(new object[] { id }, ct);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default)
        {
            _logger.LogDebug("[WAL-READ] [PostgreSQL] GetAll {EntityType}", typeof(T).Name);
            return await _dbSet.AsNoTracking().ToListAsync(ct);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<T>> FindAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default)
        {
            _logger.LogDebug("[WAL-READ] [PostgreSQL] Find {EntityType} with predicate", typeof(T).Name);
            return await _dbSet.AsNoTracking().Where(predicate).ToListAsync(ct);
        }

        /// <inheritdoc />
        public async Task<T> AddAsync(T entity, CancellationToken ct = default)
        {
            _logger.LogInformation("[WAL] [PostgreSQL] Adding {EntityType} entity", typeof(T).Name);
            var entry = await _dbSet.AddAsync(entity, ct);
            await _dbContext.SaveChangesAsync(ct);
            _logger.LogInformation("[WAL] [PostgreSQL] Added {EntityType} entity successfully", typeof(T).Name);
            return entry.Entity;
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<T>> AddRangeAsync(IEnumerable<T> entities, CancellationToken ct = default)
        {
            var list = entities as IList<T> ?? entities.ToList();
            _logger.LogInformation("[WAL] [PostgreSQL] AddRange {EntityType} Count={Count}", typeof(T).Name, list.Count);
            await _dbSet.AddRangeAsync(list, ct);
            await _dbContext.SaveChangesAsync(ct);
            _logger.LogInformation("[WAL] [PostgreSQL] AddRange {EntityType} completed", typeof(T).Name);
            return list.AsReadOnly();
        }

        /// <inheritdoc />
        public async Task UpdateAsync(T entity, CancellationToken ct = default)
        {
            _logger.LogInformation("[WAL] [PostgreSQL] Updating {EntityType} entity", typeof(T).Name);
            _dbSet.Update(entity);
            await _dbContext.SaveChangesAsync(ct);
            _logger.LogInformation("[WAL] [PostgreSQL] Updated {EntityType} entity successfully", typeof(T).Name);
        }

        /// <inheritdoc />
        public async Task DeleteAsync(string id, CancellationToken ct = default)
        {
            _logger.LogInformation("[WAL] [PostgreSQL] Deleting {EntityType} Id={Id}", typeof(T).Name, id);
            var entity = await _dbSet.FindAsync(new object[] { id }, ct);
            if (entity is null)
            {
                _logger.LogWarning("[WAL] [PostgreSQL] Delete {EntityType} Id={Id} — entity not found", typeof(T).Name, id);
                return;
            }

            _dbSet.Remove(entity);
            await _dbContext.SaveChangesAsync(ct);
            _logger.LogInformation("[WAL] [PostgreSQL] Deleted {EntityType} Id={Id} successfully", typeof(T).Name, id);
        }

        /// <inheritdoc />
        public async Task<long> CountAsync(CancellationToken ct = default)
        {
            _logger.LogDebug("[WAL-READ] [PostgreSQL] Count {EntityType}", typeof(T).Name);
            return await _dbSet.LongCountAsync(ct);
        }

        /// <inheritdoc />
        public async Task<bool> ExistsAsync(string id, CancellationToken ct = default)
        {
            _logger.LogDebug("[WAL-READ] [PostgreSQL] Exists {EntityType} Id={Id}", typeof(T).Name, id);
            var entity = await _dbSet.FindAsync(new object[] { id }, ct);
            return entity is not null;
        }
    }
}
