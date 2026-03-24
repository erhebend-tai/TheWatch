// =============================================================================
// CosmosDbUnitOfWork.cs — Azure Cosmos DB Unit of Work (Native SDK)
// =============================================================================
// Write-Ahead Log (WAL) Pattern:
//   Transaction boundaries logged. Cosmos DB TransactionalBatch is used
//   where possible (single partition key constraint).
//
// Cosmos DB Transaction Notes:
//   - TransactionalBatch: Atomic operations within a SINGLE logical partition
//   - No cross-partition transactions in Cosmos DB
//   - For cross-partition atomicity, use the Saga/Compensating pattern
//   - SaveChanges is a no-op here; each repo operation is immediately persisted
//   - BeginTransaction/Commit/Rollback are best-effort for interface compliance
//
// Example TransactionalBatch usage:
//   var batch = container.CreateTransactionalBatch(new PartitionKey("pk-value"));
//   batch.CreateItem(item1);
//   batch.ReplaceItem(item2.Id, item2);
//   var batchResponse = await batch.ExecuteAsync();
//   if (!batchResponse.IsSuccessStatusCode) { /* handle failure */ }
// =============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using TheWatch.Shared.Domain.Models;
using TheWatch.Data.Repositories;

namespace TheWatch.Data.Repositories.CosmosDb
{
    /// <summary>
    /// Unit of Work implementation for Azure Cosmos DB using the native SDK.
    /// Each entity type maps to a separate container. Transactional batches are
    /// constrained to single-partition operations per Cosmos DB limitations.
    /// </summary>
    public class CosmosDbUnitOfWork : IUnitOfWork, IDisposable
    {
        private readonly CosmosClient _cosmosClient;
        private readonly string _databaseName;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger<CosmosDbUnitOfWork> _logger;

        // Pending operations for pseudo-transactional support
        private readonly List<Func<CancellationToken, Task>> _pendingOperations = new();
        private bool _inTransaction;

        // Lazy-initialized repositories
        private IRepository<WorkItem>? _workItems;
        private IRepository<Milestone>? _milestones;
        private IRepository<AgentActivity>? _agentActivities;
        private IRepository<BuildStatus>? _buildStatuses;
        private IRepository<BranchInfo>? _branchInfos;
        private IRepository<SimulationEvent>? _simulationEvents;

        /// <summary>
        /// Initializes a new instance of <see cref="CosmosDbUnitOfWork"/>.
        /// </summary>
        /// <param name="cosmosClient">The Cosmos DB client.</param>
        /// <param name="databaseName">The target database name.</param>
        /// <param name="loggerFactory">Factory for creating typed loggers.</param>
        public CosmosDbUnitOfWork(CosmosClient cosmosClient, string databaseName, ILoggerFactory loggerFactory)
        {
            _cosmosClient = cosmosClient ?? throw new ArgumentNullException(nameof(cosmosClient));
            _databaseName = databaseName ?? throw new ArgumentNullException(nameof(databaseName));
            _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
            _logger = _loggerFactory.CreateLogger<CosmosDbUnitOfWork>();
        }

        /// <inheritdoc />
        public IRepository<WorkItem> WorkItems =>
            _workItems ??= new CosmosDbRepository<WorkItem>(_cosmosClient, _databaseName, "workitems",
                _loggerFactory.CreateLogger<CosmosDbRepository<WorkItem>>());

        /// <inheritdoc />
        public IRepository<Milestone> Milestones =>
            _milestones ??= new CosmosDbRepository<Milestone>(_cosmosClient, _databaseName, "milestones",
                _loggerFactory.CreateLogger<CosmosDbRepository<Milestone>>());

        /// <inheritdoc />
        public IRepository<AgentActivity> AgentActivities =>
            _agentActivities ??= new CosmosDbRepository<AgentActivity>(_cosmosClient, _databaseName, "agentactivities",
                _loggerFactory.CreateLogger<CosmosDbRepository<AgentActivity>>());

        /// <inheritdoc />
        public IRepository<BuildStatus> BuildStatuses =>
            _buildStatuses ??= new CosmosDbRepository<BuildStatus>(_cosmosClient, _databaseName, "buildstatuses",
                _loggerFactory.CreateLogger<CosmosDbRepository<BuildStatus>>());

        /// <inheritdoc />
        public IRepository<BranchInfo> BranchInfos =>
            _branchInfos ??= new CosmosDbRepository<BranchInfo>(_cosmosClient, _databaseName, "branchinfos",
                _loggerFactory.CreateLogger<CosmosDbRepository<BranchInfo>>());

        /// <inheritdoc />
        public IRepository<SimulationEvent> SimulationEvents =>
            _simulationEvents ??= new CosmosDbRepository<SimulationEvent>(_cosmosClient, _databaseName, "simulationevents",
                _loggerFactory.CreateLogger<CosmosDbRepository<SimulationEvent>>());

        /// <inheritdoc />
        /// <remarks>
        /// Cosmos DB operations are immediately persisted. SaveChanges returns 0
        /// unless there are pending operations queued during a transaction scope.
        /// </remarks>
        public async Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            _logger.LogInformation("[WAL-TXN] [CosmosDB] SaveChanges — {Count} pending operations", _pendingOperations.Count);

            var count = _pendingOperations.Count;
            foreach (var operation in _pendingOperations)
            {
                await operation(ct);
            }
            _pendingOperations.Clear();

            _logger.LogInformation("[WAL-TXN] [CosmosDB] SaveChanges completed — {Count} operations executed", count);
            return count;
        }

        /// <inheritdoc />
        /// <remarks>
        /// Cosmos DB does not support cross-partition transactions. This begins a
        /// logical transaction scope that queues operations for batch execution.
        /// True atomicity is only guaranteed within a single partition via TransactionalBatch.
        /// </remarks>
        public Task BeginTransactionAsync(CancellationToken ct = default)
        {
            _logger.LogInformation("[WAL-TXN] [CosmosDB] BeginTransaction (logical — no cross-partition atomicity)");
            _inTransaction = true;
            _pendingOperations.Clear();
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public async Task CommitAsync(CancellationToken ct = default)
        {
            if (!_inTransaction)
                throw new InvalidOperationException("No active transaction to commit.");

            _logger.LogInformation("[WAL-TXN] [CosmosDB] CommitTransaction — executing {Count} pending operations", _pendingOperations.Count);
            await SaveChangesAsync(ct);
            _inTransaction = false;
            _logger.LogInformation("[WAL-TXN] [CosmosDB] Transaction committed");
        }

        /// <inheritdoc />
        public Task RollbackAsync(CancellationToken ct = default)
        {
            if (!_inTransaction)
                throw new InvalidOperationException("No active transaction to rollback.");

            _logger.LogWarning("[WAL-TXN] [CosmosDB] RollbackTransaction — discarding {Count} pending operations", _pendingOperations.Count);
            _pendingOperations.Clear();
            _inTransaction = false;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            // CosmosClient is typically a singleton; do not dispose here.
            // The DI container should manage its lifetime.
            GC.SuppressFinalize(this);
            await Task.CompletedTask;
        }

        /// <summary>
        /// Disposes the Cosmos DB client resources.
        /// </summary>
        public void Dispose()
        {
            // CosmosClient is typically a singleton; do not dispose here.
            // The DI container should manage its lifetime.
            GC.SuppressFinalize(this);
        }
    }
}
