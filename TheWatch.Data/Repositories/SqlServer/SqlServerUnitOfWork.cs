// =============================================================================
// SqlServerUnitOfWork.cs — EF Core Unit of Work for SQL Server
// =============================================================================
// Write-Ahead Log (WAL) Pattern:
//   Transaction boundaries and SaveChanges calls are logged before execution.
//   This allows reconstruction of partially-applied batches from log output.
//
// Example:
//   _logger.LogInformation("[WAL-TXN] BeginTransaction");
//   await _dbContext.Database.BeginTransactionAsync(ct);
//   _logger.LogInformation("[WAL-TXN] Transaction started");
//
// Lazy initialization pattern:
//   Each repository property is lazily initialized on first access using
//   null-coalescing assignment (??=). This avoids allocating repositories
//   that are never used in a given unit-of-work scope.
// =============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using TheWatch.Shared.Domain.Models;
using TheWatch.Data.Context;
using TheWatch.Data.Repositories;

namespace TheWatch.Data.Repositories.SqlServer
{
    /// <summary>
    /// Unit of Work implementation for SQL Server backed by <see cref="TheWatchDbContext"/>.
    /// Coordinates multiple repositories under a single database transaction and
    /// provides atomic commit/rollback semantics.
    /// </summary>
    public class SqlServerUnitOfWork : IUnitOfWork, IDisposable
    {
        private readonly TheWatchDbContext _dbContext;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger<SqlServerUnitOfWork> _logger;
        private IDbContextTransaction? _transaction;

        // Lazy-initialized repository backing fields
        private IRepository<WorkItem>? _workItems;
        private IRepository<Milestone>? _milestones;
        private IRepository<AgentActivity>? _agentActivities;
        private IRepository<BuildStatus>? _buildStatuses;
        private IRepository<BranchInfo>? _branchInfos;
        private IRepository<SimulationEvent>? _simulationEvents;

        /// <summary>
        /// Initializes a new instance of <see cref="SqlServerUnitOfWork"/>.
        /// </summary>
        /// <param name="dbContext">The EF Core database context.</param>
        /// <param name="loggerFactory">Factory for creating typed loggers for each repository.</param>
        public SqlServerUnitOfWork(TheWatchDbContext dbContext, ILoggerFactory loggerFactory)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
            _logger = _loggerFactory.CreateLogger<SqlServerUnitOfWork>();
        }

        /// <inheritdoc />
        public IRepository<WorkItem> WorkItems =>
            _workItems ??= new SqlServerRepository<WorkItem>(_dbContext, _loggerFactory.CreateLogger<SqlServerRepository<WorkItem>>());

        /// <inheritdoc />
        public IRepository<Milestone> Milestones =>
            _milestones ??= new SqlServerRepository<Milestone>(_dbContext, _loggerFactory.CreateLogger<SqlServerRepository<Milestone>>());

        /// <inheritdoc />
        public IRepository<AgentActivity> AgentActivities =>
            _agentActivities ??= new SqlServerRepository<AgentActivity>(_dbContext, _loggerFactory.CreateLogger<SqlServerRepository<AgentActivity>>());

        /// <inheritdoc />
        public IRepository<BuildStatus> BuildStatuses =>
            _buildStatuses ??= new SqlServerRepository<BuildStatus>(_dbContext, _loggerFactory.CreateLogger<SqlServerRepository<BuildStatus>>());

        /// <inheritdoc />
        public IRepository<BranchInfo> BranchInfos =>
            _branchInfos ??= new SqlServerRepository<BranchInfo>(_dbContext, _loggerFactory.CreateLogger<SqlServerRepository<BranchInfo>>());

        /// <inheritdoc />
        public IRepository<SimulationEvent> SimulationEvents =>
            _simulationEvents ??= new SqlServerRepository<SimulationEvent>(_dbContext, _loggerFactory.CreateLogger<SqlServerRepository<SimulationEvent>>());

        /// <inheritdoc />
        public async Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            _logger.LogInformation("[WAL-TXN] SaveChanges starting for SqlServer");
            var result = await _dbContext.SaveChangesAsync(ct);
            _logger.LogInformation("[WAL-TXN] SaveChanges completed — {Count} entities affected", result);
            return result;
        }

        /// <inheritdoc />
        public async Task BeginTransactionAsync(CancellationToken ct = default)
        {
            _logger.LogInformation("[WAL-TXN] BeginTransaction for SqlServer");
            _transaction = await _dbContext.Database.BeginTransactionAsync(ct);
            _logger.LogInformation("[WAL-TXN] Transaction started — TxId={TransactionId}", _transaction.TransactionId);
        }

        /// <inheritdoc />
        public async Task CommitAsync(CancellationToken ct = default)
        {
            if (_transaction is null)
                throw new InvalidOperationException("No active transaction to commit. Call BeginTransactionAsync first.");

            _logger.LogInformation("[WAL-TXN] CommitTransaction TxId={TransactionId}", _transaction.TransactionId);
            await _transaction.CommitAsync(ct);
            _logger.LogInformation("[WAL-TXN] Transaction committed successfully");
        }

        /// <inheritdoc />
        public async Task RollbackAsync(CancellationToken ct = default)
        {
            if (_transaction is null)
                throw new InvalidOperationException("No active transaction to rollback. Call BeginTransactionAsync first.");

            _logger.LogWarning("[WAL-TXN] RollbackTransaction TxId={TransactionId}", _transaction.TransactionId);
            await _transaction.RollbackAsync(ct);
            _logger.LogWarning("[WAL-TXN] Transaction rolled back");
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            _transaction?.Dispose();
            await _dbContext.DisposeAsync();
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Disposes the transaction and database context.
        /// </summary>
        public void Dispose()
        {
            _transaction?.Dispose();
            _dbContext.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
