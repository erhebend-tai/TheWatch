// IBlobStoragePort — domain port for raw binary blob storage.
// NO cloud SDK imports allowed in this file. Adapters implement this.
// Maps to Azure Blob Storage, AWS S3, GCS, Cloudflare R2, or local filesystem.
//
// Containers map to logical buckets: "evidence", "thumbnails", "documents", "surveys".
// Blob names are hierarchical keys: "evidence/req-123/img-001.jpg".
//
// Example:
//   await blobStorage.UploadAsync("evidence", "req-123/img-001.jpg", stream, "image/jpeg", ct);
//   var url = await blobStorage.GetDownloadUrlAsync("evidence", "req-123/img-001.jpg", TimeSpan.FromMinutes(15), ct);

using TheWatch.Shared.Domain.Models;

namespace TheWatch.Shared.Domain.Ports;

public interface IBlobStoragePort
{
    /// <summary>Upload binary content to blob storage. Returns the blob reference key on success.</summary>
    Task<StorageResult<string>> UploadAsync(string container, string blobName, Stream content, string mimeType, CancellationToken ct = default);

    /// <summary>Download binary content from blob storage. Returns a readable stream.</summary>
    Task<StorageResult<Stream>> DownloadAsync(string container, string blobName, CancellationToken ct = default);

    /// <summary>Delete a blob. Returns true if the blob existed and was deleted.</summary>
    Task<StorageResult<bool>> DeleteAsync(string container, string blobName, CancellationToken ct = default);

    /// <summary>
    /// Generate a time-limited download URL (SAS token for Azure, pre-signed URL for S3, etc.).
    /// For mock: returns a data URI or a local path.
    /// </summary>
    Task<string> GetDownloadUrlAsync(string container, string blobName, TimeSpan expiry, CancellationToken ct = default);

    /// <summary>Check if a blob exists in storage.</summary>
    Task<bool> ExistsAsync(string container, string blobName, CancellationToken ct = default);
}
