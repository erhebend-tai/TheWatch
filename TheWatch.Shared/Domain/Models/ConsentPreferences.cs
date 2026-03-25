// ConsentPreferences — GDPR Article 7 consent record for each data-processing category.
//
// Each boolean field corresponds to a specific data-processing activity that requires
// explicit, informed, freely-given consent per GDPR Article 6(1)(a).
//
// The consent record is versioned: every update creates a new version with a timestamp,
// so we maintain a full audit trail of what the user consented to and when.
//
// Regulatory alignment:
//   - GDPR Article 7: Conditions for consent (must be demonstrable, withdrawable)
//   - GDPR Article 13: Information to be provided (what each category means)
//   - CCPA 1798.120: Right to opt-out of sale of personal information
//   - LGPD Article 8: Consent must be in writing or by other means
//   - Apple App Store 5.1.1: Data collection transparency
//   - Google Play Data Safety: Disclosure of data types collected
//
// Example:
//   var prefs = new ConsentPreferences
//   {
//       UserId = "u-123",
//       LocationTracking = true,
//       EvidenceStorage = true,
//       Analytics = false,          // User opts out of analytics
//       EmergencyContactSharing = true,
//       BiometricProcessing = false  // User opts out of voice/face
//   };
//
// WAL: ConsentPreferences is stored alongside the user profile. Every mutation
//      generates an AuditEntry with AuditAction.ConsentUpdated. The previous
//      ConsentPreferences is serialized into AuditEntry.OldValue and the new
//      one into AuditEntry.NewValue for full before/after diff.

namespace TheWatch.Shared.Domain.Models;

/// <summary>
/// User's GDPR consent preferences. Each field maps to a data-processing activity.
/// All fields default to false — consent must be affirmatively granted (no pre-checked boxes).
/// </summary>
public class ConsentPreferences
{
    /// <summary>User ID this consent record belongs to.</summary>
    public string UserId { get; set; } = string.Empty;

    // ── Data Collection Consents ──────────────────────────────────

    /// <summary>
    /// Consent to continuous location tracking (GPS, network, geofence).
    /// Required for: volunteer dispatch, SOS location sharing, geofenced alerts.
    /// GDPR basis: Article 6(1)(a) explicit consent for special category (location = behavioral profiling).
    /// </summary>
    public bool LocationTracking { get; set; }

    /// <summary>
    /// Consent to store evidence captured during incidents (photos, video, audio, screenshots).
    /// Evidence is encrypted at rest and subject to retention policy (default: 90 days).
    /// GDPR basis: Article 6(1)(a) for storage; Article 6(1)(d) vital interests for auto-capture during SOS.
    /// </summary>
    public bool EvidenceStorage { get; set; }

    /// <summary>
    /// Consent to anonymized analytics (app usage patterns, feature adoption, crash reports).
    /// No PII is included in analytics — only aggregated, de-identified metrics.
    /// GDPR basis: Article 6(1)(a). Can be withdrawn without affecting core functionality.
    /// </summary>
    public bool Analytics { get; set; }

    /// <summary>
    /// Consent to share emergency contact information with responders during an active SOS.
    /// Contacts are only revealed to acknowledged responders for the duration of the incident.
    /// GDPR basis: Article 6(1)(d) vital interests + Article 6(1)(a) explicit consent.
    /// </summary>
    public bool EmergencyContactSharing { get; set; }

    /// <summary>
    /// Consent to process biometric data (voice patterns for phrase detection,
    /// accelerometer patterns for fall detection). Processed on-device where possible.
    /// GDPR basis: Article 9(2)(a) explicit consent for special category data (biometric).
    /// </summary>
    public bool BiometricProcessing { get; set; }

    /// <summary>
    /// Consent to share medical/health information (infirmities, medications, allergies)
    /// with first responders during an active emergency.
    /// GDPR basis: Article 9(2)(c) vital interests where data subject is incapable of giving consent.
    /// </summary>
    public bool MedicalInfoSharing { get; set; }

    /// <summary>
    /// Consent to receive push notifications (SOS alerts, check-in requests, system updates).
    /// Separate from OS-level notification permission — this is the GDPR consent layer.
    /// </summary>
    public bool PushNotifications { get; set; }

    /// <summary>
    /// Consent to participate in the volunteer responder network.
    /// When true, the user's location may be shared with SOS initiators during dispatch.
    /// GDPR basis: Article 6(1)(a) explicit consent for location sharing with other users.
    /// </summary>
    public bool VolunteerParticipation { get; set; }

    // ── Metadata ──────────────────────────────────────────────────

    /// <summary>Version of the privacy policy the user consented under.</summary>
    public string PrivacyPolicyVersion { get; set; } = "1.0";

    /// <summary>When this consent record was last updated (UTC).</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>When consent was first granted (UTC). Null if never consented.</summary>
    public DateTime? InitialConsentAt { get; set; }

    /// <summary>
    /// IP address from which consent was last updated (for GDPR demonstrability).
    /// Stored hashed in production; plaintext only in dev/mock.
    /// </summary>
    public string? ConsentSourceIp { get; set; }

    /// <summary>
    /// User-agent string from which consent was last updated (device/platform identification).
    /// </summary>
    public string? ConsentSourceDevice { get; set; }
}
