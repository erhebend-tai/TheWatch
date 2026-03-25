// AuditEntry — Merkle-chained audit record for tamper-evident logging.
//
// Every AuditEntry is hash-chained to the previous entry (SHA-256 Merkle chain).
// Tampering with any entry breaks the chain, detectable by VerifyIntegrityAsync().
//
// Fields support ISO 27001 A.12.4 requirements:
//   - WHO: UserId, ActorRole, IpAddress, DeviceId, SessionId
//   - WHAT: Action, EntityType, EntityId, OldValue, NewValue
//   - WHEN: Timestamp (UTC), Duration
//   - WHERE: SourceSystem, SourceComponent
//   - WHY: Reason, CorrelationId (links all entries in a single SOS flow)
//   - HOW: Severity, DataClassification, Outcome
//
// CorrelationId threads the entire emergency chain:
//   SOS trigger → response created → dispatch → ack → escalation → 911 call → resolution
//   All entries share the same CorrelationId so the full story is queryable:
//     await audit.GetTrailByCorrelationAsync(correlationId);
//
// Example:
//   var entry = new AuditEntry
//   {
//       UserId = "u-123",
//       Action = AuditAction.Emergency911Initiated,
//       EntityType = "Emergency911Request",
//       EntityId = "req-456",
//       CorrelationId = responseRequest.RequestId,
//       SourceSystem = "Dashboard.Api",
//       SourceComponent = "ResponseCoordinationService",
//       Severity = AuditSeverity.Critical,
//       DataClassification = DataClassification.HighlyConfidential,
//       Outcome = AuditOutcome.Success,
//       Reason = "Escalation policy Immediate911 triggered",
//       NewValue = JsonSerializer.Serialize(request)
//   };
//   entry.Hash = SHA256(entry.PreviousHash + entry.Timestamp + entry.Action + ...);

using TheWatch.Shared.Enums;

namespace TheWatch.Shared.Domain.Models;

/// <summary>
/// Severity of the audited event. Maps to SIEM ingestion priority.
/// </summary>
public enum AuditSeverity
{
    /// <summary>Routine operation (login, profile view, config read).</summary>
    Info,

    /// <summary>Notable action (preference change, consent update, evidence view).</summary>
    Notice,

    /// <summary>Security-relevant action (permission change, failed login, data export).</summary>
    Warning,

    /// <summary>Life-safety or security event (SOS trigger, escalation, 911 call, integrity failure).</summary>
    Critical
}

/// <summary>
/// Data classification of the audited entity. Determines retention and access rules.
/// Aligned with ISO 27001 A.8.2 (Information classification).
/// </summary>
public enum DataClassification
{
    /// <summary>Non-sensitive operational data (build status, agent heartbeats).</summary>
    Public,

    /// <summary>Internal operational data (dispatch counts, swarm metrics).</summary>
    Internal,

    /// <summary>User PII, location data, notification content.</summary>
    Confidential,

    /// <summary>Evidence, medical info, duress events, 911 recordings, consent records.</summary>
    HighlyConfidential
}

/// <summary>
/// Whether the audited operation succeeded or failed.
/// </summary>
public enum AuditOutcome
{
    Success,
    Failure,
    Denied,
    Timeout,
    RateLimited,
    Partial
}

public class AuditEntry
{
    // ── Identity ────────────────────────────────────────────────

    /// <summary>Unique audit entry ID.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Sequence number within the chain (monotonically increasing).</summary>
    public long SequenceNumber { get; set; }

    // ── WHO ─────────────────────────────────────────────────────

    /// <summary>User who performed or was affected by the action.</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Role of the actor: "User", "Responder", "Admin", "System", "SwarmAgent", "Escalation".
    /// "System" for automated actions (Hangfire jobs, escalation timers).
    /// </summary>
    public string ActorRole { get; set; } = "User";

    /// <summary>Client IP address (null for system-initiated actions).</summary>
    public string? IpAddress { get; set; }

    /// <summary>Device ID or user-agent (mobile app version, browser, etc.).</summary>
    public string? DeviceId { get; set; }

    /// <summary>Session ID for grouping actions within a single user session.</summary>
    public string? SessionId { get; set; }

    // ── WHAT ────────────────────────────────────────────────────

    /// <summary>The audited action.</summary>
    public AuditAction Action { get; set; }

    /// <summary>Type of entity affected: "ResponseRequest", "Evidence", "Emergency911Request", etc.</summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>ID of the affected entity.</summary>
    public string EntityId { get; set; } = string.Empty;

    /// <summary>Previous state (JSON-serialized, null for creates).</summary>
    public string? OldValue { get; set; }

    /// <summary>New state (JSON-serialized, null for deletes/reads).</summary>
    public string? NewValue { get; set; }

    // ── WHEN ────────────────────────────────────────────────────

    /// <summary>UTC timestamp of the event.</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>Duration of the operation (null for instantaneous actions).</summary>
    public TimeSpan? Duration { get; set; }

    // ── WHERE ───────────────────────────────────────────────────

    /// <summary>
    /// Source system: "Dashboard.Api", "Dashboard.Web", "Functions", "BuildServer",
    /// "WorkerServices", "MobileApp.iOS", "MobileApp.Android", "AlarmSystem", "Telephony".
    /// </summary>
    public string SourceSystem { get; set; } = string.Empty;

    /// <summary>
    /// Source component within the system: "ResponseCoordinationService",
    /// "SwarmCoordinationService", "EscalationPort", "EmergencyServicesPort", etc.
    /// </summary>
    public string SourceComponent { get; set; } = string.Empty;

    // ── WHY ─────────────────────────────────────────────────────

    /// <summary>Human-readable reason for the action (e.g., "Escalation timeout: 0/5 acked").</summary>
    public string? Reason { get; set; }

    /// <summary>
    /// Correlation ID that threads all entries in a single emergency flow.
    /// Typically the ResponseRequest.RequestId so the entire SOS→dispatch→ack→911→resolve
    /// chain is queryable as one story.
    /// </summary>
    public string CorrelationId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Causation ID — the audit entry ID that directly caused this entry.
    /// Enables causal chain reconstruction: "911 call was caused by escalation,
    /// which was caused by insufficient responders, which was caused by SOS trigger."
    /// </summary>
    public string? CausationId { get; set; }

    // ── HOW (classification) ────────────────────────────────────

    /// <summary>Severity of the event for SIEM prioritization.</summary>
    public AuditSeverity Severity { get; set; } = AuditSeverity.Info;

    /// <summary>Data sensitivity classification of the affected entity.</summary>
    public DataClassification DataClassification { get; set; } = DataClassification.Internal;

    /// <summary>Whether the operation succeeded or failed.</summary>
    public AuditOutcome Outcome { get; set; } = AuditOutcome.Success;

    /// <summary>Error message if Outcome is Failure, Denied, or Timeout.</summary>
    public string? ErrorMessage { get; set; }

    // ── Merkle Chain ────────────────────────────────────────────

    /// <summary>SHA-256 hash of this entry (computed from all fields + PreviousHash).</summary>
    public string Hash { get; set; } = string.Empty;

    /// <summary>Hash of the previous entry in the chain (null for genesis entry).</summary>
    public string? PreviousHash { get; set; }
}
