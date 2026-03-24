// IOraclePort — domain port for Oracle Autonomous Database, OCI Object Storage, and OCI Notifications.
// NO database SDK imports allowed in this file.
// Oracle Cloud uses OCI SDK or REST API (https://docs.oracle.com/en-us/iaas/api/).
// Example:
//   var health = await oracle.GetHealthCheckAsync();
//   var dbStatus = await oracle.GetAutonomousDatabaseStatusAsync(databaseId);
//   var objects = await oracle.ListObjectStorageObjectsAsync(namespaceName, bucketName);
//   await oracle.PublishNotificationAsync(topicId, title, body);
//
// Write-Ahead Log (WAL):
//   2026-03-24 — IOraclePort created with methods for Autonomous DB, Object Storage, Notifications.
//   Next: Implement OraclePortAdapter in TheWatch.Adapters.Oracle.
using TheWatch.Shared.Dtos;

namespace TheWatch.Shared.Domain.Ports;

public interface IOraclePort
{
    // --- Health ---
    Task<List<HealthStatusDto>> GetHealthCheckAsync(CancellationToken ct = default);

    // --- Oracle Autonomous Database ---
    Task<Dictionary<string, object>> GetAutonomousDatabaseStatusAsync(string databaseId, CancellationToken ct = default);
    Task<List<Dictionary<string, object>>> ListAutonomousDatabasesAsync(CancellationToken ct = default);
    Task<Dictionary<string, object>> ExecuteAutonomousDbQueryAsync(string databaseId, string sql, CancellationToken ct = default);

    // --- OCI Object Storage ---
    Task<List<Dictionary<string, object>>> ListObjectStorageBucketsAsync(string namespaceName, string compartmentId, CancellationToken ct = default);
    Task<List<Dictionary<string, object>>> ListObjectStorageObjectsAsync(string namespaceName, string bucketName, string? prefix = null, CancellationToken ct = default);
    Task<Dictionary<string, object>> GetObjectStorageObjectMetadataAsync(string namespaceName, string bucketName, string objectName, CancellationToken ct = default);

    // --- OCI Notifications ---
    Task<List<Dictionary<string, object>>> ListNotificationTopicsAsync(string compartmentId, CancellationToken ct = default);
    Task PublishNotificationAsync(string topicId, string title, string body, CancellationToken ct = default);
}
