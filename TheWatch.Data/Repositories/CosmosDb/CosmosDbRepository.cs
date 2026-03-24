// =============================================================================
// CosmosDbRepository.cs — Azure Cosmos DB Repository (Native SDK, NOT EF Core)
// =============================================================================
// Write-Ahead Log (WAL) Pattern:
//   Every mutating operation logs: operation type, item id, partition key,
//   and RU charge BEFORE and AFTER execution.
//
// Example:
//   _logger.LogInformation("[WAL] CreateItem {EntityType} Id={Id} PartitionKey={Pk}",
//       typeof(T).Name, entity.Id, entity.Id);
//   var response = await _container.CreateItemAsync(entity, pk, cancellationToken: ct);
//   _logger.LogInformation("[WAL] CreateItem completed — RU={RuCharge}",
//       response.RequestCharge);
//
// Cosmos DB-Specific Notes:
//   - Partition key is set to /id by default; override via constructor
//   - Cross-partition queries are expensive; prefer single-partition reads
//   - FindAsync converts simple expressions to SQL; complex LINQ not supported
//   - Point reads (ReadItemAsync) are 1 RU for 1KB items — always prefer
//   - TransactionalBatch operates within a single logical partition
//   - Change Feed can be used for event-driven architectures
//   - Time-to-live (TTL) settable per item or container
//   - Hierarchical partition keys supported in SDK v3.33+
//   - Integrated cache available for dedicated gateway mode
//
// Providers checked:
//   - Cosmonaut — archived/unmaintained
//   - EF Core Cosmos — limited query translation, no batch support
//   - Azure.Cosmos (v4 preview) — not GA, not recommended yet
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using Microsoft.Extensions.Logging;
using TheWatch.Data.Repositories;

namespace TheWatch.Data.Repositories.CosmosDb
{
    /// <summary>
    /// Generic repository implementation backed by Azure Cosmos DB using the native
    /// <see cref="Microsoft.Azure.Cosmos"/> SDK v3. Uses direct container operations
    /// for optimal performance and RU efficiency.
    /// </summary>
    /// <typeparam name="T">The entity type. Must be a reference type with an <c>Id</c> property.</typeparam>
    public class CosmosDbRepository<T> : IRepository<T> where T : class
    {
        private readonly Container _container;
        private readonly ILogger<CosmosDbRepository<T>> _logger;

        /// <summary>
        /// Initializes a new instance of <see cref="CosmosDbRepository{T}"/>.
        /// </summary>
        /// <param name="cosmosClient">The Cosmos DB client instance.</param>
        /// <param name="databaseName">The database name.</param>
        /// <param name="containerName">The container (collection) name.</param>
        /// <param name="logger">Logger for write-ahead log entries and diagnostics.</param>
        public CosmosDbRepository(
            CosmosClient cosmosClient,
            string databaseName,
            string containerName,
            ILogger<CosmosDbRepository<T>> logger)
        {
            if (cosmosClient is null) throw new ArgumentNullException(nameof(cosmosClient));
            if (string.IsNullOrWhiteSpace(databaseName)) throw new ArgumentException("Database name required.", nameof(databaseName));
            if (string.IsNullOrWhiteSpace(containerName)) throw new ArgumentException("Container name required.", nameof(containerName));

            _container = cosmosClient.GetContainer(databaseName, containerName);
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public async Task<T?> GetByIdAsync(string id, CancellationToken ct = default)
        {
            _logger.LogDebug("[WAL-READ] [CosmosDB] ReadItem {EntityType} Id={Id}", typeof(T).Name, id);
            try
            {
                var response = await _container.ReadItemAsync<T>(id, new PartitionKey(id), cancellationToken: ct);
                _logger.LogDebug("[WAL-READ] [CosmosDB] ReadItem completed — RU={RuCharge}", response.RequestCharge);
                return response.Resource;
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogDebug("[WAL-READ] [CosmosDB] ReadItem {EntityType} Id={Id} — not found", typeof(T).Name, id);
                return null;
            }
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default)
        {
            _logger.LogDebug("[WAL-READ] [CosmosDB] GetAll {EntityType}", typeof(T).Name);
            var query = _container.GetItemQueryIterator<T>(new QueryDefinition("SELECT * FROM c"));
            var results = new List<T>();
            double totalRu = 0;

            while (query.HasMoreResults)
            {
                var response = await query.ReadNextAsync(ct);
                totalRu += response.RequestCharge;
                results.AddRange(response);
            }

            _logger.LogDebug("[WAL-READ] [CosmosDB] GetAll {EntityType} returned {Count} items — TotalRU={RuCharge}",
                typeof(T).Name, results.Count, totalRu);
            return results.AsReadOnly();
        }

        /// <inheritdoc />
        /// <remarks>
        /// Basic expression-to-SQL conversion. Supports simple equality predicates.
        /// For complex queries, consider using the Cosmos DB SDK's LINQ provider directly
        /// via <c>container.GetItemLinqQueryable</c>.
        ///
        /// Limitations:
        ///   - Only simple member-access == constant expressions are translated
        ///   - Complex predicates (Contains, StartsWith, nested objects) require manual SQL
        ///   - Cross-partition fan-out queries will consume more RU
        /// </remarks>
        public async Task<IReadOnlyList<T>> FindAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default)
        {
            _logger.LogDebug("[WAL-READ] [CosmosDB] Find {EntityType} with predicate", typeof(T).Name);

            // Use the built-in LINQ provider for Cosmos DB which handles expression translation
            var queryable = _container.GetItemLinqQueryable<T>(allowSynchronousQueryExecution: false)
                .Where(predicate);

            var iterator = queryable.ToFeedIterator();
            var results = new List<T>();
            double totalRu = 0;

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync(ct);
                totalRu += response.RequestCharge;
                results.AddRange(response);
            }

            _logger.LogDebug("[WAL-READ] [CosmosDB] Find {EntityType} returned {Count} items — TotalRU={RuCharge}",
                typeof(T).Name, results.Count, totalRu);
            return results.AsReadOnly();
        }

        /// <inheritdoc />
        public async Task<T> AddAsync(T entity, CancellationToken ct = default)
        {
            var id = GetEntityId(entity);
            _logger.LogInformation("[WAL] [CosmosDB] CreateItem {EntityType} Id={Id}", typeof(T).Name, id);

            var response = await _container.CreateItemAsync(entity, new PartitionKey(id), cancellationToken: ct);

            _logger.LogInformation("[WAL] [CosmosDB] CreateItem {EntityType} Id={Id} completed — RU={RuCharge}",
                typeof(T).Name, id, response.RequestCharge);
            return response.Resource;
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<T>> AddRangeAsync(IEnumerable<T> entities, CancellationToken ct = default)
        {
            var list = entities as IList<T> ?? entities.ToList();
            _logger.LogInformation("[WAL] [CosmosDB] AddRange {EntityType} Count={Count}", typeof(T).Name, list.Count);

            var results = new List<T>();
            double totalRu = 0;

            // Cosmos DB does not have a native bulk-insert outside of TransactionalBatch
            // (which requires same partition key). Execute individual creates.
            // For true bulk, consider using BulkExecution mode on CosmosClientOptions.
            foreach (var entity in list)
            {
                ct.ThrowIfCancellationRequested();
                var id = GetEntityId(entity);
                var response = await _container.CreateItemAsync(entity, new PartitionKey(id), cancellationToken: ct);
                totalRu += response.RequestCharge;
                results.Add(response.Resource);
            }

            _logger.LogInformation("[WAL] [CosmosDB] AddRange {EntityType} completed — TotalRU={RuCharge}", typeof(T).Name, totalRu);
            return results.AsReadOnly();
        }

        /// <inheritdoc />
        public async Task UpdateAsync(T entity, CancellationToken ct = default)
        {
            var id = GetEntityId(entity);
            _logger.LogInformation("[WAL] [CosmosDB] ReplaceItem {EntityType} Id={Id}", typeof(T).Name, id);

            var response = await _container.ReplaceItemAsync(entity, id, new PartitionKey(id), cancellationToken: ct);

            _logger.LogInformation("[WAL] [CosmosDB] ReplaceItem {EntityType} Id={Id} completed — RU={RuCharge}",
                typeof(T).Name, id, response.RequestCharge);
        }

        /// <inheritdoc />
        public async Task DeleteAsync(string id, CancellationToken ct = default)
        {
            _logger.LogInformation("[WAL] [CosmosDB] DeleteItem {EntityType} Id={Id}", typeof(T).Name, id);

            try
            {
                var response = await _container.DeleteItemAsync<T>(id, new PartitionKey(id), cancellationToken: ct);
                _logger.LogInformation("[WAL] [CosmosDB] DeleteItem {EntityType} Id={Id} completed — RU={RuCharge}",
                    typeof(T).Name, id, response.RequestCharge);
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogWarning("[WAL] [CosmosDB] DeleteItem {EntityType} Id={Id} — not found, no-op", typeof(T).Name, id);
            }
        }

        /// <inheritdoc />
        public async Task<long> CountAsync(CancellationToken ct = default)
        {
            _logger.LogDebug("[WAL-READ] [CosmosDB] Count {EntityType}", typeof(T).Name);
            var query = _container.GetItemQueryIterator<int>(
                new QueryDefinition("SELECT VALUE COUNT(1) FROM c"));

            long count = 0;
            while (query.HasMoreResults)
            {
                var response = await query.ReadNextAsync(ct);
                count += response.FirstOrDefault();
            }

            return count;
        }

        /// <inheritdoc />
        public async Task<bool> ExistsAsync(string id, CancellationToken ct = default)
        {
            _logger.LogDebug("[WAL-READ] [CosmosDB] Exists {EntityType} Id={Id}", typeof(T).Name, id);
            try
            {
                await _container.ReadItemAsync<T>(id, new PartitionKey(id), cancellationToken: ct);
                return true;
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return false;
            }
        }

        /// <summary>
        /// Extracts the Id property value from an entity using reflection.
        /// Expects a property named "Id" (case-insensitive).
        /// </summary>
        private static string GetEntityId(T entity)
        {
            var idProp = typeof(T).GetProperty("Id", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
            if (idProp is null)
                throw new InvalidOperationException($"Entity type {typeof(T).Name} does not have an 'Id' property.");

            return idProp.GetValue(entity)?.ToString()
                ?? throw new InvalidOperationException($"Entity {typeof(T).Name} has a null Id.");
        }
    }
}
