// CosmosDbStorageAdapter — IStorageService wrapping CosmosDbRepository<T>.
// Uses native Cosmos SDK Container operations. Partition key = id by default.
// Example:
//   services.AddScoped<IStorageService, CosmosDbStorageAdapter>();
using System.Text.Json;
using Microsoft.Azure.Cosmos;
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Data.Adapters.CosmosDb;

public class CosmosDbStorageAdapter : IStorageService
{
    private readonly CosmosClient _client;
    private readonly string _databaseId;

    public CosmosDbStorageAdapter(CosmosClient client, string databaseId = "TheWatch")
    {
        _client = client;
        _databaseId = databaseId;
    }

    private Container GetContainer(string collection) => _client.GetContainer(_databaseId, collection);

    public async Task<StorageResult<T>> StoreAsync<T>(string collection, string id, T entity, CancellationToken ct = default) where T : class
    {
        var container = GetContainer(collection);
        var response = await container.UpsertItemAsync(entity, new PartitionKey(id), cancellationToken: ct);
        return StorageResult<T>.Ok(response.Resource, response.ETag);
    }

    public async Task<StorageResult<T>> RetrieveAsync<T>(string collection, string id, CancellationToken ct = default) where T : class
    {
        try
        {
            var container = GetContainer(collection);
            var response = await container.ReadItemAsync<T>(id, new PartitionKey(id), cancellationToken: ct);
            return StorageResult<T>.Ok(response.Resource, response.ETag);
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return StorageResult<T>.Fail($"Entity '{id}' not found in '{collection}'");
        }
    }

    public async Task<StorageResult<List<T>>> QueryAsync<T>(string collection, Func<T, bool>? predicate = null, CancellationToken ct = default) where T : class
    {
        var container = GetContainer(collection);
        var query = container.GetItemQueryIterator<T>();
        var results = new List<T>();
        while (query.HasMoreResults)
        {
            var response = await query.ReadNextAsync(ct);
            results.AddRange(response);
        }
        if (predicate is not null)
            results = results.Where(predicate).ToList();
        return StorageResult<List<T>>.Ok(results);
    }

    public async Task<StorageResult<bool>> DeleteAsync(string collection, string id, CancellationToken ct = default)
    {
        try
        {
            var container = GetContainer(collection);
            await container.DeleteItemAsync<object>(id, new PartitionKey(id), cancellationToken: ct);
            return StorageResult<bool>.Ok(true);
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return StorageResult<bool>.Ok(false);
        }
    }

    public async Task<bool> ExistsAsync(string collection, string id, CancellationToken ct = default)
    {
        var result = await RetrieveAsync<object>(collection, id, ct);
        return result.Success;
    }

    public async Task<long> CountAsync(string collection, CancellationToken ct = default)
    {
        var container = GetContainer(collection);
        var query = container.GetItemQueryIterator<int>("SELECT VALUE COUNT(1) FROM c");
        var response = await query.ReadNextAsync(ct);
        return response.FirstOrDefault();
    }

    public Task EnqueueOfflineAsync(OfflineQueueEntry entry, CancellationToken ct = default) =>
        StoreAsync("offline_queue", entry.Id, entry, ct).ContinueWith(_ => { }, ct);

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
