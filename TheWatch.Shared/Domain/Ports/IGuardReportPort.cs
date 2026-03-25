// =============================================================================
// IGuardReportPort — port interfaces for security guard reporting and escalation.
// =============================================================================
// Security guards (professional, volunteer, neighborhood watch captains, property
// managers, campus security, event staff) file structured reports while on patrol
// or monitoring. Reports start as low-friction observations and can be UPGRADED
// to a full Watch call (SOS dispatch) with one action if the situation warrants.
//
// Key design:
//   - A GuardReport is NOT an SOS. It's a structured observation log entry.
//   - A GuardReport CAN become an SOS via EscalateToWatchCallAsync().
//   - When escalated, the report's location, description, evidence attachments,
//     and guard identity all flow into the ResponseRequest automatically.
//   - Guards see a simplified UI: "File Report" vs "Call The Watch" (escalate).
//
// Report lifecycle:
//   DRAFT → FILED → (optionally) ESCALATED → RESOLVED / EXPIRED
//                  → UNDER_REVIEW (if flagged by supervisor)
//
// Example: Guard on foot patrol
//   Guard sees broken window at 123 Oak St at 11:30 PM.
//   Files report: category=PropertyDamage, severity=Medium, description="Broken window,
//     ground floor, glass on sidewalk. No one visible inside."
//   Attaches photo via evidence system.
//   Report filed. Supervisor notified.
//
//   15 minutes later, guard sees someone climbing through the window.
//   Taps "Upgrade to Watch Call" on the existing report.
//   → Report status → ESCALATED
//   → ResponseRequest created (Scope: Neighborhood, location from report)
//   → Nearby responders dispatched with full report context
//   → Guard becomes first acknowledged responder automatically
//
// Example: Neighborhood watch volunteer
//   Sees unfamiliar vehicle circling the block 3 times.
//   Files report: category=SuspiciousActivity, severity=Low,
//     description="Dark SUV circling Elm St block, no plates visible, 3 passes in 10 min."
//   Report filed as observation only. No dispatch.
//
//   Vehicle stops and a person approaches a house at 2 AM.
//   Guard escalates the existing report.
//   → Watch call dispatched. Report history provides full context.
//
// Guard roles (not exclusive with responder roles — a guard CAN also be a responder):
//   - PROFESSIONAL_GUARD: Licensed security officer (G-license, armed/unarmed)
//   - NEIGHBORHOOD_WATCH: Volunteer neighborhood watch member/captain
//   - PROPERTY_MANAGER: Building/HOA manager with security responsibilities
//   - CAMPUS_SECURITY: School/university security staff
//   - EVENT_STAFF: Temporary security for events (concerts, sports, etc.)
//   - PARK_RANGER: Parks & recreation patrol staff
//   - TRANSIT_SECURITY: Public transit security
//   - CUSTOM: User-defined guard role
//
// WAL: Guard reports are persisted even if the app crashes mid-submission.
//      Draft reports auto-save every 10 seconds. Location is captured at
//      file time, not draft time, to reflect where the guard actually was.
// =============================================================================

namespace TheWatch.Shared.Domain.Ports;

// ═══════════════════════════════════════════════════════════════
// Guard Role & Report Enums
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// The type of security/guard role the reporting user holds.
/// </summary>
public enum GuardRole
{
    ProfessionalGuard,
    NeighborhoodWatch,
    PropertyManager,
    CampusSecurity,
    EventStaff,
    ParkRanger,
    TransitSecurity,
    Custom
}

/// <summary>
/// Category of the guard report. Determines default severity, suggested
/// escalation thresholds, and which supervisor queue receives the report.
/// </summary>
public enum ReportCategory
{
    /// <summary>Person or vehicle behaving unusually. Low baseline severity.</summary>
    SuspiciousActivity,

    /// <summary>Trespassing, unauthorized entry, fence breach.</summary>
    Trespass,

    /// <summary>Vandalism, broken windows, graffiti, damaged property.</summary>
    PropertyDamage,

    /// <summary>Disturbance, noise complaint, altercation between parties.</summary>
    Disturbance,

    /// <summary>Medical situation observed (person down, visible injury).</summary>
    MedicalObservation,

    /// <summary>Fire, smoke, gas leak, hazardous material.</summary>
    FireHazard,

    /// <summary>Theft observed or evidence of theft (broken lock, missing items).</summary>
    TheftBurglary,

    /// <summary>Assault, weapon, active threat.</summary>
    Assault,

    /// <summary>Environmental hazard (flooding, downed tree, ice, exposed wiring).</summary>
    EnvironmentalHazard,

    /// <summary>Vehicle-related (abandoned vehicle, accident, reckless driving).</summary>
    VehicleIncident,

    /// <summary>Missing person, child alone, welfare concern.</summary>
    WelfareConcern,

    /// <summary>Routine patrol observation — nothing specific, logged for record.</summary>
    RoutineObservation,

    /// <summary>Infrastructure issue (broken light, malfunctioning gate, camera down).</summary>
    InfrastructureIssue,

    /// <summary>Other — free-form report that doesn't fit a category.</summary>
    Other
}

/// <summary>
/// Assessed severity of the reported situation.
/// Guards set this initially; it can be adjusted by supervisors or auto-adjusted on escalation.
/// </summary>
public enum ReportSeverity
{
    /// <summary>Informational only. Logged but no action needed.</summary>
    Info,

    /// <summary>Low concern. Supervisor review at next opportunity.</summary>
    Low,

    /// <summary>Medium concern. Supervisor notified within 15 minutes.</summary>
    Medium,

    /// <summary>High concern. Supervisor notified immediately. Consider escalation.</summary>
    High,

    /// <summary>Critical. Auto-suggests escalation to Watch call.</summary>
    Critical
}

/// <summary>
/// Lifecycle status of a guard report.
/// </summary>
public enum ReportStatus
{
    /// <summary>Report started but not yet submitted (auto-saving).</summary>
    Draft,

    /// <summary>Report submitted. Visible to supervisors and other guards in the area.</summary>
    Filed,

    /// <summary>Report is being reviewed by a supervisor.</summary>
    UnderReview,

    /// <summary>
    /// Report was ESCALATED to a full Watch call (SOS dispatch).
    /// The EscalatedRequestId links to the ResponseRequest.
    /// </summary>
    Escalated,

    /// <summary>Report resolved — situation handled, no further action.</summary>
    Resolved,

    /// <summary>Report expired — no activity within retention window.</summary>
    Expired
}

// ═══════════════════════════════════════════════════════════════
// Guard Report Record
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// A structured report filed by a security guard or watch volunteer.
///
/// Example:
///   new GuardReport(
///       ReportId: "gr-001",
///       GuardUserId: "guard-456",
///       GuardName: "Officer Martinez",
///       GuardRole: GuardRole.ProfessionalGuard,
///       Category: ReportCategory.SuspiciousActivity,
///       Severity: ReportSeverity.Medium,
///       Title: "Unknown person at loading dock after hours",
///       Description: "Male, dark clothing, trying door handles at loading dock B. No vehicle visible.",
///       ...
///   );
/// </summary>
public record GuardReport(
    string ReportId,
    string GuardUserId,
    string GuardName,
    GuardRole GuardRole,
    string? BadgeNumber,           // Professional guards may have badge/license numbers

    // Classification
    ReportCategory Category,
    ReportSeverity Severity,
    string Title,                  // Short summary (< 120 chars)
    string Description,            // Full narrative

    // Location
    double Latitude,
    double Longitude,
    double? AccuracyMeters,
    string? LocationDescription,   // "Loading Dock B, rear of building", "Corner of Elm & 5th"

    // Status
    ReportStatus Status,
    DateTime CreatedAt,
    DateTime? FiledAt,             // When the report was submitted (null if draft)
    DateTime? EscalatedAt,         // When it was upgraded to a Watch call
    DateTime? ResolvedAt,

    // Escalation link — populated when guard upgrades to a Watch call
    string? EscalatedRequestId,    // The ResponseRequest.RequestId created on escalation
    ResponseScope? EscalatedScope, // What scope was used for the Watch call

    // Evidence — IDs of EvidenceSubmission records attached to this report
    IReadOnlyList<string>? EvidenceIds,

    // Patrol context
    string? PatrolRouteId,         // If guard is on a defined patrol route
    string? PostId,                // If guard is assigned to a fixed post/zone
    string? PropertyId,            // Property/building this report pertains to

    // Supervisor review
    string? ReviewedBy,            // Supervisor who reviewed the report
    string? ReviewNotes,           // Supervisor's notes
    DateTime? ReviewedAt,

    // Resolution
    string? ResolutionNotes,       // How the situation was resolved
    string? ResolvedBy             // Who resolved it (guard, supervisor, or "system" for auto-resolve)
);

// ═══════════════════════════════════════════════════════════════
// Guard Profile
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Guard enrollment profile. Separate from ParticipationPreferences because
/// guard duty has different requirements (shifts, posts, credentials).
/// A user can be BOTH a guard AND a volunteer responder.
/// </summary>
public record GuardProfile(
    string UserId,
    string Name,
    GuardRole Role,
    string? BadgeNumber,
    string? LicenseNumber,         // State security license (e.g., Texas DPS guard card)
    string? Organization,          // "Acme Security", "Oak Hills HOA", "UT Campus Police"

    // What this guard is authorized to do
    bool CanFileReports,
    bool CanEscalateToWatch,       // Can upgrade reports to Watch calls
    bool CanReviewOtherReports,    // Supervisor-level
    bool CanAccessCCTV,            // Can view linked camera feeds

    // Assignment
    string? CurrentPostId,
    string? CurrentPatrolRouteId,
    bool IsOnDuty,
    DateTime? ShiftStartUtc,
    DateTime? ShiftEndUtc,

    DateTime EnrolledAt,
    DateTime LastActiveAt
);

// ═══════════════════════════════════════════════════════════════
// Port Interface
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Port for guard reporting, patrol management, and report-to-SOS escalation.
///
/// Adapters:
///   - MockGuardReportAdapter: in-memory simulation with seeded data (dev)
///   - Production: SQL/Firestore-backed with Hangfire for auto-expiration
///                 and SignalR for real-time supervisor notifications
/// </summary>
public interface IGuardReportPort
{
    // ── Guard Profiles ───────────────────────────────────────────

    /// <summary>Enroll a user as a guard.</summary>
    Task<GuardProfile> EnrollGuardAsync(GuardProfile profile, CancellationToken ct = default);

    /// <summary>Update a guard's profile (role, assignment, shift times).</summary>
    Task<GuardProfile> UpdateGuardProfileAsync(GuardProfile profile, CancellationToken ct = default);

    /// <summary>Get a guard's profile.</summary>
    Task<GuardProfile?> GetGuardProfileAsync(string userId, CancellationToken ct = default);

    /// <summary>Set a guard on/off duty.</summary>
    Task<GuardProfile> SetDutyStatusAsync(string userId, bool isOnDuty, CancellationToken ct = default);

    /// <summary>Get all on-duty guards (for supervisor dashboard).</summary>
    Task<IReadOnlyList<GuardProfile>> GetOnDutyGuardsAsync(CancellationToken ct = default);

    // ── Reports ──────────────────────────────────────────────────

    /// <summary>Create or update a draft report (auto-save).</summary>
    Task<GuardReport> SaveDraftAsync(GuardReport report, CancellationToken ct = default);

    /// <summary>
    /// File (submit) a report. Sets status to Filed and notifies supervisors.
    /// </summary>
    Task<GuardReport> FileReportAsync(GuardReport report, CancellationToken ct = default);

    /// <summary>Get a report by ID.</summary>
    Task<GuardReport?> GetReportAsync(string reportId, CancellationToken ct = default);

    /// <summary>Get all reports filed by a specific guard.</summary>
    Task<IReadOnlyList<GuardReport>> GetReportsByGuardAsync(
        string guardUserId, int limit = 50, CancellationToken ct = default);

    /// <summary>Get recent reports in an area (for situational awareness).</summary>
    Task<IReadOnlyList<GuardReport>> GetNearbyReportsAsync(
        double latitude, double longitude, double radiusMeters,
        int limit = 50, DateTime? since = null,
        CancellationToken ct = default);

    /// <summary>Get reports by status (for supervisor queues).</summary>
    Task<IReadOnlyList<GuardReport>> GetReportsByStatusAsync(
        ReportStatus status, int limit = 50, CancellationToken ct = default);

    /// <summary>
    /// ESCALATE a report to a full Watch call (SOS dispatch).
    /// Creates a ResponseRequest using the report's location and description.
    /// The guard is auto-acknowledged as the first responder on scene.
    /// Returns the updated report with EscalatedRequestId populated.
    ///
    /// Example:
    ///   var escalated = await EscalateToWatchCallAsync("gr-001", ResponseScope.Neighborhood);
    ///   // escalated.EscalatedRequestId → "resp-12345"
    ///   // Guard is now first responder, other responders dispatched
    /// </summary>
    Task<GuardReport> EscalateToWatchCallAsync(
        string reportId,
        ResponseScope scope,
        string? escalationReason = null,
        CancellationToken ct = default);

    /// <summary>Resolve a report (situation handled).</summary>
    Task<GuardReport> ResolveReportAsync(
        string reportId, string resolvedBy, string? resolutionNotes = null,
        CancellationToken ct = default);

    /// <summary>Supervisor reviews and adds notes to a report.</summary>
    Task<GuardReport> ReviewReportAsync(
        string reportId, string reviewedBy, string reviewNotes,
        CancellationToken ct = default);

    /// <summary>Attach evidence (photo, video) to a report.</summary>
    Task<GuardReport> AttachEvidenceAsync(
        string reportId, string evidenceId, CancellationToken ct = default);
}
