// MockDataExportAdapter — dev/test adapter for IDataExportPort.
//
// Returns realistic synthetic data for every GDPR data category so the
// GdprController, mobile GDPR screens, and integration tests can run
// without any real infrastructure.
//
// The mock export generates a JSON structure that matches the production
// export schema exactly — so UI rendering code tested against mock data
// will work unchanged against real exports.
//
// Example:
//   var adapter = new MockDataExportAdapter(logger);
//   var result = await adapter.ExportUserDataAsync("user-123");
//   // result.JsonPayload contains ~2KB of realistic JSON
//   // result.Categories contains all 15 GdprDataCategory values
//
// WAL: This adapter stores consent preferences in-memory (ConcurrentDictionary).
//      State is lost on restart — acceptable for dev/test only.
//
// Regulatory: This adapter exists to enable GDPR compliance testing without
//             requiring production data. Real adapters must collect from actual stores.

using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Adapters.Mock;

/// <summary>
/// Mock implementation of IDataExportPort for development and testing.
/// Generates realistic synthetic exports and stores consent in-memory.
/// </summary>
public class MockDataExportAdapter : IDataExportPort
{
    private readonly ILogger<MockDataExportAdapter> _logger;
    private readonly ConcurrentDictionary<string, ConsentPreferences> _consentStore = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public MockDataExportAdapter(ILogger<MockDataExportAdapter> logger)
    {
        _logger = logger;
    }

    // ── Data Export (GDPR Article 20) ────────────────────────────

    public async Task<DataExportResult> ExportUserDataAsync(
        string userId,
        IReadOnlyList<GdprDataCategory>? categories = null,
        CancellationToken ct = default)
    {
        _logger.LogInformation("[MOCK] GDPR data export requested for user {UserId}", userId);

        // Simulate processing time (real adapter would query multiple databases)
        await Task.Delay(500, ct);

        var requestedCategories = categories ?? Enum.GetValues<GdprDataCategory>().ToList();
        var exportData = BuildExportPayload(userId, requestedCategories);
        var json = JsonSerializer.Serialize(exportData, JsonOptions);

        _logger.LogInformation(
            "[MOCK] GDPR export complete for user {UserId}: {Size} bytes, {Categories} categories",
            userId, json.Length, requestedCategories.Count);

        return new DataExportResult
        {
            UserId = userId,
            JsonPayload = json,
            SizeBytes = json.Length,
            Categories = requestedCategories,
            GeneratedAt = DateTime.UtcNow
        };
    }

    // ── Account Erasure (GDPR Article 17) ────────────────────────

    public async Task<AccountErasureResult> EraseAccountAsync(
        string userId,
        CancellationToken ct = default)
    {
        _logger.LogWarning("[MOCK] GDPR account erasure requested for user {UserId}", userId);

        // Simulate multi-store deletion
        await Task.Delay(1000, ct);

        // Remove consent record
        _consentStore.TryRemove(userId, out _);

        var allCategories = Enum.GetValues<GdprDataCategory>().ToList();

        _logger.LogWarning(
            "[MOCK] GDPR account erasure complete for user {UserId}: {Count} categories purged",
            userId, allCategories.Count);

        return new AccountErasureResult
        {
            UserId = userId,
            Success = true,
            PurgedCategories = allCategories,
            FailedCategories = Array.Empty<GdprDataCategory>(),
            DeletionAuditEntryId = Guid.NewGuid().ToString(),
            ErasedAt = DateTime.UtcNow
        };
    }

    // ── Consent Management (GDPR Article 7) ──────────────────────

    public Task<ConsentPreferences?> GetConsentAsync(
        string userId,
        CancellationToken ct = default)
    {
        _consentStore.TryGetValue(userId, out var prefs);
        return Task.FromResult(prefs);
    }

    public Task<ConsentPreferences> UpdateConsentAsync(
        ConsentPreferences preferences,
        CancellationToken ct = default)
    {
        preferences.UpdatedAt = DateTime.UtcNow;
        if (preferences.InitialConsentAt is null)
            preferences.InitialConsentAt = DateTime.UtcNow;

        _consentStore.AddOrUpdate(preferences.UserId, preferences, (_, _) => preferences);

        _logger.LogInformation(
            "[MOCK] Consent updated for user {UserId}: Location={Location}, Evidence={Evidence}, " +
            "Analytics={Analytics}, EmergencyContacts={Contacts}, Biometric={Bio}, Medical={Medical}",
            preferences.UserId, preferences.LocationTracking, preferences.EvidenceStorage,
            preferences.Analytics, preferences.EmergencyContactSharing,
            preferences.BiometricProcessing, preferences.MedicalInfoSharing);

        return Task.FromResult(preferences);
    }

    // ── Privacy Policy ────────────────────────────────────────────

    public Task<PrivacyPolicyInfo> GetPrivacyPolicyAsync(CancellationToken ct = default)
    {
        return Task.FromResult(new PrivacyPolicyInfo
        {
            Version = "1.0.0",
            EffectiveDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            PolicyUrl = "https://thewatch.app/privacy",
            PolicyText = BuildPrivacyPolicyText(),
            ChangesSummary = null // First version — no changes to summarize
        });
    }

    // ── Private: Export Payload Builder ───────────────────────────

    private static Dictionary<string, object> BuildExportPayload(
        string userId, IReadOnlyList<GdprDataCategory> categories)
    {
        var payload = new Dictionary<string, object>
        {
            ["exportMetadata"] = new
            {
                exportId = Guid.NewGuid().ToString(),
                userId,
                generatedAt = DateTime.UtcNow,
                format = "TheWatch GDPR Export v1.0",
                categories = categories.Select(c => c.ToString()).ToList(),
                regulatoryBasis = "GDPR Article 20 (Data Portability)"
            }
        };

        var categorySet = new HashSet<GdprDataCategory>(categories);

        if (categorySet.Contains(GdprDataCategory.Profile))
        {
            payload["profile"] = new
            {
                userId,
                email = $"{userId}@example.com",
                firstName = "Alex",
                lastName = "Rivera",
                phoneNumber = "+1-555-0100",
                photoUrl = (string?)null,
                createdAt = DateTime.UtcNow.AddMonths(-6),
                lastSignIn = DateTime.UtcNow.AddHours(-2)
            };
        }

        if (categorySet.Contains(GdprDataCategory.EmergencyContacts))
        {
            payload["emergencyContacts"] = new[]
            {
                new { name = "Jordan Rivera", relationship = "Spouse", phone = "+1-555-0456", priority = 1 },
                new { name = "Morgan Rivera", relationship = "Parent", phone = "+1-555-0789", priority = 2 }
            };
        }

        if (categorySet.Contains(GdprDataCategory.IncidentHistory))
        {
            payload["incidentHistory"] = new[]
            {
                new
                {
                    requestId = "req-001",
                    type = "CheckIn",
                    triggerSource = "MANUAL_BUTTON",
                    status = "Resolved",
                    createdAt = DateTime.UtcNow.AddDays(-30),
                    resolvedAt = DateTime.UtcNow.AddDays(-30).AddMinutes(8),
                    respondersAcknowledged = 3,
                    latitude = 30.2672,
                    longitude = -97.7431
                },
                new
                {
                    requestId = "req-002",
                    type = "Neighborhood",
                    triggerSource = "PHRASE",
                    status = "Cancelled",
                    createdAt = DateTime.UtcNow.AddDays(-15),
                    resolvedAt = DateTime.UtcNow.AddDays(-15).AddMinutes(2),
                    respondersAcknowledged = 0,
                    latitude = 30.2650,
                    longitude = -97.7400
                }
            };
        }

        if (categorySet.Contains(GdprDataCategory.LocationLogs))
        {
            payload["locationLogs"] = new
            {
                summary = "Location data is processed on-device. Server retains only incident-time locations.",
                incidentLocations = new[]
                {
                    new { timestamp = DateTime.UtcNow.AddDays(-30), latitude = 30.2672, longitude = -97.7431, accuracy = 15.0 },
                    new { timestamp = DateTime.UtcNow.AddDays(-15), latitude = 30.2650, longitude = -97.7400, accuracy = 10.0 }
                }
            };
        }

        if (categorySet.Contains(GdprDataCategory.ConsentRecords))
        {
            payload["consentRecords"] = new[]
            {
                new
                {
                    action = "ConsentGranted",
                    timestamp = DateTime.UtcNow.AddMonths(-6),
                    categories = new[] { "LocationTracking", "EvidenceStorage", "EmergencyContactSharing", "PushNotifications" },
                    policyVersion = "1.0.0"
                }
            };
        }

        if (categorySet.Contains(GdprDataCategory.EulaAcceptanceHistory))
        {
            payload["eulaAcceptanceHistory"] = new[]
            {
                new { version = "1.0", acceptedAt = DateTime.UtcNow.AddMonths(-6), device = "iPhone 15 Pro" }
            };
        }

        if (categorySet.Contains(GdprDataCategory.VolunteerData))
        {
            payload["volunteerData"] = new
            {
                isVolunteer = true,
                hasCar = true,
                isOver18 = true,
                certifications = new[] { "CPR", "FIRST_AID" },
                totalResponses = 12,
                enrolledAt = DateTime.UtcNow.AddMonths(-5),
                maxResponseRadiusMeters = 5000.0,
                availability = "Always"
            };
        }

        if (categorySet.Contains(GdprDataCategory.DeviceRegistrations))
        {
            payload["deviceRegistrations"] = new[]
            {
                new
                {
                    deviceId = "dev-001",
                    platform = "iOS",
                    model = "iPhone 15 Pro",
                    osVersion = "18.2",
                    appVersion = "1.0.0",
                    registeredAt = DateTime.UtcNow.AddMonths(-6),
                    pushToken = "[REDACTED]"
                }
            };
        }

        if (categorySet.Contains(GdprDataCategory.NotificationPreferences))
        {
            payload["notificationPreferences"] = new
            {
                pushEnabled = true,
                smsEnabled = true,
                emailEnabled = false,
                quietHoursStart = "22:00",
                quietHoursEnd = "07:00"
            };
        }

        if (categorySet.Contains(GdprDataCategory.BiometricEnrollmentMetadata))
        {
            payload["biometricEnrollmentMetadata"] = new
            {
                phraseDetectionEnabled = true,
                enrolledPhraseCount = 2,
                fallDetectionEnabled = false,
                note = "Biometric templates are stored on-device only and are not included in server exports."
            };
        }

        if (categorySet.Contains(GdprDataCategory.PhraseDetectionConfig))
        {
            payload["phraseDetectionConfig"] = new
            {
                enabled = true,
                activationMode = "always",
                phraseCount = 2,
                note = "Actual phrases are stored on-device only for security. Server stores only configuration metadata."
            };
        }

        if (categorySet.Contains(GdprDataCategory.SosConfiguration))
        {
            payload["sosConfiguration"] = new
            {
                defaultScope = "CheckIn",
                autoCallEnabled = true,
                autoRecordEnabled = true,
                trustedContactsCount = 3,
                silentDuressEnabled = false
            };
        }

        if (categorySet.Contains(GdprDataCategory.ResponderInteractions))
        {
            payload["responderInteractions"] = new[]
            {
                new
                {
                    requestId = "req-001",
                    role = "Responder",
                    acknowledgedAt = DateTime.UtcNow.AddDays(-30),
                    arrivedAt = DateTime.UtcNow.AddDays(-30).AddMinutes(6),
                    messagesSent = 2
                }
            };
        }

        if (categorySet.Contains(GdprDataCategory.EvidenceMetadata))
        {
            payload["evidenceMetadata"] = new[]
            {
                new
                {
                    evidenceId = "ev-001",
                    requestId = "req-001",
                    type = "Photo",
                    capturedAt = DateTime.UtcNow.AddDays(-30),
                    sizeBytes = 2_450_000L,
                    sha256 = "a1b2c3d4e5f6...",
                    retentionExpiresAt = DateTime.UtcNow.AddDays(60),
                    note = "Binary content not included in export. Request via evidence download endpoint."
                }
            };
        }

        if (categorySet.Contains(GdprDataCategory.AuditTrail))
        {
            payload["auditTrail"] = new
            {
                summary = "Recent audit entries for this user (last 100).",
                entries = new[]
                {
                    new
                    {
                        action = "Login",
                        timestamp = DateTime.UtcNow.AddHours(-2),
                        sourceSystem = "MobileApp.iOS",
                        outcome = "Success"
                    },
                    new
                    {
                        action = "SOSTrigger",
                        timestamp = DateTime.UtcNow.AddDays(-30),
                        sourceSystem = "MobileApp.iOS",
                        outcome = "Success"
                    },
                    new
                    {
                        action = "ConsentUpdated",
                        timestamp = DateTime.UtcNow.AddMonths(-3),
                        sourceSystem = "Dashboard.Api",
                        outcome = "Success"
                    }
                }
            };
        }

        return payload;
    }

    // ── Private: Privacy Policy Text ─────────────────────────────

    private static string BuildPrivacyPolicyText() => """
        TheWatch Privacy Policy
        Version 1.0.0 — Effective January 1, 2026

        1. DATA WE COLLECT
        TheWatch collects the following categories of personal data:
        - Profile information (name, email, phone number)
        - Location data (GPS coordinates during active monitoring and incidents)
        - Emergency contacts (names, phone numbers, relationships)
        - Biometric data (voice patterns for phrase detection, processed on-device)
        - Evidence (photos, video, audio captured during incidents)
        - Device information (model, OS version, app version)
        - Usage analytics (anonymized, opt-in only)
        - Medical information (infirmities, medications — shared with first responders only during emergencies)

        2. HOW WE USE YOUR DATA
        - Life-safety: Dispatching volunteer responders and emergency services during SOS events
        - Volunteer coordination: Matching nearby responders to incidents based on location and capabilities
        - Evidence preservation: Securely storing incident evidence for legal and safety purposes
        - Service improvement: Anonymized analytics to improve response times and user experience (opt-in)

        3. DATA SHARING
        - With acknowledged responders: Your location and emergency contacts during active incidents only
        - With emergency services: Your location and medical info when 911 escalation is triggered
        - We do NOT sell personal data
        - We do NOT share data with advertisers

        4. DATA RETENTION
        - Profile data: Retained while account is active
        - Incident data: 90 days after resolution (configurable)
        - Evidence: 90 days after incident resolution
        - Audit trail: 7 years (legal/compliance requirement)
        - Location data: Processed on-device; server retains only incident-time locations

        5. YOUR RIGHTS (GDPR Articles 15-22, CCPA, LGPD)
        - Right of access: Export all your data (Article 15/20)
        - Right to erasure: Delete your account and all data (Article 17)
        - Right to withdraw consent: Update consent preferences at any time (Article 7)
        - Right to data portability: Download your data in machine-readable JSON format (Article 20)
        - Right to object: Opt out of analytics and non-essential processing (Article 21)

        6. SECURITY
        - All data encrypted in transit (TLS 1.3) and at rest (AES-256)
        - Audit trail is Merkle-chained for tamper evidence
        - Evidence chain of custody maintained with SHA-256 hashes
        - Multi-factor authentication available
        - Regular security audits per ISO 27001

        7. CONTACT
        Data Protection Officer: dpo@thewatch.app
        Support: privacy@thewatch.app
        """;
}
