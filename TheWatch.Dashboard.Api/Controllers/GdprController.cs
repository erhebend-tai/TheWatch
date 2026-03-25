// GdprController — REST endpoints for GDPR compliance operations.
//
// Implements the four core GDPR data subject rights as API endpoints:
//   - Right of access / data portability (Article 15/20): GET /api/gdpr/export/{userId}
//   - Right to erasure (Article 17): DELETE /api/gdpr/account/{userId}
//   - Consent management (Article 7): GET/PUT /api/gdpr/consent/{userId}
//   - Privacy policy transparency (Article 13): GET /api/gdpr/privacy-policy
//
// Security model:
//   - Users can only export/delete/view consent for their OWN account (uid claim must match)
//   - Admin role can export/delete on behalf of any user (for support requests)
//   - Privacy policy is public (AllowAnonymous)
//   - All operations are audit-trailed
//
// Cross-platform alignment:
//   - Android: DataExportPort.kt, AccountDeletionPort.kt (mobile clients call these endpoints)
//   - iOS: DataExportService.swift, AccountDeletionService.swift (mobile clients call these endpoints)
//   - The JSON export format matches the mobile export format for consistency
//
// Example — mobile client exports user data:
//   GET /api/gdpr/export/user-123
//   Authorization: Bearer <firebase-id-token>
//   → 200 OK with Content-Disposition: attachment; filename="thewatch-export-user-123.json"
//
// Example — mobile client requests account deletion:
//   DELETE /api/gdpr/account/user-123
//   Authorization: Bearer <firebase-id-token>
//   → 200 OK { "success": true, "purgedCategories": [...], "deletionAuditEntryId": "..." }
//
// WAL: All heavy lifting delegated to IDataExportPort. Controller is thin — auth + delegation.
//      Every GDPR operation generates an AuditEntry for compliance demonstrability.

using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Domain.Ports;
using TheWatch.Shared.Enums;

namespace TheWatch.Dashboard.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GdprController : ControllerBase
{
    private readonly IDataExportPort _dataExportPort;
    private readonly IAuditTrail _auditTrail;
    private readonly IAuthPort _authPort;
    private readonly ILogger<GdprController> _logger;

    public GdprController(
        IDataExportPort dataExportPort,
        IAuditTrail auditTrail,
        IAuthPort authPort,
        ILogger<GdprController> logger)
    {
        _dataExportPort = dataExportPort;
        _auditTrail = auditTrail;
        _authPort = authPort;
        _logger = logger;
    }

    private string? GetUid() => User.FindFirst("uid")?.Value;

    private bool IsAdmin() => User.IsInRole("admin");

    /// <summary>
    /// Verify that the requesting user is authorized to access the target user's data.
    /// Users can only access their own data; admins can access any user's data.
    /// </summary>
    private bool IsAuthorized(string targetUserId)
    {
        var uid = GetUid();
        if (string.IsNullOrEmpty(uid)) return false;
        return uid == targetUserId || IsAdmin();
    }

    // ═══════════════════════════════════════════════════════════════
    // GDPR Article 20 — Data Portability (Export)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Export ALL user data as a downloadable JSON file.
    /// GDPR Article 20: data portability in a structured, machine-readable format.
    ///
    /// The export includes: profile, emergency contacts, incident history, evidence metadata,
    /// participation preferences, consent records, audit trail entries, volunteer data,
    /// device registrations, notification preferences, and more.
    ///
    /// Returns the JSON as a file download (Content-Disposition: attachment).
    /// </summary>
    [HttpGet("export/{userId}")]
    public async Task<IActionResult> ExportUserData(
        [Required] string userId,
        CancellationToken ct)
    {
        if (!IsAuthorized(userId))
            return Forbid();

        _logger.LogInformation("GDPR data export requested for user {UserId} by {RequestingUid}", userId, GetUid());

        // Audit the export request
        await _auditTrail.AppendAsync(new AuditEntry
        {
            UserId = userId,
            Action = AuditAction.DataExportRequested,
            EntityType = "GdprExport",
            EntityId = userId,
            SourceSystem = "Dashboard.Api",
            SourceComponent = "GdprController",
            Severity = AuditSeverity.Warning,
            DataClassification = DataClassification.HighlyConfidential,
            Outcome = AuditOutcome.Success,
            Reason = $"GDPR Article 20 data portability export requested by {GetUid()}",
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString()
        }, ct);

        try
        {
            var result = await _dataExportPort.ExportUserDataAsync(userId, ct: ct);

            // Return as downloadable JSON file
            var bytes = Encoding.UTF8.GetBytes(result.JsonPayload);
            var fileName = $"thewatch-export-{userId}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json";

            return File(bytes, "application/json", fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GDPR data export failed for user {UserId}", userId);
            return StatusCode(500, new { error = "Data export failed", detail = ex.Message });
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // GDPR Article 17 — Right to Erasure (Account Deletion)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Full account erasure: deletes the auth account, purges ALL user data from every
    /// storage system, and retains only a single audit entry noting the deletion occurred
    /// (retained per legal requirement — GDPR Article 17(3)(e) for legal claims).
    ///
    /// This is IRREVERSIBLE. The mobile apps should show a multi-step confirmation flow
    /// (re-authenticate, type confirmation, acknowledge data loss, offer export) before
    /// calling this endpoint.
    /// </summary>
    [HttpDelete("account/{userId}")]
    public async Task<IActionResult> EraseAccount(
        [Required] string userId,
        CancellationToken ct)
    {
        if (!IsAuthorized(userId))
            return Forbid();

        _logger.LogWarning("GDPR account erasure requested for user {UserId} by {RequestingUid}", userId, GetUid());

        try
        {
            // Step 1: Purge all data from storage systems via the data export port
            var erasureResult = await _dataExportPort.EraseAccountAsync(userId, ct);

            if (!erasureResult.Success)
            {
                _logger.LogError("GDPR account erasure FAILED for user {UserId}: {Error}",
                    userId, erasureResult.ErrorMessage);

                return StatusCode(500, new
                {
                    error = "Account erasure failed",
                    detail = erasureResult.ErrorMessage,
                    purgedCategories = erasureResult.PurgedCategories.Select(c => c.ToString()),
                    failedCategories = erasureResult.FailedCategories.Select(c => c.ToString())
                });
            }

            // Step 2: Delete the auth account (Firebase/Azure AD/etc.)
            var authDeleted = await _authPort.DeleteAccountAsync(userId, ct);
            if (!authDeleted)
            {
                _logger.LogError("GDPR: Data purged but auth account deletion FAILED for user {UserId}", userId);
                // Data is already purged — log the partial failure but don't roll back
            }

            // Step 3: Log the deletion in the audit trail (this record is RETAINED per legal requirement)
            await _auditTrail.AppendAsync(new AuditEntry
            {
                UserId = userId,
                Action = AuditAction.DataDeletionCompleted,
                EntityType = "GdprErasure",
                EntityId = userId,
                SourceSystem = "Dashboard.Api",
                SourceComponent = "GdprController",
                Severity = AuditSeverity.Critical,
                DataClassification = DataClassification.HighlyConfidential,
                Outcome = AuditOutcome.Success,
                Reason = "GDPR Article 17 right to erasure — full account deletion completed",
                NewValue = System.Text.Json.JsonSerializer.Serialize(new
                {
                    erasureResult.PurgedCategories,
                    authAccountDeleted = authDeleted,
                    erasureResult.ErasedAt
                }),
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString()
            }, ct);

            _logger.LogWarning(
                "GDPR account erasure COMPLETE for user {UserId}: {Count} categories purged, auth={AuthDeleted}",
                userId, erasureResult.PurgedCategories.Count, authDeleted);

            return Ok(new
            {
                success = true,
                userId,
                purgedCategories = erasureResult.PurgedCategories.Select(c => c.ToString()),
                authAccountDeleted = authDeleted,
                deletionAuditEntryId = erasureResult.DeletionAuditEntryId,
                erasedAt = erasureResult.ErasedAt,
                note = "All personal data has been erased. A single audit record has been retained per legal requirement (GDPR Art.17(3)(e))."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GDPR account erasure EXCEPTION for user {UserId}", userId);

            // Audit the failure
            await _auditTrail.AppendAsync(new AuditEntry
            {
                UserId = userId,
                Action = AuditAction.DataDeletionRequested,
                EntityType = "GdprErasure",
                EntityId = userId,
                SourceSystem = "Dashboard.Api",
                SourceComponent = "GdprController",
                Severity = AuditSeverity.Critical,
                DataClassification = DataClassification.HighlyConfidential,
                Outcome = AuditOutcome.Failure,
                ErrorMessage = ex.Message,
                Reason = "GDPR Article 17 erasure request failed",
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString()
            }, ct);

            return StatusCode(500, new { error = "Account erasure failed", detail = ex.Message });
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // GDPR Article 7 — Consent Management
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Get the user's current consent status — what data processing activities
    /// they have consented to and when.
    /// </summary>
    [HttpGet("consent/{userId}")]
    public async Task<IActionResult> GetConsent(
        [Required] string userId,
        CancellationToken ct)
    {
        if (!IsAuthorized(userId))
            return Forbid();

        var consent = await _dataExportPort.GetConsentAsync(userId, ct);

        if (consent is null)
        {
            // New user — no consent record yet. Return defaults (all false).
            return Ok(new ConsentPreferences { UserId = userId });
        }

        return Ok(consent);
    }

    /// <summary>
    /// Update the user's consent preferences. Each field controls a specific
    /// data processing activity. Setting a field to false withdraws consent
    /// for that activity (GDPR Article 7(3): right to withdraw consent).
    ///
    /// Changes are audit-trailed with before/after values for GDPR demonstrability.
    /// </summary>
    [HttpPut("consent/{userId}")]
    public async Task<IActionResult> UpdateConsent(
        [Required] string userId,
        [FromBody] UpdateConsentRequest request,
        CancellationToken ct)
    {
        if (!IsAuthorized(userId))
            return Forbid();

        _logger.LogInformation("Consent update for user {UserId}", userId);

        // Get current consent for audit trail (before/after)
        var currentConsent = await _dataExportPort.GetConsentAsync(userId, ct);

        var newConsent = new ConsentPreferences
        {
            UserId = userId,
            LocationTracking = request.LocationTracking,
            EvidenceStorage = request.EvidenceStorage,
            Analytics = request.Analytics,
            EmergencyContactSharing = request.EmergencyContactSharing,
            BiometricProcessing = request.BiometricProcessing,
            MedicalInfoSharing = request.MedicalInfoSharing,
            PushNotifications = request.PushNotifications,
            VolunteerParticipation = request.VolunteerParticipation,
            PrivacyPolicyVersion = request.PrivacyPolicyVersion ?? "1.0.0",
            ConsentSourceIp = HttpContext.Connection.RemoteIpAddress?.ToString(),
            ConsentSourceDevice = Request.Headers.UserAgent.ToString(),
            InitialConsentAt = currentConsent?.InitialConsentAt
        };

        var updated = await _dataExportPort.UpdateConsentAsync(newConsent, ct);

        // Audit trail with before/after for GDPR demonstrability
        await _auditTrail.AppendAsync(new AuditEntry
        {
            UserId = userId,
            Action = AuditAction.ConsentUpdated,
            EntityType = "ConsentPreferences",
            EntityId = userId,
            SourceSystem = "Dashboard.Api",
            SourceComponent = "GdprController",
            Severity = AuditSeverity.Notice,
            DataClassification = DataClassification.Confidential,
            Outcome = AuditOutcome.Success,
            OldValue = currentConsent is not null
                ? System.Text.Json.JsonSerializer.Serialize(currentConsent)
                : null,
            NewValue = System.Text.Json.JsonSerializer.Serialize(updated),
            Reason = "User updated consent preferences",
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString()
        }, ct);

        return Ok(updated);
    }

    // ═══════════════════════════════════════════════════════════════
    // GDPR Article 13 — Privacy Policy Transparency
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Get the current privacy policy version and full text.
    /// Public endpoint — no authentication required.
    /// Mobile apps check this on startup to determine if re-consent is needed
    /// (when the policy version has changed since the user last consented).
    /// </summary>
    [HttpGet("privacy-policy")]
    [AllowAnonymous]
    public async Task<IActionResult> GetPrivacyPolicy(CancellationToken ct)
    {
        var policy = await _dataExportPort.GetPrivacyPolicyAsync(ct);
        return Ok(policy);
    }
}

// ═══════════════════════════════════════════════════════════════
// Request DTOs
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Request body for updating consent preferences.
/// All fields are required — the client must send the complete consent state.
/// No pre-checked boxes: all default to false per GDPR Article 7(2).
/// </summary>
public record UpdateConsentRequest
{
    /// <summary>Consent to continuous location tracking.</summary>
    public bool LocationTracking { get; init; }

    /// <summary>Consent to store evidence captured during incidents.</summary>
    public bool EvidenceStorage { get; init; }

    /// <summary>Consent to anonymized analytics.</summary>
    public bool Analytics { get; init; }

    /// <summary>Consent to share emergency contacts with responders during SOS.</summary>
    public bool EmergencyContactSharing { get; init; }

    /// <summary>Consent to process biometric data (voice, accelerometer patterns).</summary>
    public bool BiometricProcessing { get; init; }

    /// <summary>Consent to share medical info with first responders during emergencies.</summary>
    public bool MedicalInfoSharing { get; init; }

    /// <summary>Consent to receive push notifications.</summary>
    public bool PushNotifications { get; init; }

    /// <summary>Consent to participate in volunteer responder network.</summary>
    public bool VolunteerParticipation { get; init; }

    /// <summary>Privacy policy version the user is consenting under (e.g., "1.0.0").</summary>
    public string? PrivacyPolicyVersion { get; init; }
}
