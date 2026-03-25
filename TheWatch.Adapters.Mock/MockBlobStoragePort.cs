// =============================================================================
// MockBlobStoragePort — In-memory mock adapter for IBlobStoragePort.
// =============================================================================
// Stores blobs in a ConcurrentDictionary keyed by "container/blobName".
// Generates mock download URLs as data URIs for small content or localhost URLs
// for larger blobs. Fully functional for Development/Staging.
//
// Example:
//   var port = new MockBlobStoragePort(logger);
//   var result = await port.UploadAsync("evidence", "req-123/img.jpg", stream, "image/jpeg");
//   // result.Data == "req-123/img.jpg" (the blob reference key)
//   var url = await port.GetDownloadUrlAsync("evidence", "req-123/img.jpg", TimeSpan.FromMinutes(15));
//   // url == "https://mock-blob.thewatch.local/evidence/req-123/img.jpg?expiry=..."
//
// NOTE: In-memory storage is not persisted across process restarts.
// For durable mock storage, use a file-backed adapter (not implemented here).
// =============================================================================

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using TheWatch.Shared.Domain.Ports;
using TheWatch.Shared.Domain.Models;

namespace TheWatch.Adapters.Mock;

public class MockBlobStoragePort : IBlobStoragePort
{
    private readonly ILogger<MockBlobStoragePort> _logger;
    private readonly ConcurrentDictionary<string, (byte[] Content, string MimeType, DateTime UploadedAt)> _blobs = new();

    public MockBlobStoragePort(ILogger<MockBlobStoragePort> logger)
    {
        _logger = logger;
    }

    private static string MakeKey(string container, string blobName) => $"{container}/{blobName}";

    /// <summary>Upload binary content. Returns the blob reference key on success.</summary>
    public async Task<StorageResult<string>> UploadAsync(string container, string blobName, Stream content, string mimeType, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, ct);
        var bytes = ms.ToArray();

        var key = MakeKey(container, blobName);
        _blobs[key] = (bytes, mimeType, DateTime.UtcNow);

        _logger.LogInformation(
            "[MOCK BLOB] Uploaded: {Key}, Size={Size} bytes, MimeType={MimeType}",
            key, bytes.Length, mimeType);

        return StorageResult<string>.Ok(blobName);
    }

    /// <summary>Download binary content as a readable stream.</summary>
    public Task<StorageResult<Stream>> DownloadAsync(string container, string blobName, CancellationToken ct)
    {
        var key = MakeKey(container, blobName);
        if (!_blobs.TryGetValue(key, out var blob))
            return Task.FromResult(StorageResult<Stream>.Fail($"Blob not found: {key}"));

        Stream stream = new MemoryStream(blob.Content);
        return Task.FromResult(StorageResult<Stream>.Ok(stream));
    }

    /// <summary>Delete a blob.</summary>
    public Task<StorageResult<bool>> DeleteAsync(string container, string blobName, CancellationToken ct)
    {
        var key = MakeKey(container, blobName);
        var removed = _blobs.TryRemove(key, out _);

        _logger.LogInformation("[MOCK BLOB] Delete: {Key}, Removed={Removed}", key, removed);
        return Task.FromResult(StorageResult<bool>.Ok(removed));
    }

    /// <summary>Generate a mock time-limited download URL.</summary>
    public Task<string> GetDownloadUrlAsync(string container, string blobName, TimeSpan expiry, CancellationToken ct)
    {
        var expiresAt = DateTime.UtcNow.Add(expiry);
        var url = $"https://mock-blob.thewatch.local/{container}/{blobName}?expiry={expiresAt:o}";
        return Task.FromResult(url);
    }

    /// <summary>Check if a blob exists.</summary>
    public Task<bool> ExistsAsync(string container, string blobName, CancellationToken ct)
    {
        var key = MakeKey(container, blobName);
        return Task.FromResult(_blobs.ContainsKey(key));
    }

    // ── Test Helpers ──────────────────────────────────────────────

    /// <summary>Get the number of stored blobs.</summary>
    public int BlobCount => _blobs.Count;

    /// <summary>Get the raw bytes of a stored blob (for test assertions).</summary>
    public byte[]? GetBlobBytes(string container, string blobName)
    {
        var key = MakeKey(container, blobName);
        return _blobs.TryGetValue(key, out var blob) ? blob.Content : null;
    }
}
