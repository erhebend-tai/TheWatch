// OfflineQueueEntry — queued mutation for offline-first resilience.
// Example:
//   await storage.EnqueueOfflineAsync(new OfflineQueueEntry {
//       OperationType = "Create", EntityType = "Alert", SerializedPayload = json });

namespace TheWatch.Shared.Domain.Models;

public class OfflineQueueEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string OperationType { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string SerializedPayload { get; set; } = string.Empty;
    public DateTime QueuedAt { get; set; } = DateTime.UtcNow;
    public int RetryCount { get; set; }
    public bool IsSynced { get; set; }
}
