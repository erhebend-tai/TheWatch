// IAuditTrail — domain port for tamper-evident audit logging with Merkle hash chain.
// NO database SDK imports allowed in this file.
// Example:
//   await audit.AppendAsync(new AuditEntry { Action = AuditAction.SOSTrigger, UserId = "u-1" });
//   bool intact = await audit.VerifyIntegrityAsync();
using TheWatch.Shared.Domain.Models;

namespace TheWatch.Shared.Domain.Ports;

public interface IAuditTrail
{
    Task AppendAsync(AuditEntry entry, CancellationToken ct = default);
    Task<List<AuditEntry>> GetTrailAsync(DateTime from, DateTime to, CancellationToken ct = default);
    Task<List<AuditEntry>> GetTrailByEntityAsync(string entityType, string entityId, CancellationToken ct = default);
    Task<List<AuditEntry>> GetTrailByUserAsync(string userId, CancellationToken ct = default);
    Task<bool> VerifyIntegrityAsync(CancellationToken ct = default);
    Task<AuditEntry?> GetLatestEntryAsync(CancellationToken ct = default);
}
