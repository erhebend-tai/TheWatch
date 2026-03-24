// EvidenceSubmission — core entity linking a user's submission to an incident phase.
// Every piece of evidence (photo, video, audio, text, document) captured across
// pre-incident, active-incident, and post-incident phases is an EvidenceSubmission.
//
// Tamper detection: ContentHash (SHA-256) is computed client-side at capture time.
// The server re-verifies this hash via ContentHashVerificationJob.
//
// Offline support: When captured without connectivity, IsOfflineSubmission = true,
// OfflineCapturedAt records the real capture time, SubmittedAt records the sync time.
//
// Example:
//   var submission = new EvidenceSubmission
//   {
//       Id = Guid.NewGuid().ToString(),
//       RequestId = "req-123",        // null for pre-incident
//       UserId = "user-456",
//       SubmitterId = "user-456",      // same as user unless responder is submitting
//       Phase = SubmissionPhase.Active,
//       SubmissionType = SubmissionType.Image,
//       Title = "Kitchen fire",
//       ContentHash = "a1b2c3...",     // SHA-256
//       MimeType = "image/jpeg",
//       FileSizeBytes = 2_048_000,
//       BlobReference = "evidence/req-123/img-001.jpg",
//       Status = SubmissionStatus.Pending,
//       SubmittedAt = DateTime.UtcNow,
//       Latitude = 30.2672,
//       Longitude = -97.7431,
//       DeviceId = "device-789",
//       AppVersion = "1.0.0"
//   };

using TheWatch.Shared.Enums;

namespace TheWatch.Shared.Domain.Models;

public class EvidenceSubmission
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Linked ResponseRequest ID. Null for pre-incident submissions
    /// (dwelling photos, hazard docs captured before any SOS).
    /// </summary>
    public string? RequestId { get; set; }

    /// <summary>The user this evidence belongs to (the person in distress).</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Who actually submitted the evidence. Same as UserId for self-submissions.
    /// Different when a responder submits on behalf of the user.
    /// </summary>
    public string SubmitterId { get; set; } = string.Empty;

    /// <summary>Incident lifecycle phase at time of capture.</summary>
    public SubmissionPhase Phase { get; set; }

    /// <summary>Content type of the submission.</summary>
    public SubmissionType SubmissionType { get; set; }

    public string? Title { get; set; }
    public string? Description { get; set; }

    // ── Location ───────────────────────────────────
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double? Accuracy { get; set; }

    // ── Content integrity ──────────────────────────
    /// <summary>SHA-256 hash of the raw content bytes, computed at capture time.</summary>
    public string? ContentHash { get; set; }
    public string? MimeType { get; set; }
    public long FileSizeBytes { get; set; }

    // ── Blob storage pointers ──────────────────────
    /// <summary>Path/key in blob storage (e.g., "evidence/req-123/img-001.jpg").</summary>
    public string? BlobReference { get; set; }
    /// <summary>Path/key for the generated thumbnail, if applicable.</summary>
    public string? ThumbnailBlobReference { get; set; }

    // ── Lifecycle ──────────────────────────────────
    public SubmissionStatus Status { get; set; } = SubmissionStatus.Pending;
    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }

    // ── Offline support ────────────────────────────
    /// <summary>True if this was captured offline and synced later.</summary>
    public bool IsOfflineSubmission { get; set; }
    /// <summary>The real capture timestamp when offline (before sync).</summary>
    public DateTime? OfflineCapturedAt { get; set; }

    // ── Device metadata ────────────────────────────
    public string? DeviceId { get; set; }
    public string? DeviceModel { get; set; }
    public string? AppVersion { get; set; }
}
