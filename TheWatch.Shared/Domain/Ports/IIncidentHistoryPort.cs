// IIncidentHistoryPort — domain port for querying completed/cancelled incident history,
// detailed incident timelines, personal safety statistics, and HTML export for PDF.
//
// Architecture:
//   ┌────────────────────┐     ┌──────────────────────────┐     ┌──────────────────────────┐
//   │ Mobile / Dashboard  │────▶│ IIncidentHistoryPort      │────▶│ Adapter                  │
//   │ (history list,      │     │ .GetHistoryAsync()        │     │ (SQL, Cosmos, Firebase,  │
//   │  timeline, export)  │     │ .GetTimelineAsync()       │     │  Mock)                   │
//   └────────────────────┘     │ .ExportHtmlAsync()        │     └──────────────────────────┘
//                              └──────────────────────────┘
//
// This port is READ-ONLY. It aggregates data from:
//   - IResponseRequestPort (completed/cancelled requests)
//   - IResponseTrackingPort (responder acknowledgments)
//   - IEscalationPort (escalation events)
//   - IResponderCommunicationPort (messages exchanged)
//   - IAuditTrail (evidence submissions, status changes)
//
// Privacy:
//   - Users can only view their OWN incident history
//   - Responder names are anonymized in history ("Responder 1", "Responder 2")
//   - PII is never included in exported HTML
//   - Evidence attachments are referenced by blob ID, not inline
//
// Example — paginated history:
//   var page = await port.GetHistoryAsync("u-123", pageSize: 10, pageNumber: 1, ct);
//   // page.Items = [ { RequestId, Scope=CheckIn, Duration=4min, ResponderCount=3, ... }, ... ]
//   // page.TotalCount = 47, page.TotalPages = 5
//
// Example — full timeline:
//   var timeline = await port.GetTimelineAsync("u-123", "req-abc", ct);
//   // timeline.Events = [
//   //   { Type=Trigger, Timestamp=..., Detail="SOS triggered via phrase detection" },
//   //   { Type=Dispatched, Timestamp=..., Detail="Dispatched 8 responders within 1000m" },
//   //   { Type=Acknowledged, Timestamp=..., Detail="Responder 1 acknowledged, ETA 3 min" },
//   //   { Type=Message, Timestamp=..., Detail="Responder 1: I'm on my way" },
//   //   { Type=Resolved, Timestamp=..., Detail="User confirmed safe" }
//   // ]
//
// Write-Ahead Log:
//   WAL-IHP-001: IIncidentHistoryPort interface created — 4 methods
//   WAL-IHP-002: IncidentHistoryEntry model — summary per incident
//   WAL-IHP-003: IncidentTimeline model — full event timeline
//   WAL-IHP-004: TimelineEvent model — individual events in a timeline
//   WAL-IHP-005: PersonalSafetyStats model — aggregated personal statistics
//   WAL-IHP-006: PaginatedResult generic — pagination wrapper

namespace TheWatch.Shared.Domain.Ports;

// ═══════════════════════════════════════════════════════════════
// Pagination
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Generic paginated result wrapper.
///
/// Example:
///   var page = new PaginatedResult&lt;IncidentHistoryEntry&gt;
///   {
///       Items = entries, TotalCount = 47, PageNumber = 1, PageSize = 10
///   };
///   // page.TotalPages == 5
/// </summary>
public class PaginatedResult<T>
{
    public IReadOnlyList<T> Items { get; set; } = Array.Empty<T>();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
}

// ═══════════════════════════════════════════════════════════════
// Incident History Entry (summary)
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Summary of a single completed or cancelled incident for the history list view.
///
/// Example:
///   new IncidentHistoryEntry
///   {
///       RequestId = "req-abc",
///       Scope = ResponseScope.CheckIn,
///       Status = ResponseStatus.Resolved,
///       TriggerSource = "PHRASE",
///       CreatedAt = DateTime.Parse("2026-03-20T14:30:00Z"),
///       ResolvedAt = DateTime.Parse("2026-03-20T14:34:12Z"),
///       DurationMinutes = 4.2,
///       ResponderCount = 3,
///       Resolution = "User confirmed safe"
///   };
/// </summary>
public class IncidentHistoryEntry
{
    public string RequestId { get; set; } = string.Empty;
    public ResponseScope Scope { get; set; }
    public ResponseStatus Status { get; set; }
    public string? TriggerSource { get; set; }
    public string? Description { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public double? DurationMinutes { get; set; }
    public int ResponderCount { get; set; }
    public int EscalationCount { get; set; }
    public string? Resolution { get; set; }
}

// ═══════════════════════════════════════════════════════════════
// Timeline Event Types
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Types of events that appear in an incident timeline.
/// </summary>
public enum TimelineEventType
{
    /// <summary>SOS trigger (phrase, quick-tap, manual button).</summary>
    Trigger,

    /// <summary>Responders dispatched.</summary>
    Dispatched,

    /// <summary>A responder acknowledged the dispatch.</summary>
    Acknowledged,

    /// <summary>A responder arrived on scene.</summary>
    ArrivedOnScene,

    /// <summary>Escalation policy triggered (timed, conditional 911, etc.).</summary>
    Escalated,

    /// <summary>Responder message in the incident channel.</summary>
    Message,

    /// <summary>Evidence submitted (photo, sitrep text, video).</summary>
    Evidence,

    /// <summary>Status change (e.g., Active → Escalated).</summary>
    StatusChange,

    /// <summary>User cancelled the request ("I'm OK").</summary>
    Cancelled,

    /// <summary>Incident resolved (responder confirmed safe, emergency services on scene).</summary>
    Resolved
}

// ═══════════════════════════════════════════════════════════════
// Timeline Event
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// A single event in an incident's timeline. Events are ordered chronologically.
///
/// Example:
///   new TimelineEvent
///   {
///       EventType = TimelineEventType.Acknowledged,
///       Timestamp = DateTime.Parse("2026-03-20T14:31:15Z"),
///       ActorName = "Responder 1",
///       Detail = "Acknowledged dispatch, ETA 3 minutes"
///   };
/// </summary>
public class TimelineEvent
{
    public string EventId { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public TimelineEventType EventType { get; set; }
    public DateTime Timestamp { get; set; }
    public string? ActorName { get; set; }   // Anonymized: "Responder 1", "System", "User"
    public string Detail { get; set; } = string.Empty;
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}

// ═══════════════════════════════════════════════════════════════
// Incident Timeline (full detail)
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Full timeline for a single incident including all events from trigger to resolution.
/// </summary>
public class IncidentTimeline
{
    public string RequestId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public ResponseScope Scope { get; set; }
    public ResponseStatus FinalStatus { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public double? DurationMinutes { get; set; }
    public int TotalRespondersDispatched { get; set; }
    public int TotalRespondersAcknowledged { get; set; }
    public int TotalEscalations { get; set; }
    public List<TimelineEvent> Events { get; set; } = new();
}

// ═══════════════════════════════════════════════════════════════
// Personal Safety Stats
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Aggregated personal safety statistics for a user's incident history.
///
/// Example:
///   stats.TotalIncidents = 12;
///   stats.AverageResponseTimeMinutes = 3.8;
///   stats.MostCommonScope = ResponseScope.CheckIn;
///   stats.IncidentsResolvedWithoutEscalation = 10;
/// </summary>
public class PersonalSafetyStats
{
    public string UserId { get; set; } = string.Empty;
    public int TotalIncidents { get; set; }
    public int ResolvedCount { get; set; }
    public int CancelledCount { get; set; }
    public int EscalatedCount { get; set; }
    public double AverageResponseTimeMinutes { get; set; }
    public double AverageDurationMinutes { get; set; }
    public double AverageResponderCount { get; set; }
    public int IncidentsResolvedWithoutEscalation { get; set; }
    public ResponseScope? MostCommonScope { get; set; }
    public string? MostCommonTriggerSource { get; set; }
    public DateTime? FirstIncidentAt { get; set; }
    public DateTime? LastIncidentAt { get; set; }
}

// ═══════════════════════════════════════════════════════════════
// Port Interface
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Port for querying completed/cancelled incident history, full timelines,
/// personal safety statistics, and HTML export for PDF generation.
///
/// Adapters:
///   - MockIncidentHistoryAdapter: in-memory with seeded incidents (dev)
///   - Production: SQL/Cosmos read-model aggregating from response coordination tables
/// </summary>
public interface IIncidentHistoryPort
{
    /// <summary>
    /// Get paginated incident history for a user.
    /// Only returns completed (Resolved) and cancelled (Cancelled) incidents.
    /// </summary>
    Task<PaginatedResult<IncidentHistoryEntry>> GetHistoryAsync(
        string userId, int pageSize = 10, int pageNumber = 1,
        CancellationToken ct = default);

    /// <summary>
    /// Get the full timeline for a specific incident.
    /// Includes all events from trigger through resolution.
    /// Returns null if the incident doesn't exist or doesn't belong to the user.
    /// </summary>
    Task<IncidentTimeline?> GetTimelineAsync(
        string userId, string requestId, CancellationToken ct = default);

    /// <summary>
    /// Generate an HTML summary of an incident suitable for printing/saving as PDF.
    /// The HTML is self-contained with inline CSS for print-friendly formatting.
    /// Returns null if the incident doesn't exist or doesn't belong to the user.
    /// </summary>
    Task<string?> ExportHtmlAsync(
        string userId, string requestId, CancellationToken ct = default);

    /// <summary>
    /// Get aggregated personal safety statistics for a user.
    /// </summary>
    Task<PersonalSafetyStats> GetStatsAsync(
        string userId, CancellationToken ct = default);
}
