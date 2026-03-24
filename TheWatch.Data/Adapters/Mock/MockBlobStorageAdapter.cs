// MockBlobStorageAdapter — ConcurrentDictionary-backed IBlobStoragePort for dev/testing.
// Stores blobs in memory as byte arrays. No cloud credentials needed.
// GetDownloadUrlAsync returns a base64 data URI for immediate rendering in-browser.
//
// Example:
//   services.AddSingleton<IBlobStoragePort, MockBlobStorageAdapter>();
//   var result = await blobStorage.UploadAsync("evidence", "img-001.jpg", stream, "image/jpeg", ct);
//   var url = await blobStorage.GetDownloadUrlAsync("evidence", "img-001.jpg", TimeSpan.FromMinutes(15), ct);

using System.Collections.Concurrent;
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Data.Adapters.Mock;

public class MockBlobStorageAdapter : IBlobStoragePort
{
    // Key: "{container}:{blobName}" → raw bytes
    private readonly ConcurrentDictionary<string, byte[]> _blobs = new();
    private readonly ConcurrentDictionary<string, string> _mimeTypes = new();

    private static string Key(string container, string blobName) => $"{container}:{blobName}";

    public async Task<StorageResult<string>> UploadAsync(string container, string blobName, Stream content, string mimeType, CancellationToken ct = default)
    {
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, ct);
        var key = Key(container, blobName);
        _blobs[key] = ms.ToArray();
        _mimeTypes[key] = mimeType;
        return StorageResult<string>.Ok($"{container}/{blobName}");
    }

    public Task<StorageResult<Stream>> DownloadAsync(string container, string blobName, CancellationToken ct = default)
    {
        var key = Key(container, blobName);
        if (_blobs.TryGetValue(key, out var bytes))
        {
            Stream stream = new MemoryStream(bytes, writable: false);
            return Task.FromResult(StorageResult<Stream>.Ok(stream));
        }
        return Task.FromResult(StorageResult<Stream>.Fail($"Blob '{blobName}' not found in container '{container}'"));
    }

    public Task<StorageResult<bool>> DeleteAsync(string container, string blobName, CancellationToken ct = default)
    {
        var key = Key(container, blobName);
        var removed = _blobs.TryRemove(key, out _);
        _mimeTypes.TryRemove(key, out _);
        return Task.FromResult(StorageResult<bool>.Ok(removed));
    }

    public Task<string> GetDownloadUrlAsync(string container, string blobName, TimeSpan expiry, CancellationToken ct = default)
    {
        var key = Key(container, blobName);
        if (_blobs.TryGetValue(key, out var bytes))
        {
            var mime = _mimeTypes.TryGetValue(key, out var m) ? m : "application/octet-stream";
            var dataUri = $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
            return Task.FromResult(dataUri);
        }
        return Task.FromResult($"mock://not-found/{container}/{blobName}");
    }

    public Task<bool> ExistsAsync(string container, string blobName, CancellationToken ct = default) =>
        Task.FromResult(_blobs.ContainsKey(Key(container, blobName)));
}
