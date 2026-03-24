// AuditEntry — Merkle-chained audit record for tamper-evident logging.
// Example:
//   var entry = new AuditEntry { UserId = "u-123", Action = AuditAction.SOSTrigger, EntityType = "Alert" };
//   entry.Hash = SHA256(entry.PreviousHash + entry.Timestamp + entry.Action + ...);
using TheWatch.Shared.Enums;

namespace TheWatch.Shared.Domain.Models;

public class AuditEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = string.Empty;
    public AuditAction Action { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string? IpAddress { get; set; }
    public string CorrelationId { get; set; } = Guid.NewGuid().ToString();
    public string Hash { get; set; } = string.Empty;
    public string? PreviousHash { get; set; }
}
