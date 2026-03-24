// IStorageService — domain port for all persistence operations.
// NO database SDK imports allowed in this file. Adapters implement this.
// Example:
//   var result = await storage.StoreAsync("alerts", "a-1", myAlert);
//   var items = await storage.QueryAsync<WorkItem>("workitems", w => w.Status == Open);
using TheWatch.Shared.Domain.Models;

namespace TheWatch.Shared.Domain.Ports;

public interface IStorageService
{
    Task<StorageResult<T>> StoreAsync<T>(string collection, string id, T entity, CancellationToken ct = default) where T : class;
    Task<StorageResult<T>> RetrieveAsync<T>(string collection, string id, CancellationToken ct = default) where T : class;
    Task<StorageResult<List<T>>> QueryAsync<T>(string collection, Func<T, bool>? predicate = null, CancellationToken ct = default) where T : class;
    Task<StorageResult<bool>> DeleteAsync(string collection, string id, CancellationToken ct = default);
    Task<bool> ExistsAsync(string collection, string id, CancellationToken ct = default);
    Task<long> CountAsync(string collection, CancellationToken ct = default);
    Task EnqueueOfflineAsync(OfflineQueueEntry entry, CancellationToken ct = default);
    Task<List<OfflineQueueEntry>> GetPendingQueueAsync(CancellationToken ct = default);
    Task MarkSyncedAsync(string entryId, CancellationToken ct = default);
}
