// FirebaseStorageAdapter — IStorageService wrapping FirebaseRepository<T> (RTDB via REST).
// Example:
//   services.AddScoped<IStorageService, FirebaseStorageAdapter>();
using System.Text.Json;
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Data.Adapters.Firebase;

public class FirebaseStorageAdapter : IStorageService
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;

    public FirebaseStorageAdapter(HttpClient httpClient, string projectId)
    {
        _httpClient = httpClient;
        _baseUrl = $"https://{projectId}-default-rtdb.firebaseio.com";
    }

    public async Task<StorageResult<T>> StoreAsync<T>(string collection, string id, T entity, CancellationToken ct = default) where T : class
    {
        var json = JsonSerializer.Serialize(entity);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        var response = await _httpClient.PutAsync($"{_baseUrl}/{collection}/{id}.json", content, ct);
        return response.IsSuccessStatusCode
            ? StorageResult<T>.Ok(entity)
            : StorageResult<T>.Fail($"Firebase PUT failed: {response.StatusCode}");
    }

    public async Task<StorageResult<T>> RetrieveAsync<T>(string collection, string id, CancellationToken ct = default) where T : class
    {
        var response = await _httpClient.GetAsync($"{_baseUrl}/{collection}/{id}.json", ct);
        if (!response.IsSuccessStatusCode) return StorageResult<T>.Fail($"Firebase GET failed: {response.StatusCode}");
        var json = await response.Content.ReadAsStringAsync(ct);
        if (json == "null") return StorageResult<T>.Fail($"Entity '{id}' not found");
        var entity = JsonSerializer.Deserialize<T>(json);
        return entity is not null ? StorageResult<T>.Ok(entity) : StorageResult<T>.Fail("Deserialization failed");
    }

    public async Task<StorageResult<List<T>>> QueryAsync<T>(string collection, Func<T, bool>? predicate = null, CancellationToken ct = default) where T : class
    {
        var response = await _httpClient.GetAsync($"{_baseUrl}/{collection}.json", ct);
        if (!response.IsSuccessStatusCode) return StorageResult<List<T>>.Fail($"Firebase GET failed: {response.StatusCode}");
        var json = await response.Content.ReadAsStringAsync(ct);
        if (json == "null") return StorageResult<List<T>>.Ok(new List<T>());
        var dict = JsonSerializer.Deserialize<Dictionary<string, T>>(json);
        var items = dict?.Values.ToList() ?? new List<T>();
        if (predicate is not null) items = items.Where(predicate).ToList();
        return StorageResult<List<T>>.Ok(items);
    }

    public async Task<StorageResult<bool>> DeleteAsync(string collection, string id, CancellationToken ct = default)
    {
        var response = await _httpClient.DeleteAsync($"{_baseUrl}/{collection}/{id}.json", ct);
        return StorageResult<bool>.Ok(response.IsSuccessStatusCode);
    }

    public async Task<bool> ExistsAsync(string collection, string id, CancellationToken ct = default)
    {
        var result = await RetrieveAsync<object>(collection, id, ct);
        return result.Success;
    }

    public async Task<long> CountAsync(string collection, CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync($"{_baseUrl}/{collection}.json?shallow=true", ct);
        if (!response.IsSuccessStatusCode) return 0;
        var json = await response.Content.ReadAsStringAsync(ct);
        if (json == "null") return 0;
        var dict = JsonSerializer.Deserialize<Dictionary<string, bool>>(json);
        return dict?.Count ?? 0;
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
