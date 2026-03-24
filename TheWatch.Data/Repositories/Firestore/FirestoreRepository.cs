// =============================================================================
// FirestoreRepository.cs — Google Cloud Firestore repository.
// =============================================================================
// Write-Ahead Log (WAL) Pattern:
//   Firestore provides real-time snapshot listeners and server-side
//   timestamps that form a built-in mutation log. Application-level WAL
//   logging adds intent tracking.
//
// Example:
//   services.AddScoped(typeof(IRepository<>), typeof(FirestoreRepository<>));
//
// Providers checked:
//   - Google.Cloud.Firestore SDK — used directly
//   - Firebase Admin SDK (Firestore module) — same underlying SDK
//   - FirestoreData attributes — used for serialization
// =============================================================================

using System.Linq.Expressions;
using Google.Cloud.Firestore;
using Microsoft.Extensions.Logging;

namespace TheWatch.Data.Repositories.Firestore;

/// <summary>
/// Generic Firestore repository. Each entity type maps to a collection
/// named after <typeparamref name="T"/> (lowercased + pluralised).
/// </summary>
/// <typeparam name="T">The entity type. Must be a reference type.</typeparam>
public class FirestoreRepository<T> : IRepository<T> where T : class
{
    private readonly FirestoreDb _firestoreDb;
    private readonly CollectionReference _collection;
    private readonly ILogger<FirestoreRepository<T>> _logger;
    private readonly string _collectionName;

    public FirestoreRepository(FirestoreDb firestoreDb, ILogger<FirestoreRepository<T>> logger)
    {
        _firestoreDb = firestoreDb ?? throw new ArgumentNullException(nameof(firestoreDb));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _collectionName = typeof(T).Name.ToLowerInvariant() + "s";
        _collection = _firestoreDb.Collection(_collectionName);
    }

    /// <inheritdoc />
    public async Task<T?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        _logger.LogDebug("[WAL-READ-FIRESTORE] GetById {EntityType} Id={Id}", typeof(T).Name, id);
        var snapshot = await _collection.Document(id).GetSnapshotAsync(ct);
        return snapshot.Exists ? snapshot.ConvertTo<T>() : null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default)
    {
        _logger.LogDebug("[WAL-READ-FIRESTORE] GetAll {EntityType}", typeof(T).Name);
        var snapshot = await _collection.GetSnapshotAsync(ct);
        return snapshot.Documents.Select(d => d.ConvertTo<T>()).ToList().AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<T>> FindAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default)
    {
        _logger.LogDebug("[WAL-READ-FIRESTORE] Find {EntityType} — client-side filter (complex queries need Firestore-native where clauses)", typeof(T).Name);
        var all = await GetAllAsync(ct);
        var compiled = predicate.Compile();
        return all.Where(compiled).ToList().AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<T> AddAsync(T entity, CancellationToken ct = default)
    {
        _logger.LogInformation("[WAL-FIRESTORE] Adding {EntityType}", typeof(T).Name);
        var idProp = typeof(T).GetProperty("Id") ?? typeof(T).GetProperty("id");
        var id = idProp?.GetValue(entity)?.ToString() ?? Guid.NewGuid().ToString();
        await _collection.Document(id).SetAsync(entity, cancellationToken: ct);
        _logger.LogInformation("[WAL-FIRESTORE] Added {EntityType} Id={Id} OK", typeof(T).Name, id);
        return entity;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<T>> AddRangeAsync(IEnumerable<T> entities, CancellationToken ct = default)
    {
        var list = entities.ToList();
        _logger.LogInformation("[WAL-FIRESTORE] AddRange {EntityType} Count={Count}", typeof(T).Name, list.Count);
        var batch = _firestoreDb.StartBatch();
        foreach (var entity in list)
        {
            var idProp = typeof(T).GetProperty("Id") ?? typeof(T).GetProperty("id");
            var id = idProp?.GetValue(entity)?.ToString() ?? Guid.NewGuid().ToString();
            batch.Set(_collection.Document(id), entity);
        }
        await batch.CommitAsync(ct);
        _logger.LogInformation("[WAL-FIRESTORE] AddRange {EntityType} OK", typeof(T).Name);
        return list.AsReadOnly();
    }

    /// <inheritdoc />
    public async Task UpdateAsync(T entity, CancellationToken ct = default)
    {
        _logger.LogInformation("[WAL-FIRESTORE] Updating {EntityType}", typeof(T).Name);
        var idProp = typeof(T).GetProperty("Id") ?? typeof(T).GetProperty("id");
        var id = idProp?.GetValue(entity)?.ToString() ?? throw new InvalidOperationException("Entity must have an Id property");
        await _collection.Document(id).SetAsync(entity, SetOptions.MergeAll, ct);
        _logger.LogInformation("[WAL-FIRESTORE] Updated {EntityType} OK", typeof(T).Name);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        _logger.LogInformation("[WAL-FIRESTORE] Deleting {EntityType} Id={Id}", typeof(T).Name, id);
        await _collection.Document(id).DeleteAsync(cancellationToken: ct);
        _logger.LogInformation("[WAL-FIRESTORE] Deleted {EntityType} Id={Id} OK", typeof(T).Name, id);
    }

    /// <inheritdoc />
    public async Task<long> CountAsync(CancellationToken ct = default)
    {
        _logger.LogDebug("[WAL-READ-FIRESTORE] Count {EntityType}", typeof(T).Name);
        var snapshot = await _collection.GetSnapshotAsync(ct);
        return snapshot.Count;
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(string id, CancellationToken ct = default)
    {
        var snapshot = await _collection.Document(id).GetSnapshotAsync(ct);
        return snapshot.Exists;
    }
}
