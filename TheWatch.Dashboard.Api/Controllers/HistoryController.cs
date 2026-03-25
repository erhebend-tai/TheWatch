// HistoryController — REST endpoints for personal incident history, timelines,
// PDF/HTML export, and personal safety statistics.
//
// Endpoints:
//   GET /api/history/{userId}                       — Paginated incident history (resolved + cancelled)
//   GET /api/history/{userId}/{requestId}           — Full incident timeline with all events
//   GET /api/history/{userId}/{requestId}/export    — Generate print-friendly HTML summary
//   GET /api/history/{userId}/stats                 — Personal safety statistics
//
// Privacy:
//   - Users can only view their OWN incident history
//   - Responder identities are anonymized ("Responder 1", "Responder 2")
//   - PII is never included in exported HTML
//   - Export returns Content-Type text/html — the client handles actual PDF generation
//     (via window.print(), wkhtmltopdf, or similar)
//
// Pagination:
//   GET /api/history/u-123?pageSize=10&pageNumber=2
//   Response includes: items, totalCount, pageNumber, pageSize, totalPages
//
// Example — export flow:
//   1. Client fetches: GET /api/history/u-123/req-abc/export
//   2. Server returns self-contained HTML with inline CSS and print-friendly styling
//   3. Client opens HTML in a WebView or iframe, user taps "Print / Save as PDF"
//   4. Browser's native print dialog handles PDF generation
//
// WAL: HistoryController created — 4 endpoints wired to IIncidentHistoryPort.

using Microsoft.AspNetCore.Mvc;
using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Dashboard.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HistoryController : ControllerBase
{
    private readonly IIncidentHistoryPort _historyPort;
    private readonly ILogger<HistoryController> _logger;

    public HistoryController(
        IIncidentHistoryPort historyPort,
        ILogger<HistoryController> logger)
    {
        _historyPort = historyPort;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────
    // Paginated History
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Get paginated incident history for a user.
    /// Only returns completed (Resolved) and cancelled (Cancelled) incidents.
    /// Ordered by most recent first.
    ///
    /// Example:
    ///   GET /api/history/u-123?pageSize=10&pageNumber=1
    ///
    /// Response:
    ///   {
    ///     "items": [ { "requestId": "req-abc", "scope": "CheckIn", ... } ],
    ///     "totalCount": 47,
    ///     "pageNumber": 1,
    ///     "pageSize": 10,
    ///     "totalPages": 5
    ///   }
    /// </summary>
    [HttpGet("{userId}")]
    public async Task<IActionResult> GetHistory(
        string userId,
        [FromQuery] int pageSize = 10,
        [FromQuery] int pageNumber = 1,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return BadRequest(new { error = "UserId is required" });

        if (pageSize < 1) pageSize = 10;
        if (pageSize > 100) pageSize = 100;
        if (pageNumber < 1) pageNumber = 1;

        var result = await _historyPort.GetHistoryAsync(userId, pageSize, pageNumber, ct);

        return Ok(new
        {
            items = result.Items.Select(e => new
            {
                e.RequestId,
                Scope = e.Scope.ToString(),
                Status = e.Status.ToString(),
                e.TriggerSource,
                e.Description,
                e.Latitude,
                e.Longitude,
                e.CreatedAt,
                e.ResolvedAt,
                e.DurationMinutes,
                e.ResponderCount,
                e.EscalationCount,
                e.Resolution
            }),
            result.TotalCount,
            result.PageNumber,
            result.PageSize,
            result.TotalPages
        });
    }

    // ─────────────────────────────────────────────────────────────
    // Full Timeline
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Get the full event timeline for a specific incident.
    /// Includes all events from trigger through resolution: dispatches,
    /// acknowledgments, messages, escalations, evidence, and resolution.
    ///
    /// Example:
    ///   GET /api/history/u-123/req-abc
    ///
    /// Response includes chronological events:
    ///   [
    ///     { "eventType": "Trigger", "timestamp": "...", "detail": "SOS triggered via phrase detection" },
    ///     { "eventType": "Dispatched", "timestamp": "...", "detail": "Dispatched 8 responders..." },
    ///     { "eventType": "Acknowledged", "actorName": "Responder 1", ... },
    ///     ...
    ///   ]
    /// </summary>
    [HttpGet("{userId}/{requestId}")]
    public async Task<IActionResult> GetTimeline(
        string userId,
        string requestId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return BadRequest(new { error = "UserId is required" });
        if (string.IsNullOrWhiteSpace(requestId))
            return BadRequest(new { error = "RequestId is required" });

        var timeline = await _historyPort.GetTimelineAsync(userId, requestId, ct);
        if (timeline is null)
            return NotFound(new { error = $"Incident {requestId} not found for user {userId}" });

        return Ok(new
        {
            timeline.RequestId,
            timeline.UserId,
            Scope = timeline.Scope.ToString(),
            FinalStatus = timeline.FinalStatus.ToString(),
            timeline.CreatedAt,
            timeline.ResolvedAt,
            timeline.DurationMinutes,
            timeline.TotalRespondersDispatched,
            timeline.TotalRespondersAcknowledged,
            timeline.TotalEscalations,
            Events = timeline.Events.OrderBy(e => e.Timestamp).Select(e => new
            {
                e.EventId,
                EventType = e.EventType.ToString(),
                e.Timestamp,
                e.ActorName,
                e.Detail,
                e.Latitude,
                e.Longitude
            })
        });
    }

    // ─────────────────────────────────────────────────────────────
    // HTML Export (for PDF)
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Generate an HTML incident report suitable for printing or saving as PDF.
    /// Returns Content-Type text/html with self-contained inline CSS.
    ///
    /// The HTML includes:
    ///   - Incident summary (ID, scope, status, duration, responder count)
    ///   - Full event timeline
    ///   - Resolution details
    ///   - Print button (hidden during actual printing via @media print)
    ///   - Footer with generation timestamp and privacy notice
    ///
    /// The client can:
    ///   - Open this in a WebView and use window.print()
    ///   - Pipe through wkhtmltopdf on the server if needed
    ///   - Use a headless browser (Puppeteer/Playwright) for PDF generation
    ///
    /// Example:
    ///   GET /api/history/u-123/req-abc/export
    ///   → Returns full HTML document as text/html
    /// </summary>
    [HttpGet("{userId}/{requestId}/export")]
    public async Task<IActionResult> ExportHtml(
        string userId,
        string requestId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return BadRequest(new { error = "UserId is required" });
        if (string.IsNullOrWhiteSpace(requestId))
            return BadRequest(new { error = "RequestId is required" });

        var html = await _historyPort.ExportHtmlAsync(userId, requestId, ct);
        if (html is null)
            return NotFound(new { error = $"Incident {requestId} not found for user {userId}" });

        _logger.LogInformation("HTML export generated for {RequestId} by {UserId}", requestId, userId);

        return Content(html, "text/html");
    }

    // ─────────────────────────────────────────────────────────────
    // Personal Safety Stats
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Get aggregated personal safety statistics for a user.
    /// Includes: total incidents, resolution rates, average response times,
    /// most common scope/trigger, and time range.
    ///
    /// Example:
    ///   GET /api/history/u-123/stats
    ///
    /// Response:
    ///   {
    ///     "totalIncidents": 12,
    ///     "resolvedCount": 9,
    ///     "cancelledCount": 3,
    ///     "averageResponseTimeMinutes": 3.8,
    ///     "mostCommonScope": "CheckIn",
    ///     ...
    ///   }
    /// </summary>
    [HttpGet("{userId}/stats")]
    public async Task<IActionResult> GetStats(string userId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return BadRequest(new { error = "UserId is required" });

        var stats = await _historyPort.GetStatsAsync(userId, ct);

        return Ok(new
        {
            stats.UserId,
            stats.TotalIncidents,
            stats.ResolvedCount,
            stats.CancelledCount,
            stats.EscalatedCount,
            stats.AverageResponseTimeMinutes,
            stats.AverageDurationMinutes,
            stats.AverageResponderCount,
            stats.IncidentsResolvedWithoutEscalation,
            MostCommonScope = stats.MostCommonScope?.ToString(),
            stats.MostCommonTriggerSource,
            stats.FirstIncidentAt,
            stats.LastIncidentAt
        });
    }
}
