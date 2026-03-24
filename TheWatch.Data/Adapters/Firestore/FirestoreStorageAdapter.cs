// FirestoreStorageAdapter — IStorageService wrapping Google Cloud Firestore.
// Example:
//   services.AddScoped<IStorageService, FirestoreStorageAdapter>();
using System.Text.Json;
using Google.Cloud.Firestore;
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Data.Adapters.Firestore;

public class FirestoreStorageAdapter : IStorageService
{
    private readonly FirestoreDb _db;

    public FirestoreStorageAdapter(FirestoreDb db) => _db = db;

    public async Task<StorageResult<T>> StoreAsync<T>(string collection, string id, T entity, CancellationToken ct = default) where T : class
    {
        var docRef = _db.Collection(collection).Document(id);
        var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(JsonSerializer.Serialize(entity));
        if (dict is null) return StorageResult<T>.Fail("Serialization failed");
        await docRef.SetAsync(dict, cancellationToken: ct);
        return StorageResult<T>.Ok(entity);
    }

    public async Task<StorageResult<T>> RetrieveAsync<T>(string collection, string id, CancellationToken ct = default) where T : class
    {
        var docRef = _db.Collection(collection).Document(id);
        var snapshot = await docRef.GetSnapshotAsync(ct);
        if (!snapshot.Exists) return StorageResult<T>.Fail($"Entity '{id}' not found in '{collection}'");
        var json = JsonSerializer.Serialize(snapshot.ToDictionary());
        var entity = JsonSerializer.Deserialize<T>(json);
        return entity is not null ? StorageResult<T>.Ok(entity) : StorageResult<T>.Fail("Deserialization failed");
    }

    public async Task<StorageResult<List<T>>> QueryAsync<T>(string collection, Func<T, bool>? predicate = null, CancellationToken ct = default) where T : class
    {
        var snapshot = await _db.Collection(collection).GetSnapshotAsync(ct);
        var items = snapshot.Documents
            .Select(d => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(d.ToDictionary())))
            .Where(e => e is not null)
            .Cast<T>()
            .Where(e => predicate is null || predicate(e))
            .ToList();
        return StorageResult<List<T>>.Ok(items);
    }

    public async Task<StorageResult<bool>> DeleteAsync(string collection, string id, CancellationToken ct = default)
    {
        var docRef = _db.Collection(collection).Document(id);
        await docRef.DeleteAsync(cancellationToken: ct);
        return StorageResult<bool>.Ok(true);
    }

    public async Task<bool> ExistsAsync(string collection, string id, CancellationToken ct = default)
    {
        var snapshot = await _db.Collection(collection).Document(id).GetSnapshotAsync(ct);
        return snapshot.Exists;
    }

    public async Task<long> CountAsync(string collection, CancellationToken ct = default)
    {
        var snapshot = await _db.Collection(collection).GetSnapshotAsync(ct);
        return snapshot.Count;
    }

    public async Task EnqueueOfflineAsync(OfflineQueueEntry entry, CancellationToken ct = default) =>
        await StoreAsync("offline_queue", entry.Id, entry, ct);

    public async Task<List<OfflineQueueEntry>> GetPendingQueueAsync(CancellationToken ct = default)
    {
        var result = await QueryAsync<OfflineQueueEntry>("offline_queue", e => !e.IsSynced, ct);
        return result.Data ?? new List<OfflineQueueEntry>();
    }

    public async Task MarkSyncedAsync(string entryId, CancellationToken ct = default)
    {
        var result = await RetrieveAsync<OfflineQueueEntry>("offline_queue", entryId, ct);
        if (result is { Success: true, Data: not null })
        {
            result.Data.IsSynced = true;
            await StoreAsync("offline_queue", entryId, result.Data, ct);
        }
    }
}
