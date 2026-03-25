// IDataExportPort — domain port for GDPR Article 20 data portability.
//
// Orchestrates data collection from ALL storage ports to produce a comprehensive
// JSON export of everything TheWatch stores about a user. This is the server-side
// counterpart to the mobile DataExportPort (Android) and DataExportServiceProtocol (iOS).
//
// Architecture:
//   GdprController → IDataExportPort → (IAuditTrail, IStorageService, IEvidencePort,
//                                        IIncidentHistoryPort, IParticipationPort,
//                                        IVolunteeringPort, IResponseRequestPort)
//
// The export includes:
//   - Profile data (name, email, phone, photo URL)
//   - Emergency contacts
//   - Incident history (all SOS requests, responses, outcomes)
//   - Evidence metadata (NOT binary files — just metadata: filename, timestamp, hash)
//   - Participation preferences (volunteer settings, availability)
//   - Consent records (what the user consented to and when)
//   - Audit trail entries (all actions by or affecting this user)
//   - Volunteering data (enrollment, stats, certifications)
//   - Device registrations
//   - Location history summary (aggregate, not raw GPS — raw would be too large)
//
// The export MUST be machine-readable (JSON) per GDPR Article 20(1).
// The export MUST include ALL personal data per GDPR Article 15(3).
//
// Example:
//   var port = serviceProvider.GetRequiredService<IDataExportPort>();
//   var json = await port.ExportUserDataAsync("user-123", ct);
//   // Returns a comprehensive JSON string with all user data
//
// WAL: Every export triggers AuditAction.DataExportRequested in the audit trail.
//      The export itself is logged but the exported data is NOT stored in the audit trail
//      (that would duplicate PII).
//
// Regulatory: GDPR Art.15 (right of access), Art.20 (data portability),
//             CCPA 1798.100 (right to know), LGPD Art.18(V) (data portability)

using TheWatch.Shared.Domain.Models;

namespace TheWatch.Shared.Domain.Ports;

/// <summary>
/// Data categories available for export. Maps to the Android DataCategory enum
/// and iOS GDPRDataCategory enum for cross-platform consistency.
/// </summary>
public enum GdprDataCategory
{
    Profile,
    EmergencyContacts,
    IncidentHistory,
    LocationLogs,
    ConsentRecords,
    EulaAcceptanceHistory,
    VolunteerData,
    DeviceRegistrations,
    NotificationPreferences,
    BiometricEnrollmentMetadata,
    PhraseDetectionConfig,
    SosConfiguration,
    ResponderInteractions,
    EvidenceMetadata,
    AuditTrail
}

/// <summary>
/// Result of a data export operation.
/// </summary>
public record DataExportResult
{
    /// <summary>Unique export ID for audit tracking.</summary>
    public string ExportId { get; init; } = Guid.NewGuid().ToString();

    /// <summary>User whose data was exported.</summary>
    public string UserId { get; init; } = string.Empty;

    /// <summary>The exported data as a JSON string.</summary>
    public string JsonPayload { get; init; } = string.Empty;

    /// <summary>Size of the export in bytes.</summary>
    public long SizeBytes { get; init; }

    /// <summary>Categories included in this export.</summary>
    public IReadOnlyList<GdprDataCategory> Categories { get; init; } = Array.Empty<GdprDataCategory>();

    /// <summary>When the export was generated (UTC).</summary>
    public DateTime GeneratedAt { get; init; } = DateTime.UtcNow;

    /// <summary>Format identifier for cross-platform compatibility.</summary>
    public string Format { get; init; } = "TheWatch GDPR Export v1.0";
}

/// <summary>
/// Result of a full account erasure operation (GDPR Article 17).
/// </summary>
public record AccountErasureResult
{
    /// <summary>User whose data was erased.</summary>
    public string UserId { get; init; } = string.Empty;

    /// <summary>Whether the erasure was fully successful.</summary>
    public bool Success { get; init; }

    /// <summary>Data categories that were successfully purged.</summary>
    public IReadOnlyList<GdprDataCategory> PurgedCategories { get; init; } = Array.Empty<GdprDataCategory>();

    /// <summary>Data categories that failed to purge (partial failure).</summary>
    public IReadOnlyList<GdprDataCategory> FailedCategories { get; init; } = Array.Empty<GdprDataCategory>();

    /// <summary>The audit entry ID for the deletion record (retained per legal requirement).</summary>
    public string? DeletionAuditEntryId { get; init; }

    /// <summary>When the erasure was performed (UTC).</summary>
    public DateTime ErasedAt { get; init; } = DateTime.UtcNow;

    /// <summary>Error message if erasure failed.</summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Port for GDPR data operations: export (Art.20), erasure (Art.17), consent (Art.7).
/// Adapters orchestrate data collection from all storage ports.
/// </summary>
public interface IDataExportPort
{
    // ── Data Export (GDPR Article 20) ────────────────────────────

    /// <summary>
    /// Export ALL user data as a structured JSON payload.
    /// Collects from every storage port and assembles a comprehensive export.
    /// </summary>
    /// <param name="userId">The user whose data to export.</param>
    /// <param name="categories">Which categories to include (default: all).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>DataExportResult containing the JSON payload.</returns>
    Task<DataExportResult> ExportUserDataAsync(
        string userId,
        IReadOnlyList<GdprDataCategory>? categories = null,
        CancellationToken ct = default);

    // ── Account Erasure (GDPR Article 17) ────────────────────────

    /// <summary>
    /// Purge ALL user data from every storage system. Deletes auth account,
    /// clears all data stores, and retains only a single audit entry noting
    /// that deletion occurred (legal requirement for demonstrating compliance).
    /// </summary>
    /// <param name="userId">The user whose data to erase.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>AccountErasureResult with details of what was purged.</returns>
    Task<AccountErasureResult> EraseAccountAsync(
        string userId,
        CancellationToken ct = default);

    // ── Consent Management (GDPR Article 7) ──────────────────────

    /// <summary>
    /// Get the user's current consent preferences.
    /// Returns null if the user has never set preferences (new account).
    /// </summary>
    Task<ConsentPreferences?> GetConsentAsync(
        string userId,
        CancellationToken ct = default);

    /// <summary>
    /// Update the user's consent preferences. Creates an audit trail entry
    /// with before/after values for GDPR demonstrability.
    /// </summary>
    Task<ConsentPreferences> UpdateConsentAsync(
        ConsentPreferences preferences,
        CancellationToken ct = default);

    // ── Privacy Policy ────────────────────────────────────────────

    /// <summary>
    /// Get the current privacy policy version and text.
    /// </summary>
    Task<PrivacyPolicyInfo> GetPrivacyPolicyAsync(
        CancellationToken ct = default);
}

/// <summary>
/// Privacy policy information returned by the GDPR controller.
/// </summary>
public record PrivacyPolicyInfo
{
    /// <summary>Semantic version of the current privacy policy (e.g., "1.0.0").</summary>
    public string Version { get; init; } = "1.0.0";

    /// <summary>When this version became effective (UTC).</summary>
    public DateTime EffectiveDate { get; init; } = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>When the previous version was superseded (null if first version).</summary>
    public DateTime? PreviousVersionDate { get; init; }

    /// <summary>Full text of the privacy policy.</summary>
    public string PolicyText { get; init; } = string.Empty;

    /// <summary>URL where the full policy can be viewed in a browser.</summary>
    public string PolicyUrl { get; init; } = "https://thewatch.app/privacy";

    /// <summary>
    /// Summary of changes from the previous version (null if first version).
    /// Displayed to users when they need to re-consent after a policy update.
    /// </summary>
    public string? ChangesSummary { get; init; }
}
