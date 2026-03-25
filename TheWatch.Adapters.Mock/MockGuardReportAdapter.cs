// =============================================================================
// Mock Guard Report Adapter — permanent first-class in-memory implementation.
// =============================================================================
// Simulates guard reporting lifecycle: enrollment, report filing, supervisor
// review, escalation to Watch call, and resolution.
//
// Seeded data:
//   - 3 guard profiles (professional guard, neighborhood watch, campus security)
//   - 4 reports across those guards (1 escalated, 1 under review, 2 filed)
//
// WAL: All operations in-memory. No external calls.
// =============================================================================

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Adapters.Mock;

public class MockGuardReportAdapter : IGuardReportPort
{
    private readonly ConcurrentDictionary<string, GuardProfile> _profiles = new();
    private readonly ConcurrentDictionary<string, GuardReport> _reports = new();
    private readonly ILogger<MockGuardReportAdapter> _logger;

    public MockGuardReportAdapter(ILogger<MockGuardReportAdapter> logger)
    {
        _logger = logger;
        SeedMockData();
    }

    private void SeedMockData()
    {
        var profiles = new[]
        {
            new GuardProfile(
                UserId: "guard-001", Name: "Officer Martinez",
                Role: GuardRole.ProfessionalGuard,
                BadgeNumber: "G-4472", LicenseNumber: "TX-PSB-881234",
                Organization: "Allied Universal Security",
                CanFileReports: true, CanEscalateToWatch: true,
                CanReviewOtherReports: false, CanAccessCCTV: true,
                CurrentPostId: "post-lobby-A", CurrentPatrolRouteId: null,
                IsOnDuty: true,
                ShiftStartUtc: DateTime.UtcNow.AddHours(-4),
                ShiftEndUtc: DateTime.UtcNow.AddHours(4),
                EnrolledAt: DateTime.UtcNow.AddDays(-120),
                LastActiveAt: DateTime.UtcNow.AddMinutes(-5)),

            new GuardProfile(
                UserId: "guard-002", Name: "Linda Chen",
                Role: GuardRole.NeighborhoodWatch,
                BadgeNumber: null, LicenseNumber: null,
                Organization: "Oak Hills Neighborhood Watch",
                CanFileReports: true, CanEscalateToWatch: true,
                CanReviewOtherReports: true, CanAccessCCTV: false,
                CurrentPostId: null, CurrentPatrolRouteId: "route-oak-hills-loop",
                IsOnDuty: true,
                ShiftStartUtc: DateTime.UtcNow.AddHours(-2),
                ShiftEndUtc: DateTime.UtcNow.AddHours(2),
                EnrolledAt: DateTime.UtcNow.AddDays(-60),
                LastActiveAt: DateTime.UtcNow.AddMinutes(-12)),

            new GuardProfile(
                UserId: "guard-003", Name: "James Park",
                Role: GuardRole.CampusSecurity,
                BadgeNumber: "CS-0091", LicenseNumber: null,
                Organization: "State University Campus Security",
                CanFileReports: true, CanEscalateToWatch: true,
                CanReviewOtherReports: false, CanAccessCCTV: true,
                CurrentPostId: null, CurrentPatrolRouteId: "route-campus-south",
                IsOnDuty: false,
                ShiftStartUtc: null, ShiftEndUtc: null,
                EnrolledAt: DateTime.UtcNow.AddDays(-90),
                LastActiveAt: DateTime.UtcNow.AddHours(-8)),
        };

        foreach (var p in profiles) _profiles[p.UserId] = p;

        var reports = new[]
        {
            new GuardReport(
                ReportId: "gr-001",
                GuardUserId: "guard-001", GuardName: "Officer Martinez",
                GuardRole: GuardRole.ProfessionalGuard, BadgeNumber: "G-4472",
                Category: ReportCategory.SuspiciousActivity,
                Severity: ReportSeverity.High,
                Title: "Person attempting entry at loading dock after hours",
                Description: "Male, 5'10\", dark hoodie, attempting to pry open loading dock B " +
                    "rear door. No vehicle visible in lot. Subject noticed me and fled " +
                    "eastbound on foot toward alley behind 200 block of Elm St.",
                Latitude: 30.2672, Longitude: -97.7431, AccuracyMeters: 5,
                LocationDescription: "Loading Dock B, rear of Westlake Office Building",
                Status: ReportStatus.Escalated,
                CreatedAt: DateTime.UtcNow.AddHours(-1),
                FiledAt: DateTime.UtcNow.AddHours(-1).AddMinutes(2),
                EscalatedAt: DateTime.UtcNow.AddMinutes(-50),
                ResolvedAt: null,
                EscalatedRequestId: "resp-guard-esc-001",
                EscalatedScope: ResponseScope.Neighborhood,
                EvidenceIds: new[] { "ev-photo-001", "ev-photo-002" },
                PatrolRouteId: null, PostId: "post-lobby-A",
                PropertyId: "prop-westlake-office",
                ReviewedBy: null, ReviewNotes: null, ReviewedAt: null,
                ResolutionNotes: null, ResolvedBy: null),

            new GuardReport(
                ReportId: "gr-002",
                GuardUserId: "guard-002", GuardName: "Linda Chen",
                GuardRole: GuardRole.NeighborhoodWatch, BadgeNumber: null,
                Category: ReportCategory.SuspiciousActivity,
                Severity: ReportSeverity.Low,
                Title: "Unfamiliar vehicle circling block repeatedly",
                Description: "Dark SUV, possibly Ford Explorer, circled the 300 block of " +
                    "Oak Hill Dr three times between 11:15-11:30 PM. Slowed in front of " +
                    "several houses. Could not make out plates in darkness.",
                Latitude: 30.2690, Longitude: -97.7450, AccuracyMeters: 10,
                LocationDescription: "300 block of Oak Hill Dr",
                Status: ReportStatus.Filed,
                CreatedAt: DateTime.UtcNow.AddMinutes(-45),
                FiledAt: DateTime.UtcNow.AddMinutes(-43),
                EscalatedAt: null, ResolvedAt: null,
                EscalatedRequestId: null, EscalatedScope: null,
                EvidenceIds: null,
                PatrolRouteId: "route-oak-hills-loop", PostId: null,
                PropertyId: null,
                ReviewedBy: null, ReviewNotes: null, ReviewedAt: null,
                ResolutionNotes: null, ResolvedBy: null),

            new GuardReport(
                ReportId: "gr-003",
                GuardUserId: "guard-001", GuardName: "Officer Martinez",
                GuardRole: GuardRole.ProfessionalGuard, BadgeNumber: "G-4472",
                Category: ReportCategory.InfrastructureIssue,
                Severity: ReportSeverity.Low,
                Title: "Parking lot light #7 out",
                Description: "Light pole #7 in section C of the south parking lot is out. " +
                    "Creates a dark zone approximately 30x20ft. Safety concern for " +
                    "employees leaving after dark.",
                Latitude: 30.2668, Longitude: -97.7435, AccuracyMeters: 8,
                LocationDescription: "South Parking Lot, Section C, Pole #7",
                Status: ReportStatus.UnderReview,
                CreatedAt: DateTime.UtcNow.AddHours(-3),
                FiledAt: DateTime.UtcNow.AddHours(-3).AddMinutes(1),
                EscalatedAt: null, ResolvedAt: null,
                EscalatedRequestId: null, EscalatedScope: null,
                EvidenceIds: new[] { "ev-photo-003" },
                PatrolRouteId: null, PostId: "post-lobby-A",
                PropertyId: "prop-westlake-office",
                ReviewedBy: "supervisor-001",
                ReviewNotes: "Maintenance ticket #M-2261 created. Temp portable light requested.",
                ReviewedAt: DateTime.UtcNow.AddHours(-2),
                ResolutionNotes: null, ResolvedBy: null),

            new GuardReport(
                ReportId: "gr-004",
                GuardUserId: "guard-003", GuardName: "James Park",
                GuardRole: GuardRole.CampusSecurity, BadgeNumber: "CS-0091",
                Category: ReportCategory.WelfareConcern,
                Severity: ReportSeverity.Medium,
                Title: "Student sleeping outdoors in cold weather",
                Description: "Found a student sleeping on a bench near the science building " +
                    "at 1:30 AM. Temperature is 38°F. Student responsive but groggy. " +
                    "Offered to walk them to the student center warming station.",
                Latitude: 30.2840, Longitude: -97.7360, AccuracyMeters: 3,
                LocationDescription: "Bench near Science Building, South Campus",
                Status: ReportStatus.Resolved,
                CreatedAt: DateTime.UtcNow.AddHours(-6),
                FiledAt: DateTime.UtcNow.AddHours(-6).AddMinutes(3),
                EscalatedAt: null,
                ResolvedAt: DateTime.UtcNow.AddHours(-5).AddMinutes(30),
                EscalatedRequestId: null, EscalatedScope: null,
                EvidenceIds: null,
                PatrolRouteId: "route-campus-south", PostId: null,
                PropertyId: null,
                ReviewedBy: null, ReviewNotes: null, ReviewedAt: null,
                ResolutionNotes: "Student escorted to warming station. " +
                    "Counseling services referral provided.",
                ResolvedBy: "guard-003"),
        };

        foreach (var r in reports) _reports[r.ReportId] = r;
    }

    // ═══════════════════════════════════════════════════════════════
    // Guard Profiles
    // ═══════════════════════════════════════════════════════════════

    public Task<GuardProfile> EnrollGuardAsync(GuardProfile profile, CancellationToken ct = default)
    {
        _profiles[profile.UserId] = profile with { EnrolledAt = DateTime.UtcNow, LastActiveAt = DateTime.UtcNow };
        _logger.LogInformation("[MockGuard] Enrolled: {UserId} ({Name}) as {Role}, org={Org}",
            profile.UserId, profile.Name, profile.Role, profile.Organization);
        return Task.FromResult(_profiles[profile.UserId]);
    }

    public Task<GuardProfile> UpdateGuardProfileAsync(GuardProfile profile, CancellationToken ct = default)
    {
        _profiles[profile.UserId] = profile with { LastActiveAt = DateTime.UtcNow };
        return Task.FromResult(_profiles[profile.UserId]);
    }

    public Task<GuardProfile?> GetGuardProfileAsync(string userId, CancellationToken ct = default)
    {
        _profiles.TryGetValue(userId, out var p);
        return Task.FromResult(p);
    }

    public Task<GuardProfile> SetDutyStatusAsync(string userId, bool isOnDuty, CancellationToken ct = default)
    {
        if (!_profiles.TryGetValue(userId, out var p))
            throw new KeyNotFoundException($"Guard {userId} not found");

        var updated = p with
        {
            IsOnDuty = isOnDuty,
            ShiftStartUtc = isOnDuty ? DateTime.UtcNow : null,
            ShiftEndUtc = isOnDuty ? DateTime.UtcNow.AddHours(8) : null,
            LastActiveAt = DateTime.UtcNow
        };
        _profiles[userId] = updated;

        _logger.LogInformation("[MockGuard] {UserId} duty status → {Status}", userId, isOnDuty ? "ON DUTY" : "OFF DUTY");
        return Task.FromResult(updated);
    }

    public Task<IReadOnlyList<GuardProfile>> GetOnDutyGuardsAsync(CancellationToken ct = default)
    {
        var onDuty = _profiles.Values.Where(p => p.IsOnDuty).OrderBy(p => p.Name).ToList();
        return Task.FromResult<IReadOnlyList<GuardProfile>>(onDuty);
    }

    // ═══════════════════════════════════════════════════════════════
    // Reports
    // ═══════════════════════════════════════════════════════════════

    public Task<GuardReport> SaveDraftAsync(GuardReport report, CancellationToken ct = default)
    {
        var id = string.IsNullOrEmpty(report.ReportId)
            ? $"gr-{Guid.NewGuid():N}"[..12]
            : report.ReportId;

        var draft = report with { ReportId = id, Status = ReportStatus.Draft, CreatedAt = DateTime.UtcNow };
        _reports[id] = draft;
        _logger.LogDebug("[MockGuard] Draft saved: {ReportId}", id);
        return Task.FromResult(draft);
    }

    public Task<GuardReport> FileReportAsync(GuardReport report, CancellationToken ct = default)
    {
        var id = string.IsNullOrEmpty(report.ReportId)
            ? $"gr-{Guid.NewGuid():N}"[..12]
            : report.ReportId;

        var filed = report with
        {
            ReportId = id,
            Status = ReportStatus.Filed,
            CreatedAt = report.CreatedAt == default ? DateTime.UtcNow : report.CreatedAt,
            FiledAt = DateTime.UtcNow
        };
        _reports[id] = filed;

        _logger.LogInformation(
            "[MockGuard] REPORT FILED: {ReportId} by {GuardName} ({GuardRole}) — " +
            "category={Category}, severity={Severity}: {Title}",
            id, report.GuardName, report.GuardRole, report.Category, report.Severity, report.Title);

        return Task.FromResult(filed);
    }

    public Task<GuardReport?> GetReportAsync(string reportId, CancellationToken ct = default)
    {
        _reports.TryGetValue(reportId, out var r);
        return Task.FromResult(r);
    }

    public Task<IReadOnlyList<GuardReport>> GetReportsByGuardAsync(
        string guardUserId, int limit = 50, CancellationToken ct = default)
    {
        var reports = _reports.Values
            .Where(r => r.GuardUserId == guardUserId)
            .OrderByDescending(r => r.CreatedAt)
            .Take(limit).ToList();
        return Task.FromResult<IReadOnlyList<GuardReport>>(reports);
    }

    public Task<IReadOnlyList<GuardReport>> GetNearbyReportsAsync(
        double latitude, double longitude, double radiusMeters,
        int limit = 50, DateTime? since = null, CancellationToken ct = default)
    {
        // Mock: return all non-draft reports (real impl uses spatial index)
        var query = _reports.Values.Where(r => r.Status != ReportStatus.Draft);
        if (since.HasValue) query = query.Where(r => r.CreatedAt > since.Value);

        var reports = query.OrderByDescending(r => r.CreatedAt).Take(limit).ToList();
        return Task.FromResult<IReadOnlyList<GuardReport>>(reports);
    }

    public Task<IReadOnlyList<GuardReport>> GetReportsByStatusAsync(
        ReportStatus status, int limit = 50, CancellationToken ct = default)
    {
        var reports = _reports.Values
            .Where(r => r.Status == status)
            .OrderByDescending(r => r.CreatedAt)
            .Take(limit).ToList();
        return Task.FromResult<IReadOnlyList<GuardReport>>(reports);
    }

    public Task<GuardReport> EscalateToWatchCallAsync(
        string reportId, ResponseScope scope, string? escalationReason = null,
        CancellationToken ct = default)
    {
        if (!_reports.TryGetValue(reportId, out var report))
            throw new KeyNotFoundException($"Report {reportId} not found");

        if (report.Status == ReportStatus.Escalated)
            throw new InvalidOperationException($"Report {reportId} is already escalated");

        // Create a mock ResponseRequest ID (in production, this calls IResponseCoordinationService)
        var requestId = $"resp-guard-{Guid.NewGuid():N}"[..20];

        var escalated = report with
        {
            Status = ReportStatus.Escalated,
            EscalatedAt = DateTime.UtcNow,
            EscalatedRequestId = requestId,
            EscalatedScope = scope,
            // Auto-upgrade severity to Critical on escalation
            Severity = report.Severity < ReportSeverity.High ? ReportSeverity.High : report.Severity
        };

        _reports[reportId] = escalated;

        _logger.LogWarning(
            "[MockGuard] *** ESCALATED TO WATCH CALL *** Report {ReportId} by {GuardName}: " +
            "{Title} → ResponseRequest {RequestId} (Scope: {Scope}). Reason: {Reason}",
            reportId, report.GuardName, report.Title, requestId, scope,
            escalationReason ?? "Guard observed escalation-worthy conditions");

        return Task.FromResult(escalated);
    }

    public Task<GuardReport> ResolveReportAsync(
        string reportId, string resolvedBy, string? resolutionNotes = null,
        CancellationToken ct = default)
    {
        if (!_reports.TryGetValue(reportId, out var report))
            throw new KeyNotFoundException($"Report {reportId} not found");

        var resolved = report with
        {
            Status = ReportStatus.Resolved,
            ResolvedAt = DateTime.UtcNow,
            ResolvedBy = resolvedBy,
            ResolutionNotes = resolutionNotes
        };
        _reports[reportId] = resolved;

        _logger.LogInformation("[MockGuard] Report {ReportId} resolved by {ResolvedBy}: {Notes}",
            reportId, resolvedBy, resolutionNotes ?? "(no notes)");
        return Task.FromResult(resolved);
    }

    public Task<GuardReport> ReviewReportAsync(
        string reportId, string reviewedBy, string reviewNotes,
        CancellationToken ct = default)
    {
        if (!_reports.TryGetValue(reportId, out var report))
            throw new KeyNotFoundException($"Report {reportId} not found");

        var reviewed = report with
        {
            Status = ReportStatus.UnderReview,
            ReviewedBy = reviewedBy,
            ReviewNotes = reviewNotes,
            ReviewedAt = DateTime.UtcNow
        };
        _reports[reportId] = reviewed;

        _logger.LogInformation("[MockGuard] Report {ReportId} reviewed by {Reviewer}", reportId, reviewedBy);
        return Task.FromResult(reviewed);
    }

    public Task<GuardReport> AttachEvidenceAsync(
        string reportId, string evidenceId, CancellationToken ct = default)
    {
        if (!_reports.TryGetValue(reportId, out var report))
            throw new KeyNotFoundException($"Report {reportId} not found");

        var existingIds = report.EvidenceIds?.ToList() ?? new List<string>();
        existingIds.Add(evidenceId);

        var updated = report with { EvidenceIds = existingIds };
        _reports[reportId] = updated;

        _logger.LogInformation("[MockGuard] Evidence {EvidenceId} attached to report {ReportId}",
            evidenceId, reportId);
        return Task.FromResult(updated);
    }
}
