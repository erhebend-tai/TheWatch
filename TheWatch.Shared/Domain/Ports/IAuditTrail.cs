// IAuditTrail — domain port for tamper-evident audit logging with Merkle hash chain.
//
// Architecture:
//   Every state transition in TheWatch flows through this port:
//     SOS trigger → response created → dispatch → ack → escalation → 911 → resolve
//   All entries in one emergency share a CorrelationId (= ResponseRequest.RequestId).
//   CausationId links each entry to the entry that directly caused it.
//
//   ┌────────────┐     ┌──────────────────┐     ┌────────────────────┐
//   │ Any Service │────▶│ IAuditTrail      │────▶│ Adapter            │
//   │ or Port     │     │ .AppendAsync()   │     │ (SQL, Cosmos, Mock)│
//   └────────────┘     └──────────────────┘     └────────────────────┘
//                              │
//                     Merkle hash chain ensures tamper evidence
//                     VerifyIntegrityAsync() validates the full chain
//
// ISO 27001 compliance:
//   A.12.4.1 — Event logging (this entire port)
//   A.12.4.2 — Protection of log information (Merkle chain + write-once storage)
//   A.12.4.3 — Administrator and operator logs (ActorRole filtering)
//   A.12.4.4 — Clock synchronization (all timestamps UTC)
//
// Example — full SOS flow query:
//   var chain = await audit.GetTrailByCorrelationAsync("req-abc123");
//   // Returns: SOSTrigger → ResponseRequestCreated → ResponseDispatched →
//   //          ResponderNotified (x5) → ResponderAcknowledged (x3) →
//   //          EscalationFired → Emergency911Initiated → Emergency911CallConnected →
//   //          ResponderOnScene → SOSResolved
//
// Example — integrity check:
//   bool intact = await audit.VerifyIntegrityAsync();
//   // Walks every entry, recomputes SHA-256, validates chain links

using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Enums;

namespace TheWatch.Shared.Domain.Ports;

public interface IAuditTrail
{
    // ── Write ───────────────────────────────────────────────────

    /// <summary>
    /// Append an audit entry to the chain. Computes Merkle hash and links to previous.
    /// This is write-once — entries cannot be modified or deleted after append.
    /// </summary>
    Task AppendAsync(AuditEntry entry, CancellationToken ct = default);

    /// <summary>
    /// Append multiple entries atomically (e.g., batch notification sends).
    /// All entries are chained in order within the batch.
    /// </summary>
    Task AppendBatchAsync(IReadOnlyList<AuditEntry> entries, CancellationToken ct = default);

    // ── Query by Time ───────────────────────────────────────────

    /// <summary>Get all audit entries within a time range.</summary>
    Task<List<AuditEntry>> GetTrailAsync(DateTime from, DateTime to, CancellationToken ct = default);

    // ── Query by Entity ─────────────────────────────────────────

    /// <summary>Get all audit entries for a specific entity (e.g., all changes to a ResponseRequest).</summary>
    Task<List<AuditEntry>> GetTrailByEntityAsync(string entityType, string entityId, CancellationToken ct = default);

    // ── Query by User ───────────────────────────────────────────

    /// <summary>Get all audit entries for a specific user (their actions AND actions affecting them).</summary>
    Task<List<AuditEntry>> GetTrailByUserAsync(string userId, CancellationToken ct = default);

    // ── Query by Correlation (end-to-end flow) ──────────────────

    /// <summary>
    /// Get the complete audit chain for a single emergency flow.
    /// CorrelationId = ResponseRequest.RequestId threads the entire chain:
    /// SOS → dispatch → ack → escalation → 911 → resolve.
    /// Returns entries in chronological order.
    /// </summary>
    Task<List<AuditEntry>> GetTrailByCorrelationAsync(string correlationId, CancellationToken ct = default);

    // ── Query by Action Type ────────────────────────────────────

    /// <summary>
    /// Get audit entries filtered by action type (e.g., all Emergency911Initiated events).
    /// Useful for compliance reporting and incident review.
    /// </summary>
    Task<List<AuditEntry>> GetTrailByActionAsync(AuditAction action, DateTime? from = null, DateTime? to = null, CancellationToken ct = default);

    // ── Query by Severity ───────────────────────────────────────

    /// <summary>
    /// Get audit entries at or above a severity level within a time range.
    /// Critical = life-safety events (SOS, 911, escalation, integrity failure).
    /// </summary>
    Task<List<AuditEntry>> GetTrailBySeverityAsync(AuditSeverity minSeverity, DateTime from, DateTime to, CancellationToken ct = default);

    // ── Query by Source ─────────────────────────────────────────

    /// <summary>
    /// Get audit entries from a specific source system and/or component.
    /// Example: GetTrailBySourceAsync("Dashboard.Api", "ResponseCoordinationService")
    /// </summary>
    Task<List<AuditEntry>> GetTrailBySourceAsync(string sourceSystem, string? sourceComponent = null, DateTime? from = null, DateTime? to = null, CancellationToken ct = default);

    // ── Chain Integrity ─────────────────────────────────────────

    /// <summary>
    /// Verify the entire Merkle hash chain. Walks every entry from genesis,
    /// recomputes SHA-256, and validates PreviousHash links.
    /// Returns true if the chain is intact (no tampering detected).
    /// </summary>
    Task<bool> VerifyIntegrityAsync(CancellationToken ct = default);

    /// <summary>
    /// Verify integrity of a specific range (faster than full chain for spot checks).
    /// Validates entries within the range AND the chain link into the range.
    /// </summary>
    Task<bool> VerifyIntegrityRangeAsync(DateTime from, DateTime to, CancellationToken ct = default);

    // ── Latest Entry ────────────────────────────────────────────

    /// <summary>Get the most recent audit entry (used for chaining).</summary>
    Task<AuditEntry?> GetLatestEntryAsync(CancellationToken ct = default);

    // ── Statistics ───────────────────────────────────────────────

    /// <summary>
    /// Get audit statistics for a time range (entry counts by action, severity, outcome).
    /// Used by the dashboard for compliance reporting.
    /// </summary>
    Task<AuditStatistics> GetStatisticsAsync(DateTime from, DateTime to, CancellationToken ct = default);
}

/// <summary>
/// Aggregated audit statistics for dashboard reporting.
/// </summary>
public record AuditStatistics(
    long TotalEntries,
    long CriticalEntries,
    long FailedEntries,
    long DeniedEntries,
    Dictionary<string, long> CountByAction,
    Dictionary<string, long> CountBySourceSystem,
    Dictionary<string, long> CountBySeverity,
    Dictionary<string, long> CountByOutcome,
    DateTime OldestEntry,
    DateTime NewestEntry,
    bool ChainIntact
);
