// =============================================================================
// PostgreSqlUnitOfWork.cs — EF Core Unit of Work for PostgreSQL (Npgsql)
// =============================================================================
// Write-Ahead Log (WAL) Pattern:
//   Transaction boundaries and SaveChanges calls are logged before execution.
//
// PostgreSQL Transaction Notes:
//   - Supports SERIALIZABLE, REPEATABLE READ, READ COMMITTED isolation levels
//   - Npgsql supports savepoints via NpgsqlTransaction.SaveAsync("name")
//   - Advisory locks available: SELECT pg_advisory_lock(key)
//   - Two-phase commit (PREPARE TRANSACTION) supported for distributed txns
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

namespace TheWatch.Data.Repositories.PostgreSql
{
    /// <summary>
    /// Unit of Work implementation for PostgreSQL backed by <see cref="TheWatchDbContext"/>
    /// using the Npgsql EF Core provider. Coordinates multiple repositories under a single
    /// database transaction with atomic commit/rollback semantics.
    /// </summary>
    public class PostgreSqlUnitOfWork : IUnitOfWork, IDisposable
    {
        private readonly TheWatchDbContext _dbContext;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger<PostgreSqlUnitOfWork> _logger;
        private IDbContextTransaction? _transaction;

        private IRepository<WorkItem>? _workItems;
        private IRepository<Milestone>? _milestones;
        private IRepository<AgentActivity>? _agentActivities;
        private IRepository<BuildStatus>? _buildStatuses;
        private IRepository<BranchInfo>? _branchInfos;
        private IRepository<SimulationEvent>? _simulationEvents;

        /// <summary>
        /// Initializes a new instance of <see cref="PostgreSqlUnitOfWork"/>.
        /// </summary>
        public PostgreSqlUnitOfWork(TheWatchDbContext dbContext, ILoggerFactory loggerFactory)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
            _logger = _loggerFactory.CreateLogger<PostgreSqlUnitOfWork>();
        }

        /// <inheritdoc />
        public IRepository<WorkItem> WorkItems =>
            _workItems ??= new PostgreSqlRepository<WorkItem>(_dbContext, _loggerFactory.CreateLogger<PostgreSqlRepository<WorkItem>>());

        /// <inheritdoc />
        public IRepository<Milestone> Milestones =>
            _milestones ??= new PostgreSqlRepository<Milestone>(_dbContext, _loggerFactory.CreateLogger<PostgreSqlRepository<Milestone>>());

        /// <inheritdoc />
        public IRepository<AgentActivity> AgentActivities =>
            _agentActivities ??= new PostgreSqlRepository<AgentActivity>(_dbContext, _loggerFactory.CreateLogger<PostgreSqlRepository<AgentActivity>>());

        /// <inheritdoc />
        public IRepository<BuildStatus> BuildStatuses =>
            _buildStatuses ??= new PostgreSqlRepository<BuildStatus>(_dbContext, _loggerFactory.CreateLogger<PostgreSqlRepository<BuildStatus>>());

        /// <inheritdoc />
        public IRepository<BranchInfo> BranchInfos =>
            _branchInfos ??= new PostgreSqlRepository<BranchInfo>(_dbContext, _loggerFactory.CreateLogger<PostgreSqlRepository<BranchInfo>>());

        /// <inheritdoc />
        public IRepository<SimulationEvent> SimulationEvents =>
            _simulationEvents ??= new PostgreSqlRepository<SimulationEvent>(_dbContext, _loggerFactory.CreateLogger<PostgreSqlRepository<SimulationEvent>>());

        /// <inheritdoc />
        public async Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            _logger.LogInformation("[WAL-TXN] [PostgreSQL] SaveChanges starting");
            var result = await _dbContext.SaveChangesAsync(ct);
            _logger.LogInformation("[WAL-TXN] [PostgreSQL] SaveChanges completed — {Count} entities affected", result);
            return result;
        }

        /// <inheritdoc />
        public async Task BeginTransactionAsync(CancellationToken ct = default)
        {
            _logger.LogInformation("[WAL-TXN] [PostgreSQL] BeginTransaction");
            _transaction = await _dbContext.Database.BeginTransactionAsync(ct);
            _logger.LogInformation("[WAL-TXN] [PostgreSQL] Transaction started — TxId={TransactionId}", _transaction.TransactionId);
        }

        /// <inheritdoc />
        public async Task CommitAsync(CancellationToken ct = default)
        {
            if (_transaction is null)
                throw new InvalidOperationException("No active transaction to commit.");

            _logger.LogInformation("[WAL-TXN] [PostgreSQL] CommitTransaction TxId={TransactionId}", _transaction.TransactionId);
            await _transaction.CommitAsync(ct);
            _logger.LogInformation("[WAL-TXN] [PostgreSQL] Transaction committed");
        }

        /// <inheritdoc />
        public async Task RollbackAsync(CancellationToken ct = default)
        {
            if (_transaction is null)
                throw new InvalidOperationException("No active transaction to rollback.");

            _logger.LogWarning("[WAL-TXN] [PostgreSQL] RollbackTransaction TxId={TransactionId}", _transaction.TransactionId);
            await _transaction.RollbackAsync(ct);
            _logger.LogWarning("[WAL-TXN] [PostgreSQL] Transaction rolled back");
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
