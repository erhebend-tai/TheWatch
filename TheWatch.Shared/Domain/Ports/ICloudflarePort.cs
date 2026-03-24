// ICloudflarePort — domain port for Cloudflare Workers, R2, D1, KV, Turnstile, and Stream.
// NO database SDK imports allowed in this file.
// Cloudflare communicates via REST API (https://api.cloudflare.com/client/v4/).
// Example:
//   var health = await cloudflare.GetHealthCheckAsync();
//   var workerStatus = await cloudflare.GetWorkerDeploymentStatusAsync("my-worker");
//   var buckets = await cloudflare.ListR2BucketsAsync();
//   var turnstileResult = await cloudflare.VerifyTurnstileTokenAsync(token, remoteIp);
//   var kvValue = await cloudflare.GetKvValueAsync(namespaceId, key);
//   var streamInfo = await cloudflare.GetStreamVideoStatusAsync(videoId);
//
// Write-Ahead Log (WAL):
//   2026-03-24 — ICloudflarePort created with methods for Workers, R2, D1, KV, Turnstile, Stream.
//   Next: Implement CloudflarePortAdapter in TheWatch.Adapters.Cloudflare.
using TheWatch.Shared.Dtos;

namespace TheWatch.Shared.Domain.Ports;

public interface ICloudflarePort
{
    // --- Health ---
    Task<List<HealthStatusDto>> GetHealthCheckAsync(CancellationToken ct = default);

    // --- Workers (serverless functions) ---
    Task<Dictionary<string, object>> GetWorkerDeploymentStatusAsync(string workerName, CancellationToken ct = default);
    Task<List<Dictionary<string, object>>> ListWorkersAsync(CancellationToken ct = default);

    // --- R2 (object storage, S3-compatible) ---
    Task<List<Dictionary<string, object>>> ListR2BucketsAsync(CancellationToken ct = default);
    Task<Dictionary<string, object>> GetR2BucketDetailsAsync(string bucketName, CancellationToken ct = default);
    Task<List<Dictionary<string, object>>> ListR2ObjectsAsync(string bucketName, string? prefix = null, CancellationToken ct = default);

    // --- D1 (SQLite database at edge) ---
    Task<List<Dictionary<string, object>>> ListD1DatabasesAsync(CancellationToken ct = default);
    Task<Dictionary<string, object>> QueryD1Async(string databaseId, string sql, CancellationToken ct = default);

    // --- KV (key-value store at edge) ---
    Task<string?> GetKvValueAsync(string namespaceId, string key, CancellationToken ct = default);
    Task SetKvValueAsync(string namespaceId, string key, string value, CancellationToken ct = default);
    Task<List<string>> ListKvKeysAsync(string namespaceId, string? prefix = null, CancellationToken ct = default);

    // --- Turnstile (bot protection / CAPTCHA alternative) ---
    Task<Dictionary<string, object>> VerifyTurnstileTokenAsync(string token, string remoteIp, CancellationToken ct = default);

    // --- Stream (video storage and delivery) ---
    Task<Dictionary<string, object>> GetStreamVideoStatusAsync(string videoId, CancellationToken ct = default);
    Task<List<Dictionary<string, object>>> ListStreamVideosAsync(CancellationToken ct = default);
}
