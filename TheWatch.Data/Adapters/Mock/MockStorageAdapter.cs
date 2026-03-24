// MockStorageAdapter — ConcurrentDictionary-backed IStorageService for development/testing.
// Supports offline queue FIFO, no cloud credentials needed.
// Example:
//   services.AddSingleton<IStorageService, MockStorageAdapter>();
//   var result = await storage.StoreAsync("alerts", "a-1", new Alert { ... });
using System.Collections.Concurrent;
using System.Text.Json;
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Data.Adapters.Mock;

public class MockStorageAdapter : IStorageService
{
    // Key: "{collection}:{id}" → serialized JSON
    private readonly ConcurrentDictionary<string, string> _store = new();
    private readonly ConcurrentQueue<OfflineQueueEntry> _offlineQueue = new();
    private readonly ConcurrentDictionary<string, bool> _syncedEntries = new();

    private static string Key(string collection, string id) => $"{collection}:{id}";

    public Task<StorageResult<T>> StoreAsync<T>(string collection, string id, T entity, CancellationToken ct = default) where T : class
    {
        var json = JsonSerializer.Serialize(entity);
        _store[Key(collection, id)] = json;
        return Task.FromResult(StorageResult<T>.Ok(entity, Guid.NewGuid().ToString()));
    }

    public Task<StorageResult<T>> RetrieveAsync<T>(string collection, string id, CancellationToken ct = default) where T : class
    {
        if (_store.TryGetValue(Key(collection, id), out var json))
        {
            var entity = JsonSerializer.Deserialize<T>(json);
            return Task.FromResult(entity is not null
                ? StorageResult<T>.Ok(entity)
                : StorageResult<T>.Fail("Deserialization returned null"));
        }
        return Task.FromResult(StorageResult<T>.Fail($"Entity '{id}' not found in '{collection}'"));
    }

    public Task<StorageResult<List<T>>> QueryAsync<T>(string collection, Func<T, bool>? predicate = null, CancellationToken ct = default) where T : class
    {
        var prefix = $"{collection}:";
        var results = _store
            .Where(kv => kv.Key.StartsWith(prefix))
            .Select(kv => JsonSerializer.Deserialize<T>(kv.Value))
            .Where(e => e is not null)
            .Cast<T>()
            .Where(e => predicate is null || predicate(e))
            .ToList();
        return Task.FromResult(StorageResult<List<T>>.Ok(results));
    }

    public Task<StorageResult<bool>> DeleteAsync(string collection, string id, CancellationToken ct = default)
    {
        var removed = _store.TryRemove(Key(collection, id), out _);
        return Task.FromResult(StorageResult<bool>.Ok(removed));
    }

    public Task<bool> ExistsAsync(string collection, string id, CancellationToken ct = default) =>
        Task.FromResult(_store.ContainsKey(Key(collection, id)));

    public Task<long> CountAsync(string collection, CancellationToken ct = default)
    {
        var prefix = $"{collection}:";
        var count = _store.Keys.Count(k => k.StartsWith(prefix));
        return Task.FromResult((long)count);
    }

    public Task EnqueueOfflineAsync(OfflineQueueEntry entry, CancellationToken ct = default)
    {
        _offlineQueue.Enqueue(entry);
        return Task.CompletedTask;
    }

    public Task<List<OfflineQueueEntry>> GetPendingQueueAsync(CancellationToken ct = default)
    {
        var pending = _offlineQueue.Where(e => !_syncedEntries.ContainsKey(e.Id)).ToList();
        return Task.FromResult(pending);
    }

    public Task MarkSyncedAsync(string entryId, CancellationToken ct = default)
    {
        _syncedEntries[entryId] = true;
        return Task.CompletedTask;
    }
}
