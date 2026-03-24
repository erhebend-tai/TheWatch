// =============================================================================
// FirebaseRepository.cs — Firebase Realtime Database Repository
// =============================================================================
// Write-Ahead Log (WAL) Pattern:
//   Every mutating operation is logged BEFORE execution. Firebase RTDB operations
//   are immediately persisted (no local buffering unless offline mode is enabled).
//
// Example:
//   _logger.LogInformation("[WAL] SetAsync {EntityType} Id={Id} Path={Path}",
//       typeof(T).Name, id, $"/{_collectionPath}/{id}");
//   await _rootRef.Child(_collectionPath).Child(id).SetRawJsonAsync(json);
//   _logger.LogInformation("[WAL] SetAsync completed for {EntityType} Id={Id}",
//       typeof(T).Name, id);
//
// Firebase Realtime Database Notes:
//   - Path-based: /workitems/{id}, /milestones/{id}, etc.
//   - Data stored as JSON tree — deep nesting should be avoided
//   - Offline persistence available on mobile SDKs (not Admin SDK)
//   - Security rules enforce access at path level
//   - Fan-out pattern for denormalized writes:
//       var updates = new Dictionary<string, object> {
//           { $"/workitems/{id}", workItemJson },
//           { $"/milestones/{milestoneId}/items/{id}", true }
//       };
//       await _rootRef.UpdateChildrenAsync(updates);
//   - Max depth: 32 levels
//   - Max payload per write: 16MB
//   - Indexing: .indexOn in security rules for orderByChild queries
//
// NuGet Package: FirebaseAdmin (Google.Apis.Auth dependency)
//
// Providers checked:
//   - FirebaseDatabase.net (REST-based, third-party) — less maintained
//   - FireSharp — archived
//   - Firebase Admin SDK (.NET) — official, used here
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FirebaseAdmin;
using FirebaseAdmin.Auth;
using Google.Cloud.Firestore;
using Microsoft.Extensions.Logging;
using TheWatch.Data.Repositories;

// NOTE: Firebase Admin SDK for .NET does not include a Realtime Database client
// in the official FirebaseAdmin NuGet package. The RTDB client is available via
// the REST API or the FirebaseDatabase.net third-party package.
// This implementation uses a REST-based approach via HttpClient for RTDB operations.
// If using FirebaseDatabase.net, replace HttpClient calls with:
//   var client = new FirebaseClient("https://your-project.firebaseio.com");
//   var response = await client.Child(path).PostAsync(json);

namespace TheWatch.Data.Repositories.Firebase
{
    /// <summary>
    /// Generic repository implementation for Firebase Realtime Database.
    /// Uses path-based CRUD operations against the Firebase REST API or Admin SDK.
    /// </summary>
    /// <typeparam name="T">The entity type. Must be a reference type with an <c>Id</c> property.</typeparam>
    public class FirebaseRepository<T> : IRepository<T> where T : class
    {
        private readonly FirebaseApp _firebaseApp;
        private readonly string _collectionPath;
        private readonly string _databaseUrl;
        private readonly ILogger<FirebaseRepository<T>> _logger;
        private readonly System.Net.Http.HttpClient _httpClient;
        private readonly JsonSerializerOptions _jsonOptions;

        /// <summary>
        /// Initializes a new instance of <see cref="FirebaseRepository{T}"/>.
        /// </summary>
        /// <param name="firebaseApp">The Firebase application instance.</param>
        /// <param name="databaseUrl">The Firebase Realtime Database URL (e.g., https://project-id.firebaseio.com).</param>
        /// <param name="collectionPath">The root path for this entity type (e.g., "workitems").</param>
        /// <param name="httpClient">HTTP client for REST API calls.</param>
        /// <param name="logger">Logger for write-ahead log entries.</param>
        public FirebaseRepository(
            FirebaseApp firebaseApp,
            string databaseUrl,
            string collectionPath,
            System.Net.Http.HttpClient httpClient,
            ILogger<FirebaseRepository<T>> logger)
        {
            _firebaseApp = firebaseApp ?? throw new ArgumentNullException(nameof(firebaseApp));
            _databaseUrl = databaseUrl?.TrimEnd('/') ?? throw new ArgumentNullException(nameof(databaseUrl));
            _collectionPath = collectionPath ?? throw new ArgumentNullException(nameof(collectionPath));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true
            };
        }

        private string BuildUrl(string? childPath = null)
        {
            var path = childPath is null
                ? $"{_databaseUrl}/{_collectionPath}.json"
                : $"{_databaseUrl}/{_collectionPath}/{childPath}.json";
            return path;
        }

        /// <inheritdoc />
        public async Task<T?> GetByIdAsync(string id, CancellationToken ct = default)
        {
            _logger.LogDebug("[WAL-READ] [Firebase] GET /{CollectionPath}/{Id}", _collectionPath, id);
            var response = await _httpClient.GetAsync(BuildUrl(id), ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[WAL-READ] [Firebase] GET /{CollectionPath}/{Id} failed — Status={Status}",
                    _collectionPath, id, response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            if (json == "null") return null;

            return JsonSerializer.Deserialize<T>(json, _jsonOptions);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default)
        {
            _logger.LogDebug("[WAL-READ] [Firebase] GET /{CollectionPath}", _collectionPath);
            var response = await _httpClient.GetAsync(BuildUrl(), ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            if (json == "null") return Array.Empty<T>();

            var dict = JsonSerializer.Deserialize<Dictionary<string, T>>(json, _jsonOptions);
            return dict?.Values.ToList().AsReadOnly() ?? (IReadOnlyList<T>)Array.Empty<T>();
        }

        /// <inheritdoc />
        /// <remarks>
        /// Firebase Realtime Database does not support arbitrary query expressions.
        /// This implementation fetches all items and filters in-memory.
        /// For production use, consider:
        ///   - orderByChild + equalTo for simple equality filters
        ///   - Denormalized indexes for frequently queried fields
        ///   - Moving complex queries to Cloud Firestore instead
        /// </remarks>
        public async Task<IReadOnlyList<T>> FindAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default)
        {
            _logger.LogDebug("[WAL-READ] [Firebase] Find {EntityType} — fetching all and filtering in-memory", typeof(T).Name);
            var all = await GetAllAsync(ct);
            var compiled = predicate.Compile();
            return all.Where(compiled).ToList().AsReadOnly();
        }

        /// <inheritdoc />
        public async Task<T> AddAsync(T entity, CancellationToken ct = default)
        {
            var id = GetEntityId(entity);
            _logger.LogInformation("[WAL] [Firebase] PUT /{CollectionPath}/{Id}", _collectionPath, id);

            var json = JsonSerializer.Serialize(entity, _jsonOptions);
            var content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json");
            var response = await _httpClient.PutAsync(BuildUrl(id), content, ct);
            response.EnsureSuccessStatusCode();

            _logger.LogInformation("[WAL] [Firebase] PUT /{CollectionPath}/{Id} completed", _collectionPath, id);
            return entity;
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<T>> AddRangeAsync(IEnumerable<T> entities, CancellationToken ct = default)
        {
            var list = entities as IList<T> ?? entities.ToList();
            _logger.LogInformation("[WAL] [Firebase] AddRange {EntityType} Count={Count}", typeof(T).Name, list.Count);

            // Use multi-path update for atomicity
            var updates = new Dictionary<string, object>();
            foreach (var entity in list)
            {
                var id = GetEntityId(entity);
                updates[$"{id}"] = entity;
            }

            var json = JsonSerializer.Serialize(updates, _jsonOptions);
            var content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json");
            var response = await _httpClient.PatchAsync(BuildUrl(), content, ct);
            response.EnsureSuccessStatusCode();

            _logger.LogInformation("[WAL] [Firebase] AddRange {EntityType} completed", typeof(T).Name);
            return list.AsReadOnly();
        }

        /// <inheritdoc />
        public async Task UpdateAsync(T entity, CancellationToken ct = default)
        {
            var id = GetEntityId(entity);
            _logger.LogInformation("[WAL] [Firebase] PUT (update) /{CollectionPath}/{Id}", _collectionPath, id);

            var json = JsonSerializer.Serialize(entity, _jsonOptions);
            var content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json");
            var response = await _httpClient.PutAsync(BuildUrl(id), content, ct);
            response.EnsureSuccessStatusCode();

            _logger.LogInformation("[WAL] [Firebase] PUT (update) /{CollectionPath}/{Id} completed", _collectionPath, id);
        }

        /// <inheritdoc />
        public async Task DeleteAsync(string id, CancellationToken ct = default)
        {
            _logger.LogInformation("[WAL] [Firebase] DELETE /{CollectionPath}/{Id}", _collectionPath, id);

            var response = await _httpClient.DeleteAsync(BuildUrl(id), ct);
            response.EnsureSuccessStatusCode();

            _logger.LogInformation("[WAL] [Firebase] DELETE /{CollectionPath}/{Id} completed", _collectionPath, id);
        }

        /// <inheritdoc />
        public async Task<long> CountAsync(CancellationToken ct = default)
        {
            _logger.LogDebug("[WAL-READ] [Firebase] Count {EntityType} — shallow query", typeof(T).Name);
            // Firebase shallow query: ?shallow=true returns keys only
            var url = $"{_databaseUrl}/{_collectionPath}.json?shallow=true";
            var response = await _httpClient.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            if (json == "null") return 0;

            var dict = JsonSerializer.Deserialize<Dictionary<string, bool>>(json, _jsonOptions);
            return dict?.Count ?? 0;
        }

        /// <inheritdoc />
        public async Task<bool> ExistsAsync(string id, CancellationToken ct = default)
        {
            var entity = await GetByIdAsync(id, ct);
            return entity is not null;
        }

        private static string GetEntityId(T entity)
        {
            var idProp = typeof(T).GetProperty("Id",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
            if (idProp is null)
                throw new InvalidOperationException($"Entity type {typeof(T).Name} does not have an 'Id' property.");
            return idProp.GetValue(entity)?.ToString()
                ?? throw new InvalidOperationException($"Entity {typeof(T).Name} has a null Id.");
        }
    }
}
