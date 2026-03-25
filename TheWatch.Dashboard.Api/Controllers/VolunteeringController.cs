// VolunteeringController — REST endpoints for the volunteer enrollment, profile management,
// verification, scheduling (designated responder program), and community statistics.
//
// Endpoints:
//   POST   /api/volunteering/enroll            — Enroll as volunteer (age verification, capability declaration)
//   PUT    /api/volunteering/profile           — Update volunteer profile (vehicle, schedule, certifications)
//   GET    /api/volunteering/profile/{userId}  — Get volunteer profile
//   POST   /api/volunteering/verify            — Submit verification documents (background check placeholder)
//   GET    /api/volunteering/stats/{userId}    — Response stats (total responses, avg time, acceptance rate)
//   GET    /api/volunteering/leaderboard       — Anonymized community stats
//   POST   /api/volunteering/schedule          — Set availability schedule (designated responder program)
//   DELETE /api/volunteering/enroll            — Withdraw from volunteering
//
// Enrollment requirements (enforced here, not in the adapter):
//   1. Must be 18+ (age verification via DateOfBirth)
//   2. Must complete mock Watch Call training (tracked via IWatchCallPort)
//   3. Must accept volunteering terms (AcceptedTermsAt must be set)
//
// The controller is thin — validation + delegation to IVolunteeringPort.
//
// WAL: VolunteeringController created — 8 endpoints wired to IVolunteeringPort + IWatchCallPort.

using Microsoft.AspNetCore.Mvc;
using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Dashboard.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class VolunteeringController : ControllerBase
{
    private readonly IVolunteeringPort _volunteeringPort;
    private readonly IWatchCallPort _watchCallPort;
    private readonly IPersonCapabilityPort _capabilityPort;
    private readonly ILogger<VolunteeringController> _logger;

    public VolunteeringController(
        IVolunteeringPort volunteeringPort,
        IWatchCallPort watchCallPort,
        IPersonCapabilityPort capabilityPort,
        ILogger<VolunteeringController> logger)
    {
        _volunteeringPort = volunteeringPort;
        _watchCallPort = watchCallPort;
        _capabilityPort = capabilityPort;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────
    // Enrollment
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Enroll as a volunteer. Validates:
    ///   1. DateOfBirth is present and user is 18+
    ///   2. AcceptedTermsAt is set (user accepted terms)
    ///
    /// Training completion is NOT required at enrollment time — the profile
    /// starts in PendingTraining status. The user must complete a mock Watch Call
    /// and then the system auto-activates them.
    ///
    /// Example request:
    ///   POST /api/volunteering/enroll
    ///   {
    ///     "userId": "u-123",
    ///     "displayName": "Maria S.",
    ///     "dateOfBirth": "1990-06-15",
    ///     "hasVehicle": true,
    ///     "isCprCertified": true,
    ///     "isFirstAidCertified": true,
    ///     "isEmtCertified": false,
    ///     "languages": ["en", "es"],
    ///     "acceptedTermsAt": "2026-03-24T10:00:00Z"
    ///   }
    /// </summary>
    [HttpPost("enroll")]
    public async Task<IActionResult> Enroll(
        [FromBody] EnrollVolunteerRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.UserId))
            return BadRequest(new { error = "UserId is required" });

        // Age verification: must be 18+
        if (!request.DateOfBirth.HasValue)
            return BadRequest(new { error = "DateOfBirth is required for age verification" });

        var age = CalculateAge(request.DateOfBirth.Value);
        if (age < 18)
            return BadRequest(new { error = "Must be 18 or older to volunteer", age });

        // Terms acceptance
        if (!request.AcceptedTermsAt.HasValue)
            return BadRequest(new { error = "Must accept volunteering terms before enrollment" });

        // Check if already enrolled
        var existing = await _volunteeringPort.GetProfileAsync(request.UserId, ct);
        if (existing is not null && existing.Status != VolunteerStatus.Withdrawn)
            return Conflict(new { error = "User is already enrolled", status = existing.Status.ToString() });

        // Check Watch Call training status
        var watchCallEnrollment = await _watchCallPort.GetEnrollmentAsync(request.UserId, ct);
        var mockCallsCompleted = watchCallEnrollment?.MockCallsCompleted ?? 0;

        var profile = new VolunteerProfile
        {
            UserId = request.UserId,
            DisplayName = request.DisplayName ?? "Volunteer",
            DateOfBirth = request.DateOfBirth,
            HasVehicle = request.HasVehicle,
            IsCprCertified = request.IsCprCertified,
            IsFirstAidCertified = request.IsFirstAidCertified,
            IsEmtCertified = request.IsEmtCertified,
            Languages = request.Languages ?? new List<string> { "en" },
            AdditionalCertifications = request.AdditionalCertifications ?? new List<string>(),
            MaxResponseRadiusMeters = request.MaxResponseRadiusMeters ?? 5000,
            WillingToBeFirstOnScene = request.WillingToBeFirstOnScene,
            MockCallsCompleted = mockCallsCompleted,
            AcceptedTermsAt = request.AcceptedTermsAt
        };

        var enrolled = await _volunteeringPort.EnrollAsync(profile, ct);

        // If user already completed mock training, activate immediately
        if (mockCallsCompleted >= 1)
        {
            enrolled = await _volunteeringPort.ActivateAfterTrainingAsync(request.UserId, ct);
        }

        _logger.LogInformation(
            "Volunteer enrollment: UserId={UserId}, Status={Status}, Age={Age}, MockCalls={MockCalls}",
            request.UserId, enrolled.Status, age, mockCallsCompleted);

        return CreatedAtAction(
            nameof(GetProfile),
            new { userId = enrolled.UserId },
            new
            {
                enrolled.ProfileId,
                enrolled.UserId,
                enrolled.DisplayName,
                Status = enrolled.Status.ToString(),
                enrolled.HasVehicle,
                enrolled.IsCprCertified,
                enrolled.IsFirstAidCertified,
                enrolled.IsEmtCertified,
                enrolled.Languages,
                enrolled.TrainingComplete,
                enrolled.MockCallsCompleted,
                enrolled.EnrolledAt,
                TrainingRequired = !enrolled.TrainingComplete,
                Message = enrolled.TrainingComplete
                    ? "Enrollment complete! You are now an active volunteer."
                    : "Enrollment received. Please complete a mock Watch Call to activate your volunteer status."
            });
    }

    /// <summary>
    /// Withdraw from the volunteering program.
    /// Sets profile status to Withdrawn. The user can re-enroll later.
    /// </summary>
    [HttpDelete("enroll")]
    public async Task<IActionResult> Withdraw(
        [FromBody] WithdrawRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.UserId))
            return BadRequest(new { error = "UserId is required" });

        var success = await _volunteeringPort.WithdrawAsync(request.UserId, ct);
        if (!success)
            return NotFound(new { error = "Volunteer profile not found" });

        _logger.LogInformation("Volunteer withdrawal: UserId={UserId}", request.UserId);
        return Ok(new { request.UserId, status = "Withdrawn", message = "You have been withdrawn from the volunteering program." });
    }

    // ─────────────────────────────────────────────────────────────
    // Profile
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Get a volunteer's profile by user ID.
    /// Returns 404 if the user is not enrolled.
    /// </summary>
    [HttpGet("profile/{userId}")]
    public async Task<IActionResult> GetProfile(string userId, CancellationToken ct)
    {
        var profile = await _volunteeringPort.GetProfileAsync(userId, ct);
        if (profile is null)
            return NotFound(new { error = $"No volunteer profile for user {userId}" });

        var schedule = await _volunteeringPort.GetScheduleAsync(userId, ct);

        return Ok(new
        {
            profile.ProfileId,
            profile.UserId,
            profile.DisplayName,
            Status = profile.Status.ToString(),
            profile.HasVehicle,
            profile.IsCprCertified,
            profile.IsFirstAidCertified,
            profile.IsEmtCertified,
            profile.Languages,
            profile.AdditionalCertifications,
            profile.MaxResponseRadiusMeters,
            profile.WillingToBeFirstOnScene,
            profile.TrainingComplete,
            profile.MockCallsCompleted,
            profile.BackgroundCheckSubmitted,
            profile.BackgroundCheckApproved,
            profile.EnrolledAt,
            profile.LastUpdated,
            IsDesignatedResponder = schedule.Count > 0,
            ScheduleEntries = schedule.Select(s => new
            {
                DayOfWeek = s.DayOfWeek.ToString(),
                StartTime = s.StartTime.ToString("HH:mm"),
                EndTime = s.EndTime.ToString("HH:mm")
            })
        });
    }

    /// <summary>
    /// Update a volunteer's profile. Partial update — only provided fields are changed.
    ///
    /// Example request:
    ///   PUT /api/volunteering/profile
    ///   {
    ///     "userId": "u-123",
    ///     "hasVehicle": true,
    ///     "isCprCertified": true,
    ///     "languages": ["en", "es", "fr"],
    ///     "maxResponseRadiusMeters": 8000
    ///   }
    /// </summary>
    [HttpPut("profile")]
    public async Task<IActionResult> UpdateProfile(
        [FromBody] UpdateVolunteerProfileRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.UserId))
            return BadRequest(new { error = "UserId is required" });

        var existing = await _volunteeringPort.GetProfileAsync(request.UserId, ct);
        if (existing is null)
            return NotFound(new { error = "Volunteer profile not found" });

        // Apply updates (only non-null fields)
        if (request.HasVehicle.HasValue)
            existing.HasVehicle = request.HasVehicle.Value;
        if (request.IsCprCertified.HasValue)
            existing.IsCprCertified = request.IsCprCertified.Value;
        if (request.IsFirstAidCertified.HasValue)
            existing.IsFirstAidCertified = request.IsFirstAidCertified.Value;
        if (request.IsEmtCertified.HasValue)
            existing.IsEmtCertified = request.IsEmtCertified.Value;
        if (request.Languages is not null)
            existing.Languages = request.Languages;
        if (request.AdditionalCertifications is not null)
            existing.AdditionalCertifications = request.AdditionalCertifications;
        if (request.MaxResponseRadiusMeters.HasValue)
            existing.MaxResponseRadiusMeters = request.MaxResponseRadiusMeters.Value;
        if (request.WillingToBeFirstOnScene.HasValue)
            existing.WillingToBeFirstOnScene = request.WillingToBeFirstOnScene.Value;
        if (!string.IsNullOrWhiteSpace(request.DisplayName))
            existing.DisplayName = request.DisplayName;

        var updated = await _volunteeringPort.UpdateProfileAsync(existing, ct);

        _logger.LogInformation("Volunteer profile updated: UserId={UserId}", request.UserId);

        return Ok(new
        {
            updated.ProfileId,
            updated.UserId,
            updated.DisplayName,
            Status = updated.Status.ToString(),
            updated.HasVehicle,
            updated.IsCprCertified,
            updated.IsFirstAidCertified,
            updated.IsEmtCertified,
            updated.Languages,
            updated.AdditionalCertifications,
            updated.MaxResponseRadiusMeters,
            updated.WillingToBeFirstOnScene,
            updated.LastUpdated
        });
    }

    // ─────────────────────────────────────────────────────────────
    // Verification
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Submit verification documents for background check.
    /// In mock mode: auto-approves immediately.
    /// In production: sends to background check provider (Checkr, Sterling, etc.).
    ///
    /// Example request:
    ///   POST /api/volunteering/verify
    ///   {
    ///     "userId": "u-123",
    ///     "documentType": "BACKGROUND_CHECK_CONSENT",
    ///     "documentReference": "doc-ref-abc123"
    ///   }
    /// </summary>
    [HttpPost("verify")]
    public async Task<IActionResult> SubmitVerification(
        [FromBody] VerificationRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.UserId))
            return BadRequest(new { error = "UserId is required" });
        if (string.IsNullOrWhiteSpace(request.DocumentType))
            return BadRequest(new { error = "DocumentType is required" });

        var profile = await _volunteeringPort.GetProfileAsync(request.UserId, ct);
        if (profile is null)
            return NotFound(new { error = "Volunteer profile not found. Enroll first." });

        var submission = new VerificationSubmission
        {
            UserId = request.UserId,
            DocumentType = request.DocumentType,
            DocumentReference = request.DocumentReference
        };

        var result = await _volunteeringPort.SubmitVerificationAsync(submission, ct);

        _logger.LogInformation(
            "Verification submitted: UserId={UserId}, Type={Type}, Approved={Approved}",
            request.UserId, request.DocumentType, result.Approved);

        return Ok(new
        {
            result.SubmissionId,
            result.UserId,
            result.DocumentType,
            result.Approved,
            result.SubmittedAt,
            result.ReviewedAt,
            Message = result.Approved
                ? "Background check approved."
                : "Background check submitted and under review. You will be notified when complete."
        });
    }

    // ─────────────────────────────────────────────────────────────
    // Stats & Leaderboard
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Get response statistics for a volunteer.
    /// Includes total responses, average response time, acceptance rate,
    /// and breakdown by scope.
    /// </summary>
    [HttpGet("stats/{userId}")]
    public async Task<IActionResult> GetStats(string userId, CancellationToken ct)
    {
        var profile = await _volunteeringPort.GetProfileAsync(userId, ct);
        if (profile is null)
            return NotFound(new { error = "Volunteer profile not found" });

        var stats = await _volunteeringPort.GetStatsAsync(userId, ct);

        return Ok(new
        {
            stats.UserId,
            stats.TotalResponses,
            stats.TotalDispatched,
            AcceptanceRate = $"{stats.AcceptanceRate:P0}",
            AcceptanceRateRaw = stats.AcceptanceRate,
            stats.AverageResponseTimeMinutes,
            Breakdown = new
            {
                stats.CheckInsResponded,
                stats.NeighborhoodResponded,
                stats.CommunityResponded,
                stats.EvacuationResponded
            },
            stats.LastResponseAt,
            stats.MemberSince
        });
    }

    /// <summary>
    /// Get anonymized community volunteer leaderboard.
    /// No identifying information — ranked by total responses.
    ///
    /// Example response:
    ///   [
    ///     { rank: 1, anonymizedName: "Volunteer #7", totalResponses: 52, ... },
    ///     { rank: 2, anonymizedName: "Volunteer #14", totalResponses: 44, ... }
    ///   ]
    /// </summary>
    [HttpGet("leaderboard")]
    public async Task<IActionResult> GetLeaderboard(
        [FromQuery] int top = 20,
        [FromQuery] string? currentUserId = null,
        CancellationToken ct = default)
    {
        var leaderboard = await _volunteeringPort.GetLeaderboardAsync(top, currentUserId, ct);

        return Ok(leaderboard.Select(e => new
        {
            e.Rank,
            e.AnonymizedName,
            e.TotalResponses,
            e.AverageResponseTimeMinutes,
            AcceptanceRate = $"{e.AcceptanceRate:P0}",
            AcceptanceRateRaw = e.AcceptanceRate,
            e.IsCurrentUser
        }));
    }

    // ─────────────────────────────────────────────────────────────
    // Schedule (Designated Responder Program)
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Set the volunteer's recurring availability schedule for the designated responder program.
    /// Volunteers with schedules get priority dispatch during their committed windows.
    ///
    /// Example request:
    ///   POST /api/volunteering/schedule
    ///   {
    ///     "userId": "u-123",
    ///     "entries": [
    ///       { "dayOfWeek": 6, "startTime": "09:00", "endTime": "18:00" },
    ///       { "dayOfWeek": 0, "startTime": "09:00", "endTime": "18:00" }
    ///     ]
    ///   }
    /// </summary>
    [HttpPost("schedule")]
    public async Task<IActionResult> SetSchedule(
        [FromBody] SetScheduleRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.UserId))
            return BadRequest(new { error = "UserId is required" });

        var profile = await _volunteeringPort.GetProfileAsync(request.UserId, ct);
        if (profile is null)
            return NotFound(new { error = "Volunteer profile not found. Enroll first." });

        if (profile.Status != VolunteerStatus.Active)
            return BadRequest(new { error = "Must be an active volunteer to set a schedule", status = profile.Status.ToString() });

        var entries = (request.Entries ?? Enumerable.Empty<ScheduleEntryRequest>())
            .Select(e => new AvailabilityScheduleEntry
            {
                DayOfWeek = (DayOfWeek)e.DayOfWeek,
                StartTime = TimeOnly.Parse(e.StartTime),
                EndTime = TimeOnly.Parse(e.EndTime)
            })
            .ToList();

        await _volunteeringPort.SetScheduleAsync(request.UserId, entries, ct);

        _logger.LogInformation(
            "Schedule set for {UserId}: {Count} time windows (designated responder)",
            request.UserId, entries.Count);

        return Ok(new
        {
            request.UserId,
            IsDesignatedResponder = entries.Count > 0,
            ScheduleEntries = entries.Select(e => new
            {
                DayOfWeek = e.DayOfWeek.ToString(),
                StartTime = e.StartTime.ToString("HH:mm"),
                EndTime = e.EndTime.ToString("HH:mm")
            }),
            Message = entries.Count > 0
                ? $"You are now a designated responder with {entries.Count} scheduled time windows. You will receive priority dispatch during these times."
                : "Schedule cleared. You are still an active volunteer but no longer a designated responder."
        });
    }

    // ─────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Calculate age in years from a date of birth.
    /// Accounts for leap years and birthday-not-yet-passed scenarios.
    /// </summary>
    private static int CalculateAge(DateTime dateOfBirth)
    {
        var today = DateTime.UtcNow.Date;
        var age = today.Year - dateOfBirth.Year;
        if (dateOfBirth.Date > today.AddYears(-age))
            age--;
        return age;
    }
}

// ─────────────────────────────────────────────────────────────
// Request DTOs
// ─────────────────────────────────────────────────────────────

public record EnrollVolunteerRequest(
    string UserId,
    string? DisplayName = null,
    DateTime? DateOfBirth = null,
    bool HasVehicle = false,
    bool IsCprCertified = false,
    bool IsFirstAidCertified = false,
    bool IsEmtCertified = false,
    List<string>? Languages = null,
    List<string>? AdditionalCertifications = null,
    double? MaxResponseRadiusMeters = null,
    bool WillingToBeFirstOnScene = true,
    DateTime? AcceptedTermsAt = null
);

public record WithdrawRequest(string UserId);

public record UpdateVolunteerProfileRequest(
    string UserId,
    string? DisplayName = null,
    bool? HasVehicle = null,
    bool? IsCprCertified = null,
    bool? IsFirstAidCertified = null,
    bool? IsEmtCertified = null,
    List<string>? Languages = null,
    List<string>? AdditionalCertifications = null,
    double? MaxResponseRadiusMeters = null,
    bool? WillingToBeFirstOnScene = null
);

public record VerificationRequest(
    string UserId,
    string DocumentType,
    string? DocumentReference = null
);

public record SetScheduleRequest(
    string UserId,
    List<ScheduleEntryRequest>? Entries = null
);

public record ScheduleEntryRequest(
    int DayOfWeek, // 0=Sunday, 1=Monday, ..., 6=Saturday
    string StartTime, // "HH:mm" format
    string EndTime    // "HH:mm" format
);
