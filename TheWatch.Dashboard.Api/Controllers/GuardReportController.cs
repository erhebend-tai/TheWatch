// GuardReportController — REST endpoints for security guard reporting and escalation.
//
// Endpoints:
//   POST /api/guard/enroll               — Enroll a user as a guard
//   GET  /api/guard/profile/{userId}     — Get guard profile
//   POST /api/guard/duty                 — Set on/off duty
//   GET  /api/guard/on-duty              — Get all on-duty guards
//
//   POST /api/guard/reports              — File a new report
//   POST /api/guard/reports/draft        — Save/update a draft report
//   GET  /api/guard/reports/{reportId}   — Get a specific report
//   GET  /api/guard/reports/by-guard/{userId} — Get reports by a guard
//   GET  /api/guard/reports/nearby       — Get nearby reports
//   GET  /api/guard/reports/queue/{status} — Get reports by status (supervisor queue)
//
//   POST /api/guard/reports/{reportId}/escalate  — UPGRADE to a Watch call (SOS)
//   POST /api/guard/reports/{reportId}/resolve   — Resolve a report
//   POST /api/guard/reports/{reportId}/review    — Supervisor review
//   POST /api/guard/reports/{reportId}/evidence  — Attach evidence
//
// WAL: Thin controller — validation + delegation to IGuardReportPort.
//      Escalation calls IResponseCoordinationService to create the SOS.

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using TheWatch.Dashboard.Api.Hubs;
using TheWatch.Dashboard.Api.Services;
using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Dashboard.Api.Controllers;

[ApiController]
[Route("api/guard")]
public class GuardReportController : ControllerBase
{
    private readonly IGuardReportPort _guardPort;
    private readonly IResponseCoordinationService _coordinationService;
    private readonly IHubContext<DashboardHub> _hubContext;
    private readonly ILogger<GuardReportController> _logger;

    public GuardReportController(
        IGuardReportPort guardPort,
        IResponseCoordinationService coordinationService,
        IHubContext<DashboardHub> hubContext,
        ILogger<GuardReportController> logger)
    {
        _guardPort = guardPort;
        _coordinationService = coordinationService;
        _hubContext = hubContext;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────
    // Guard Profiles
    // ─────────────────────────────────────────────────────────────

    [HttpPost("enroll")]
    public async Task<IActionResult> EnrollGuard(
        [FromBody] GuardProfile profile, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(profile.UserId))
            return BadRequest(new { error = "UserId is required" });

        var result = await _guardPort.EnrollGuardAsync(profile, ct);
        return Created($"/api/guard/profile/{result.UserId}", result);
    }

    [HttpGet("profile/{userId}")]
    public async Task<IActionResult> GetProfile(string userId, CancellationToken ct)
    {
        var profile = await _guardPort.GetGuardProfileAsync(userId, ct);
        return profile is null
            ? NotFound(new { error = $"Guard {userId} not found" })
            : Ok(profile);
    }

    [HttpPost("duty")]
    public async Task<IActionResult> SetDutyStatus(
        [FromBody] SetDutyRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.UserId))
            return BadRequest(new { error = "UserId is required" });

        var updated = await _guardPort.SetDutyStatusAsync(request.UserId, request.IsOnDuty, ct);

        await _hubContext.Clients.All.SendAsync("GuardDutyStatusChanged", new
        {
            updated.UserId,
            updated.Name,
            Role = updated.Role.ToString(),
            updated.IsOnDuty,
            Timestamp = DateTime.UtcNow
        }, ct);

        return Ok(updated);
    }

    [HttpGet("on-duty")]
    public async Task<IActionResult> GetOnDutyGuards(CancellationToken ct)
    {
        var guards = await _guardPort.GetOnDutyGuardsAsync(ct);
        return Ok(guards);
    }

    // ─────────────────────────────────────────────────────────────
    // Reports
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// File a new guard report. Sets status to Filed and notifies supervisors.
    /// </summary>
    [HttpPost("reports")]
    public async Task<IActionResult> FileReport(
        [FromBody] FileGuardReportRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.GuardUserId))
            return BadRequest(new { error = "GuardUserId is required" });
        if (string.IsNullOrWhiteSpace(request.Title))
            return BadRequest(new { error = "Title is required" });

        var report = new GuardReport(
            ReportId: "",
            GuardUserId: request.GuardUserId,
            GuardName: request.GuardName ?? "Unknown",
            GuardRole: request.GuardRole,
            BadgeNumber: request.BadgeNumber,
            Category: request.Category,
            Severity: request.Severity,
            Title: request.Title,
            Description: request.Description ?? "",
            Latitude: request.Latitude,
            Longitude: request.Longitude,
            AccuracyMeters: request.AccuracyMeters,
            LocationDescription: request.LocationDescription,
            Status: ReportStatus.Draft,
            CreatedAt: DateTime.UtcNow,
            FiledAt: null, EscalatedAt: null, ResolvedAt: null,
            EscalatedRequestId: null, EscalatedScope: null,
            EvidenceIds: request.EvidenceIds,
            PatrolRouteId: request.PatrolRouteId,
            PostId: request.PostId,
            PropertyId: request.PropertyId,
            ReviewedBy: null, ReviewNotes: null, ReviewedAt: null,
            ResolutionNotes: null, ResolvedBy: null);

        var filed = await _guardPort.FileReportAsync(report, ct);

        _logger.LogInformation(
            "Guard report filed: {ReportId} by {GuardName} — {Category}/{Severity}: {Title}",
            filed.ReportId, filed.GuardName, filed.Category, filed.Severity, filed.Title);

        // Broadcast to dashboard for supervisor awareness
        await _hubContext.Clients.All.SendAsync("GuardReportFiled", new
        {
            filed.ReportId,
            filed.GuardUserId,
            filed.GuardName,
            GuardRole = filed.GuardRole.ToString(),
            Category = filed.Category.ToString(),
            Severity = filed.Severity.ToString(),
            filed.Title,
            filed.Latitude,
            filed.Longitude,
            filed.LocationDescription,
            Status = filed.Status.ToString(),
            filed.FiledAt
        }, ct);

        return Created($"/api/guard/reports/{filed.ReportId}", filed);
    }

    /// <summary>
    /// Save or update a draft report (auto-save from mobile).
    /// </summary>
    [HttpPost("reports/draft")]
    public async Task<IActionResult> SaveDraft(
        [FromBody] GuardReport report, CancellationToken ct)
    {
        var draft = await _guardPort.SaveDraftAsync(report, ct);
        return Ok(draft);
    }

    [HttpGet("reports/{reportId}")]
    public async Task<IActionResult> GetReport(string reportId, CancellationToken ct)
    {
        var report = await _guardPort.GetReportAsync(reportId, ct);
        return report is null
            ? NotFound(new { error = $"Report {reportId} not found" })
            : Ok(report);
    }

    [HttpGet("reports/by-guard/{userId}")]
    public async Task<IActionResult> GetReportsByGuard(
        string userId, [FromQuery] int limit = 50, CancellationToken ct = default)
    {
        var reports = await _guardPort.GetReportsByGuardAsync(userId, limit, ct);
        return Ok(reports);
    }

    [HttpGet("reports/nearby")]
    public async Task<IActionResult> GetNearbyReports(
        [FromQuery] double latitude, [FromQuery] double longitude,
        [FromQuery] double radiusMeters = 5000,
        [FromQuery] int limit = 50,
        [FromQuery] DateTime? since = null,
        CancellationToken ct = default)
    {
        var reports = await _guardPort.GetNearbyReportsAsync(
            latitude, longitude, radiusMeters, limit, since, ct);
        return Ok(reports);
    }

    [HttpGet("reports/queue/{status}")]
    public async Task<IActionResult> GetReportsByStatus(
        string status, [FromQuery] int limit = 50, CancellationToken ct = default)
    {
        if (!Enum.TryParse<ReportStatus>(status, ignoreCase: true, out var reportStatus))
            return BadRequest(new { error = $"Invalid status: {status}" });

        var reports = await _guardPort.GetReportsByStatusAsync(reportStatus, limit, ct);
        return Ok(reports);
    }

    // ─────────────────────────────────────────────────────────────
    // Escalation — Upgrade Report to Watch Call
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// UPGRADE a guard report to a full Watch call (SOS dispatch).
    /// This is the critical path: guard sees something, filed a report, and now
    /// the situation has worsened enough to need nearby responders.
    ///
    /// What happens:
    ///   1. Report status → Escalated, severity auto-upgraded
    ///   2. ResponseRequest created via IResponseCoordinationService
    ///      (uses report location, description, scope)
    ///   3. Nearby responders dispatched with full report context
    ///   4. Guard auto-acknowledged as first responder on scene
    ///   5. All report evidence attached to the response
    ///   6. SignalR broadcasts to dashboard + response group
    /// </summary>
    [HttpPost("reports/{reportId}/escalate")]
    public async Task<IActionResult> EscalateToWatchCall(
        string reportId,
        [FromBody] EscalateReportRequest request,
        CancellationToken ct)
    {
        var report = await _guardPort.GetReportAsync(reportId, ct);
        if (report is null)
            return NotFound(new { error = $"Report {reportId} not found" });

        if (report.Status == ReportStatus.Escalated)
            return Conflict(new { error = "Report is already escalated", report.EscalatedRequestId });

        var scope = request.Scope ?? ResponseScope.Neighborhood;

        _logger.LogWarning(
            "GUARD ESCALATION: Report {ReportId} by {GuardName} → Watch call. " +
            "Category={Category}, Severity={Severity}, Scope={Scope}. Reason: {Reason}",
            reportId, report.GuardName, report.Category, report.Severity, scope, request.Reason);

        // 1. Escalate the report (updates status, creates mock request ID)
        var escalated = await _guardPort.EscalateToWatchCallAsync(
            reportId, scope, request.Reason, ct);

        // 2. Create the actual SOS ResponseRequest via the coordination service
        //    In production, the mock adapter's requestId would be replaced by the real one.
        var triggerSource = $"GUARD_REPORT:{reportId}";
        var description = $"[Guard Report Escalation] {report.Title}\n\n" +
            $"Guard: {report.GuardName} ({report.GuardRole})\n" +
            $"Category: {report.Category}\n" +
            $"Original report: {report.Description}\n" +
            (request.Reason != null ? $"Escalation reason: {request.Reason}" : "");

        var response = await _coordinationService.CreateResponseAsync(
            report.GuardUserId,
            scope,
            report.Latitude,
            report.Longitude,
            description,
            triggerSource,
            ct);

        // 3. Auto-acknowledge the guard as first responder on scene
        await _coordinationService.AcknowledgeResponseAsync(
            response.RequestId,
            report.GuardUserId,
            report.GuardName,
            report.GuardRole.ToString(),
            report.Latitude,
            report.Longitude,
            0, // Distance = 0, guard is already on scene
            hasVehicle: true,
            estimatedArrivalMinutes: 0,
            ct: ct);

        // 4. Broadcast escalation event
        await _hubContext.Clients.All.SendAsync("GuardReportEscalated", new
        {
            escalated.ReportId,
            escalated.GuardUserId,
            escalated.GuardName,
            GuardRole = escalated.GuardRole.ToString(),
            Category = escalated.Category.ToString(),
            Severity = escalated.Severity.ToString(),
            escalated.Title,
            escalated.Latitude,
            escalated.Longitude,
            ResponseRequestId = response.RequestId,
            Scope = scope.ToString(),
            Reason = request.Reason,
            EscalatedAt = escalated.EscalatedAt
        }, ct);

        return Ok(new
        {
            escalated.ReportId,
            Status = escalated.Status.ToString(),
            ResponseRequestId = response.RequestId,
            Scope = scope.ToString(),
            Severity = escalated.Severity.ToString(),
            escalated.EscalatedAt,
            Message = "Report escalated to Watch call. Responders are being dispatched."
        });
    }

    // ─────────────────────────────────────────────────────────────
    // Resolve / Review / Evidence
    // ─────────────────────────────────────────────────────────────

    [HttpPost("reports/{reportId}/resolve")]
    public async Task<IActionResult> ResolveReport(
        string reportId, [FromBody] ResolveGuardReportRequest request, CancellationToken ct)
    {
        var resolved = await _guardPort.ResolveReportAsync(
            reportId, request.ResolvedBy ?? "system", request.ResolutionNotes, ct);

        await _hubContext.Clients.All.SendAsync("GuardReportResolved", new
        {
            resolved.ReportId,
            resolved.GuardName,
            resolved.ResolvedBy,
            resolved.ResolutionNotes,
            resolved.ResolvedAt
        }, ct);

        return Ok(resolved);
    }

    [HttpPost("reports/{reportId}/review")]
    public async Task<IActionResult> ReviewReport(
        string reportId, [FromBody] ReviewGuardReportRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ReviewedBy))
            return BadRequest(new { error = "ReviewedBy is required" });

        var reviewed = await _guardPort.ReviewReportAsync(
            reportId, request.ReviewedBy, request.ReviewNotes ?? "", ct);
        return Ok(reviewed);
    }

    [HttpPost("reports/{reportId}/evidence")]
    public async Task<IActionResult> AttachEvidence(
        string reportId, [FromBody] AttachEvidenceRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.EvidenceId))
            return BadRequest(new { error = "EvidenceId is required" });

        var updated = await _guardPort.AttachEvidenceAsync(reportId, request.EvidenceId, ct);
        return Ok(updated);
    }
}

// ─────────────────────────────────────────────────────────────
// Request DTOs
// ─────────────────────────────────────────────────────────────

public record SetDutyRequest(string UserId, bool IsOnDuty);

public record FileGuardReportRequest(
    string GuardUserId,
    string? GuardName = null,
    GuardRole GuardRole = GuardRole.NeighborhoodWatch,
    string? BadgeNumber = null,
    ReportCategory Category = ReportCategory.Other,
    ReportSeverity Severity = ReportSeverity.Low,
    string? Title = null,
    string? Description = null,
    double Latitude = 0,
    double Longitude = 0,
    double? AccuracyMeters = null,
    string? LocationDescription = null,
    IReadOnlyList<string>? EvidenceIds = null,
    string? PatrolRouteId = null,
    string? PostId = null,
    string? PropertyId = null
);

public record EscalateReportRequest(
    ResponseScope? Scope = null,   // Defaults to Neighborhood if null
    string? Reason = null          // Why the guard is escalating
);

public record ResolveGuardReportRequest(
    string? ResolvedBy = null,
    string? ResolutionNotes = null
);

public record ReviewGuardReportRequest(
    string ReviewedBy,
    string? ReviewNotes = null
);

public record AttachEvidenceRequest(string EvidenceId);
