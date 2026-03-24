// =============================================================================
// FirebaseUnitOfWork.cs — Firebase Realtime Database Unit of Work
// =============================================================================
// Write-Ahead Log (WAL) Pattern:
//   Transaction scope and multi-path updates are logged before execution.
//
// Firebase RTDB Transaction Notes:
//   - Multi-path updates are atomic: PATCH with multiple paths
//       { "/workitems/id1": {...}, "/milestones/id2/progress": 50 }
//   - ServerValue.Timestamp for server-side timestamps
//   - Transaction (read-modify-write): runTransaction on mobile SDKs
//   - REST API: conditional writes via ETag headers (If-Match)
//   - No cross-database transactions; single database per project
//
// Example multi-path update:
//   var updates = new Dictionary<string, object> {
//       { "/workitems/abc123", workItemJson },
//       { "/milestones/m1/progress", 75 },
//       { "/agentactivities/act456", activityJson }
//   };
//   await httpClient.PatchAsync($"{dbUrl}/.json", updates);
// =============================================================================

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FirebaseAdmin;
using Microsoft.Extensions.Logging;
using TheWatch.Shared.Domain.Models;
using TheWatch.Data.Repositories;

namespace TheWatch.Data.Repositories.Firebase
{
    /// <summary>
    /// Unit of Work implementation for Firebase Realtime Database.
    /// Uses multi-path updates for batched atomic writes and provides
    /// a logical transaction scope for coordinating multiple repositories.
    /// </summary>
    public class FirebaseUnitOfWork : IUnitOfWork, IDisposable
    {
        private readonly FirebaseApp _firebaseApp;
        private readonly string _databaseUrl;
        private readonly System.Net.Http.HttpClient _httpClient;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger<FirebaseUnitOfWork> _logger;

        // Pending multi-path updates for transactional batching
        private readonly Dictionary<string, object> _pendingUpdates = new();
        private bool _inTransaction;

        private IRepository<WorkItem>? _workItems;
        private IRepository<Milestone>? _milestones;
        private IRepository<AgentActivity>? _agentActivities;
        private IRepository<BuildStatus>? _buildStatuses;
        private IRepository<BranchInfo>? _branchInfos;
        private IRepository<SimulationEvent>? _simulationEvents;

        /// <summary>
        /// Initializes a new instance of <see cref="FirebaseUnitOfWork"/>.
        /// </summary>
        /// <param name="firebaseApp">The Firebase application instance.</param>
        /// <param name="databaseUrl">The Firebase RTDB URL.</param>
        /// <param name="httpClient">HTTP client for REST API calls.</param>
        /// <param name="loggerFactory">Factory for creating typed loggers.</param>
        public FirebaseUnitOfWork(
            FirebaseApp firebaseApp,
            string databaseUrl,
            System.Net.Http.HttpClient httpClient,
            ILoggerFactory loggerFactory)
        {
            _firebaseApp = firebaseApp ?? throw new ArgumentNullException(nameof(firebaseApp));
            _databaseUrl = databaseUrl?.TrimEnd('/') ?? throw new ArgumentNullException(nameof(databaseUrl));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
            _logger = _loggerFactory.CreateLogger<FirebaseUnitOfWork>();
        }

        /// <inheritdoc />
        public IRepository<WorkItem> WorkItems =>
            _workItems ??= new FirebaseRepository<WorkItem>(_firebaseApp, _databaseUrl, "workitems",
                _httpClient, _loggerFactory.CreateLogger<FirebaseRepository<WorkItem>>());

        /// <inheritdoc />
        public IRepository<Milestone> Milestones =>
            _milestones ??= new FirebaseRepository<Milestone>(_firebaseApp, _databaseUrl, "milestones",
                _httpClient, _loggerFactory.CreateLogger<FirebaseRepository<Milestone>>());

        /// <inheritdoc />
        public IRepository<AgentActivity> AgentActivities =>
            _agentActivities ??= new FirebaseRepository<AgentActivity>(_firebaseApp, _databaseUrl, "agentactivities",
                _httpClient, _loggerFactory.CreateLogger<FirebaseRepository<AgentActivity>>());

        /// <inheritdoc />
        public IRepository<BuildStatus> BuildStatuses =>
            _buildStatuses ??= new FirebaseRepository<BuildStatus>(_firebaseApp, _databaseUrl, "buildstatuses",
                _httpClient, _loggerFactory.CreateLogger<FirebaseRepository<BuildStatus>>());

        /// <inheritdoc />
        public IRepository<BranchInfo> BranchInfos =>
            _branchInfos ??= new FirebaseRepository<BranchInfo>(_firebaseApp, _databaseUrl, "branchinfos",
                _httpClient, _loggerFactory.CreateLogger<FirebaseRepository<BranchInfo>>());

        /// <inheritdoc />
        public IRepository<SimulationEvent> SimulationEvents =>
            _simulationEvents ??= new FirebaseRepository<SimulationEvent>(_firebaseApp, _databaseUrl, "simulationevents",
                _httpClient, _loggerFactory.CreateLogger<FirebaseRepository<SimulationEvent>>());

        /// <inheritdoc />
        public async Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            if (_pendingUpdates.Count == 0)
            {
                _logger.LogDebug("[WAL-TXN] [Firebase] SaveChanges — no pending updates");
                return 0;
            }

            _logger.LogInformation("[WAL-TXN] [Firebase] SaveChanges — flushing {Count} multi-path updates", _pendingUpdates.Count);

            var json = JsonSerializer.Serialize(_pendingUpdates);
            var content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json");
            var response = await _httpClient.PatchAsync($"{_databaseUrl}/.json", content, ct);
            response.EnsureSuccessStatusCode();

            var count = _pendingUpdates.Count;
            _pendingUpdates.Clear();

            _logger.LogInformation("[WAL-TXN] [Firebase] SaveChanges completed — {Count} paths updated", count);
            return count;
        }

        /// <inheritdoc />
        public Task BeginTransactionAsync(CancellationToken ct = default)
        {
            _logger.LogInformation("[WAL-TXN] [Firebase] BeginTransaction (multi-path batch scope)");
            _inTransaction = true;
            _pendingUpdates.Clear();
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public async Task CommitAsync(CancellationToken ct = default)
        {
            if (!_inTransaction)
                throw new InvalidOperationException("No active transaction to commit.");

            _logger.LogInformation("[WAL-TXN] [Firebase] CommitTransaction — executing multi-path update");
            await SaveChangesAsync(ct);
            _inTransaction = false;
            _logger.LogInformation("[WAL-TXN] [Firebase] Transaction committed");
        }

        /// <inheritdoc />
        public Task RollbackAsync(CancellationToken ct = default)
        {
            if (!_inTransaction)
                throw new InvalidOperationException("No active transaction to rollback.");

            _logger.LogWarning("[WAL-TXN] [Firebase] RollbackTransaction — discarding {Count} pending updates", _pendingUpdates.Count);
            _pendingUpdates.Clear();
            _inTransaction = false;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            // HttpClient and FirebaseApp are typically managed by DI; do not dispose.
            GC.SuppressFinalize(this);
            await Task.CompletedTask;
        }

        /// <summary>
        /// Enqueues a path update for the next multi-path batch write.
        /// </summary>
        /// <param name="path">The database path (e.g., "/workitems/abc123").</param>
        /// <param name="value">The value to write at the path.</param>
        public void EnqueueUpdate(string path, object value)
        {
            _pendingUpdates[path] = value;
        }

        /// <summary>
        /// Disposes managed resources.
        /// </summary>
        public void Dispose()
        {
            // HttpClient and FirebaseApp are typically managed by DI; do not dispose.
            GC.SuppressFinalize(this);
        }
    }
}
