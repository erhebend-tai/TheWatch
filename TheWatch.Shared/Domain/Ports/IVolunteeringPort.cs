// IVolunteeringPort — domain port for volunteer enrollment, profile management,
// verification, scheduling (designated responder program), and statistics.
//
// Architecture:
//   ┌────────────────────┐     ┌─────────────────────────┐     ┌──────────────────────────┐
//   │ Mobile / Dashboard  │────▶│ IVolunteeringPort        │────▶│ Adapter                  │
//   │ (enrollment CRUD,   │     │ .EnrollAsync()           │     │ (SQL, Cosmos, Firebase,  │
//   │  profile, schedule) │     │ .GetProfileAsync()       │     │  Mock)                   │
//   └────────────────────┘     │ .SetScheduleAsync()      │     └──────────────────────────┘
//                              └─────────────────────────┘
//
// Enrollment Requirements (enforced by the controller, stored by the adapter):
//   1. User must be 18+ years old (verified via DateOfBirth on PersonCapabilityProfile)
//   2. User must have completed at least one mock Watch Call (via IWatchCallPort)
//   3. User must accept volunteering terms (AcceptedTermsAt must be set)
//
// Designated Responder Program:
//   Volunteers who set a recurring availability schedule are "designated responders."
//   They receive PRIORITY dispatch over ad-hoc volunteers because they have committed
//   to specific time windows. The dispatch engine (IParticipationPort.FindEligibleRespondersAsync)
//   checks AvailabilitySchedule entries to prioritize these volunteers.
//
// Standards:
//   - NFPA 1600 — Standard on Continuity, Emergency, and Crisis Management (volunteer programs)
//   - CERT (Community Emergency Response Team) — FEMA volunteer training model
//   - Good Samaritan Laws (per state) — liability protection for trained volunteers
//
// Example — full enrollment flow:
//   // 1. User submits enrollment request
//   var profile = await port.EnrollAsync(new VolunteerProfile { UserId = "u-123", ... }, ct);
//   // profile.Status == VolunteerStatus.PendingTraining
//
//   // 2. User completes a mock Watch Call (tracked by IWatchCallPort)
//   // 3. User returns to volunteering, system checks training completion
//   var updated = await port.ActivateAfterTrainingAsync("u-123", ct);
//   // updated.Status == VolunteerStatus.Active
//
//   // 4. User sets their weekly schedule (designated responder)
//   await port.SetScheduleAsync("u-123", schedule, ct);
//
//   // 5. Query stats after some responses
//   var stats = await port.GetStatsAsync("u-123", ct);
//   // stats.TotalResponses == 12, stats.AverageResponseTimeMinutes == 4.2
//
// Write-Ahead Log:
//   WAL-VOL-001: IVolunteeringPort interface created — 10 methods
//   WAL-VOL-002: VolunteerProfile model — enrollment state + capabilities
//   WAL-VOL-003: VolunteerStats model — response metrics + leaderboard
//   WAL-VOL-004: AvailabilityScheduleEntry model — designated responder time windows
//   WAL-VOL-005: VerificationSubmission model — background check placeholder

namespace TheWatch.Shared.Domain.Ports;

// ═══════════════════════════════════════════════════════════════
// Volunteer Status
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Lifecycle status of a volunteer enrollment.
/// </summary>
public enum VolunteerStatus
{
    /// <summary>Enrolled but has not completed mock Watch Call training.</summary>
    PendingTraining,

    /// <summary>Training complete, awaiting optional background check.</summary>
    PendingVerification,

    /// <summary>Fully active volunteer — eligible for dispatch.</summary>
    Active,

    /// <summary>Suspended due to behavioral issue or failed verification.</summary>
    Suspended,

    /// <summary>Voluntarily withdrawn from the program.</summary>
    Withdrawn
}

// ═══════════════════════════════════════════════════════════════
// Volunteer Profile
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// A volunteer's enrollment profile including capabilities, certifications,
/// vehicle availability, and designated responder schedule.
///
/// Example:
///   new VolunteerProfile
///   {
///       UserId = "u-123",
///       DisplayName = "Maria S.",
///       Status = VolunteerStatus.Active,
///       HasVehicle = true,
///       IsCprCertified = true,
///       IsFirstAidCertified = true,
///       Languages = new List&lt;string&gt; { "en", "es" },
///       DateOfBirth = new DateTime(1990, 6, 15),
///       AcceptedTermsAt = DateTime.UtcNow
///   };
/// </summary>
public class VolunteerProfile
{
    /// <summary>Unique profile identifier (GUID).</summary>
    public string ProfileId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>User ID this volunteer profile belongs to.</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>Display name (first name + last initial recommended for privacy).</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Current enrollment status.</summary>
    public VolunteerStatus Status { get; set; } = VolunteerStatus.PendingTraining;

    /// <summary>Date of birth for age verification (must be 18+).</summary>
    public DateTime? DateOfBirth { get; set; }

    // ── Capabilities ──────────────────────────────────────────────

    /// <summary>Whether the volunteer has access to a vehicle for response.</summary>
    public bool HasVehicle { get; set; }

    /// <summary>CPR certification (American Heart Association, Red Cross, or equivalent).</summary>
    public bool IsCprCertified { get; set; }

    /// <summary>First Aid certification (Red Cross, OSHA, or equivalent).</summary>
    public bool IsFirstAidCertified { get; set; }

    /// <summary>EMT certification (NREMT or state-equivalent).</summary>
    public bool IsEmtCertified { get; set; }

    /// <summary>
    /// ISO 639-1 language codes the volunteer can communicate in.
    /// Used to match volunteers to incidents where the subject speaks a non-English language.
    /// Example: ["en", "es", "zh"]
    /// </summary>
    public List<string> Languages { get; set; } = new() { "en" };

    /// <summary>
    /// Additional certifications or skills (e.g., "CERT", "Wilderness First Responder",
    /// "Crisis Intervention Training", "Search and Rescue").
    /// </summary>
    public List<string> AdditionalCertifications { get; set; } = new();

    /// <summary>Maximum radius in meters this volunteer is willing to respond within.</summary>
    public double MaxResponseRadiusMeters { get; set; } = 5000;

    /// <summary>Whether the volunteer is willing to be the first person on scene.</summary>
    public bool WillingToBeFirstOnScene { get; set; } = true;

    // ── Training ──────────────────────────────────────────────────

    /// <summary>Number of mock Watch Calls completed (must be >= 1 to activate).</summary>
    public int MockCallsCompleted { get; set; }

    /// <summary>Whether the Watch Call training requirement has been satisfied.</summary>
    public bool TrainingComplete => MockCallsCompleted >= 1;

    // ── Verification ──────────────────────────────────────────────

    /// <summary>Whether background check has been submitted.</summary>
    public bool BackgroundCheckSubmitted { get; set; }

    /// <summary>Whether background check has been approved (placeholder — always true in mock).</summary>
    public bool BackgroundCheckApproved { get; set; }

    /// <summary>Date background check was submitted.</summary>
    public DateTime? BackgroundCheckSubmittedAt { get; set; }

    // ── Terms & Timestamps ────────────────────────────────────────

    /// <summary>When the user accepted the volunteering terms. Null means not accepted.</summary>
    public DateTime? AcceptedTermsAt { get; set; }

    /// <summary>When the user enrolled.</summary>
    public DateTime EnrolledAt { get; set; } = DateTime.UtcNow;

    /// <summary>Last profile update.</summary>
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}

// ═══════════════════════════════════════════════════════════════
// Availability Schedule (Designated Responder Program)
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// A recurring availability window for the designated responder program.
/// Volunteers who commit to specific time windows get priority dispatch.
///
/// Example:
///   new AvailabilityScheduleEntry
///   {
///       DayOfWeek = DayOfWeek.Saturday,
///       StartTime = new TimeOnly(9, 0),
///       EndTime = new TimeOnly(18, 0)
///   };
/// </summary>
public class AvailabilityScheduleEntry
{
    /// <summary>Day of the week this entry applies to.</summary>
    public DayOfWeek DayOfWeek { get; set; }

    /// <summary>Start time (local to the user's timezone).</summary>
    public TimeOnly StartTime { get; set; }

    /// <summary>End time (local to the user's timezone).</summary>
    public TimeOnly EndTime { get; set; }
}

// ═══════════════════════════════════════════════════════════════
// Volunteer Stats
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Aggregated statistics for a volunteer's response history.
///
/// Example:
///   stats.TotalResponses = 23;
///   stats.AverageResponseTimeMinutes = 4.2;
///   stats.AcceptanceRate = 0.87; // 87% of dispatches accepted
/// </summary>
public class VolunteerStats
{
    public string UserId { get; set; } = string.Empty;
    public int TotalResponses { get; set; }
    public int TotalDispatched { get; set; }
    public double AcceptanceRate { get; set; }
    public double AverageResponseTimeMinutes { get; set; }
    public int CheckInsResponded { get; set; }
    public int NeighborhoodResponded { get; set; }
    public int CommunityResponded { get; set; }
    public int EvacuationResponded { get; set; }
    public DateTime? LastResponseAt { get; set; }
    public DateTime MemberSince { get; set; }
}

// ═══════════════════════════════════════════════════════════════
// Leaderboard Entry
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Anonymized community stats entry for the volunteer leaderboard.
/// No identifying information — just rank, response count, and average time.
/// </summary>
public class LeaderboardEntry
{
    public int Rank { get; set; }
    public string AnonymizedName { get; set; } = string.Empty; // "Volunteer #42"
    public int TotalResponses { get; set; }
    public double AverageResponseTimeMinutes { get; set; }
    public double AcceptanceRate { get; set; }
    public bool IsCurrentUser { get; set; }
}

// ═══════════════════════════════════════════════════════════════
// Verification Submission
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Background check / verification document submission.
/// In production, integrates with a background check provider (e.g., Checkr, Sterling).
/// In mock mode, auto-approves after a simulated delay.
/// </summary>
public class VerificationSubmission
{
    public string SubmissionId { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = string.Empty;
    public string DocumentType { get; set; } = string.Empty; // "DRIVERS_LICENSE", "BACKGROUND_CHECK_CONSENT"
    public string? DocumentReference { get; set; }
    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
    public bool Approved { get; set; }
    public DateTime? ReviewedAt { get; set; }
}

// ═══════════════════════════════════════════════════════════════
// Port Interface
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Port for volunteer enrollment, profile management, verification,
/// scheduling, and statistics.
///
/// Adapters:
///   - MockVolunteeringAdapter: in-memory with seeded profiles (dev)
///   - Production: Firestore/SQL-backed enrollment, Checkr for background checks,
///     Hangfire for verification processing
/// </summary>
public interface IVolunteeringPort
{
    // ── Enrollment ────────────────────────────────────────────────

    /// <summary>Enroll a user as a volunteer. Returns profile with PendingTraining status.</summary>
    Task<VolunteerProfile> EnrollAsync(VolunteerProfile profile, CancellationToken ct = default);

    /// <summary>Get a volunteer profile by user ID. Returns null if not enrolled.</summary>
    Task<VolunteerProfile?> GetProfileAsync(string userId, CancellationToken ct = default);

    /// <summary>Update a volunteer's profile (capabilities, certifications, radius, etc.).</summary>
    Task<VolunteerProfile> UpdateProfileAsync(VolunteerProfile profile, CancellationToken ct = default);

    /// <summary>
    /// Activate a volunteer after they have completed mock Watch Call training.
    /// Checks IWatchCallPort enrollment for MockCallsCompleted >= 1.
    /// Transitions PendingTraining → Active (or PendingVerification if background check required).
    /// </summary>
    Task<VolunteerProfile> ActivateAfterTrainingAsync(string userId, CancellationToken ct = default);

    /// <summary>Withdraw from the volunteering program. Sets status to Withdrawn.</summary>
    Task<bool> WithdrawAsync(string userId, CancellationToken ct = default);

    // ── Verification ──────────────────────────────────────────────

    /// <summary>
    /// Submit verification documents for background check.
    /// In mock: auto-approves. In production: sends to background check provider.
    /// </summary>
    Task<VerificationSubmission> SubmitVerificationAsync(
        VerificationSubmission submission, CancellationToken ct = default);

    // ── Schedule (Designated Responder Program) ───────────────────

    /// <summary>
    /// Set the volunteer's recurring availability schedule.
    /// Volunteers with schedules are designated responders and get priority dispatch.
    /// </summary>
    Task SetScheduleAsync(string userId, IReadOnlyList<AvailabilityScheduleEntry> schedule,
        CancellationToken ct = default);

    /// <summary>Get the volunteer's availability schedule.</summary>
    Task<IReadOnlyList<AvailabilityScheduleEntry>> GetScheduleAsync(
        string userId, CancellationToken ct = default);

    // ── Stats & Leaderboard ───────────────────────────────────────

    /// <summary>Get response statistics for a volunteer.</summary>
    Task<VolunteerStats> GetStatsAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// Get anonymized community leaderboard.
    /// Top volunteers by response count, with anonymized names.
    /// </summary>
    Task<IReadOnlyList<LeaderboardEntry>> GetLeaderboardAsync(
        int top = 20, string? currentUserId = null, CancellationToken ct = default);
}
