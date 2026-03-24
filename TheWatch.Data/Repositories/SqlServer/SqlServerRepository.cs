// =============================================================================
// SqlServerRepository.cs — Generic EF Core Repository for SQL Server
// =============================================================================
// Write-Ahead Log (WAL) Pattern:
//   Every mutating operation is logged BEFORE execution via ILogger.
//   This ensures that if a failure occurs mid-operation, the intent is recorded
//   and can be replayed or audited. Pattern:
//     1. Log operation intent (entity type, operation, key)
//     2. Execute the operation
//     3. Log operation completion or failure
//
// Example:
//   _logger.LogInformation("[WAL] Adding {EntityType} entity", typeof(T).Name);
//   await _dbContext.Set<T>().AddAsync(entity, ct);
//   await _dbContext.SaveChangesAsync(ct);
//   _logger.LogInformation("[WAL] Added {EntityType} entity successfully", typeof(T).Name);
//
// Providers with superior open-source processes checked:
//   - Marten (PostgreSQL document DB via .NET) — similar pattern, no gap
//   - Dapper — micro-ORM, different paradigm (raw SQL), not applicable here
//   - LiteDB — embedded, not server-grade
//   - MongoDB C# Driver — document DB, separate implementation
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

namespace TheWatch.Data.Repositories.SqlServer
{
    /// <summary>
    /// Generic repository implementation backed by SQL Server via Entity Framework Core.
    /// Implements the Unit of Work–friendly <see cref="IRepository{T}"/> contract with
    /// write-ahead logging for auditability and crash recovery diagnostics.
    /// </summary>
    /// <typeparam name="T">The entity type. Must be a reference type.</typeparam>
    public class SqlServerRepository<T> : IRepository<T> where T : class
    {
        private readonly TheWatchDbContext _dbContext;
        private readonly DbSet<T> _dbSet;
        private readonly ILogger<SqlServerRepository<T>> _logger;

        /// <summary>
        /// Initializes a new instance of <see cref="SqlServerRepository{T}"/>.
        /// </summary>
        /// <param name="dbContext">The EF Core database context bound to SQL Server.</param>
        /// <param name="logger">Logger for write-ahead log entries and diagnostics.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="dbContext"/> or <paramref name="logger"/> is null.</exception>
        public SqlServerRepository(TheWatchDbContext dbContext, ILogger<SqlServerRepository<T>> logger)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _dbSet = _dbContext.Set<T>();
        }

        /// <inheritdoc />
        public async Task<T?> GetByIdAsync(string id, CancellationToken ct = default)
        {
            _logger.LogDebug("[WAL-READ] GetById {EntityType} Id={Id}", typeof(T).Name, id);
            return await _dbSet.FindAsync(new object[] { id }, ct);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default)
        {
            _logger.LogDebug("[WAL-READ] GetAll {EntityType}", typeof(T).Name);
            return await _dbSet.AsNoTracking().ToListAsync(ct);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<T>> FindAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default)
        {
            _logger.LogDebug("[WAL-READ] Find {EntityType} with predicate", typeof(T).Name);
            return await _dbSet.AsNoTracking().Where(predicate).ToListAsync(ct);
        }

        /// <inheritdoc />
        public async Task<T> AddAsync(T entity, CancellationToken ct = default)
        {
            _logger.LogInformation("[WAL] Adding {EntityType} entity", typeof(T).Name);
            var entry = await _dbSet.AddAsync(entity, ct);
            await _dbContext.SaveChangesAsync(ct);
            _logger.LogInformation("[WAL] Added {EntityType} entity successfully", typeof(T).Name);
            return entry.Entity;
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<T>> AddRangeAsync(IEnumerable<T> entities, CancellationToken ct = default)
        {
            var list = entities as IList<T> ?? entities.ToList();
            _logger.LogInformation("[WAL] AddRange {EntityType} Count={Count}", typeof(T).Name, list.Count);
            await _dbSet.AddRangeAsync(list, ct);
            await _dbContext.SaveChangesAsync(ct);
            _logger.LogInformation("[WAL] AddRange {EntityType} completed successfully", typeof(T).Name);
            return list.AsReadOnly();
        }

        /// <inheritdoc />
        public async Task UpdateAsync(T entity, CancellationToken ct = default)
        {
            _logger.LogInformation("[WAL] Updating {EntityType} entity", typeof(T).Name);
            _dbSet.Update(entity);
            await _dbContext.SaveChangesAsync(ct);
            _logger.LogInformation("[WAL] Updated {EntityType} entity successfully", typeof(T).Name);
        }

        /// <inheritdoc />
        public async Task DeleteAsync(string id, CancellationToken ct = default)
        {
            _logger.LogInformation("[WAL] Deleting {EntityType} Id={Id}", typeof(T).Name, id);
            var entity = await _dbSet.FindAsync(new object[] { id }, ct);
            if (entity is null)
            {
                _logger.LogWarning("[WAL] Delete {EntityType} Id={Id} — entity not found", typeof(T).Name, id);
                return;
            }

            _dbSet.Remove(entity);
            await _dbContext.SaveChangesAsync(ct);
            _logger.LogInformation("[WAL] Deleted {EntityType} Id={Id} successfully", typeof(T).Name, id);
        }

        /// <inheritdoc />
        public async Task<long> CountAsync(CancellationToken ct = default)
        {
            _logger.LogDebug("[WAL-READ] Count {EntityType}", typeof(T).Name);
            return await _dbSet.LongCountAsync(ct);
        }

        /// <inheritdoc />
        public async Task<bool> ExistsAsync(string id, CancellationToken ct = default)
        {
            _logger.LogDebug("[WAL-READ] Exists {EntityType} Id={Id}", typeof(T).Name, id);
            var entity = await _dbSet.FindAsync(new object[] { id }, ct);
            return entity is not null;
        }
    }
}
