// AuditAction — enumerates every auditable operation in TheWatch end-to-end.
//
// Organized by domain boundary so the audit trail is queryable by subsystem.
// Every state transition in every subsystem MUST have a corresponding action here.
//
// ISO 27001 A.12.4 (Logging and Monitoring) requires:
//   - User activities, exceptions, faults, and information security events
//   - Audit logs recording user activities, access, and system changes
//   - Protection of log information against tampering (Merkle chain handles this)
//   - Clock synchronization (all timestamps UTC)
//
// Example: AuditAction.SOSTrigger when user utters their emergency phrase.
// Example: AuditAction.Emergency911Initiated when auto-911 fires.

namespace TheWatch.Shared.Enums;

public enum AuditAction
{
    // ── Generic CRUD (entity-level) ─────────────────────────────
    Create = 0,
    Read = 1,
    Update = 2,
    Delete = 3,

    // ── Authentication & Authorization ──────────────────────────
    Login = 4,
    Logout = 5,
    LoginFailed = 100,
    TokenRefreshed = 101,
    TwoFactorVerified = 102,
    TwoFactorFailed = 103,
    PasswordChanged = 104,
    PasswordResetRequested = 105,
    AccountLocked = 106,
    AccountUnlocked = 107,
    SessionExpired = 108,
    PermissionGrant = 13,
    PermissionRevoke = 14,
    RoleAssigned = 109,
    RoleRevoked = 110,

    // ── SOS Lifecycle (end-to-end emergency chain) ──────────────
    SOSTrigger = 6,
    SOSCancel = 7,
    SOSResolved = 200,
    SOSExpired = 201,
    SOSPhraseDetected = 202,
    SOSQuickTapDetected = 203,
    SOSManualButton = 204,
    SOSFallDetected = 205,
    SOSDuressCodeEntered = 206,

    // ── Response Coordination ───────────────────────────────────
    ResponseRequestCreated = 210,
    ResponseDispatched = 211,
    ResponderNotified = 212,
    ResponderAcknowledged = 213,
    ResponderDeclined = 214,
    ResponderEnRoute = 215,
    ResponderOnScene = 216,
    ResponderTimedOut = 217,
    ResponseRedispatched = 218,
    ResponseStatusChanged = 219,

    // ── Escalation ──────────────────────────────────────────────
    AlertAcknowledge = 8,
    AlertEscalate = 9,
    EscalationScheduled = 220,
    EscalationFired = 221,
    EscalationCancelled = 222,
    EscalationScopeExpanded = 223,

    // ── 911 Emergency Services ──────────────────────────────────
    Emergency911Initiated = 230,
    Emergency911ConfirmationShown = 231,
    Emergency911ConfirmedByUser = 232,
    Emergency911CancelledByUser = 233,
    Emergency911CallDialing = 234,
    Emergency911CallConnected = 235,
    Emergency911CallCompleted = 236,
    Emergency911CallFailed = 237,
    Emergency911RateLimited = 238,
    Emergency911Blocked = 239,
    RapidSosLocationPushed = 240,
    RapidSosLocationUpdated = 241,
    RapidSosSessionClosed = 242,
    E911AddressRegistered = 243,
    E911AddressUpdated = 244,

    // ── Evidence Chain of Custody ────────────────────────────────
    EvidenceCapture = 10,
    EvidenceSubmitted = 250,
    EvidenceReceived = 251,
    EvidenceProcessed = 252,
    EvidenceViewed = 253,
    EvidenceExported = 254,
    EvidenceDeleted = 255,
    EvidenceRetentionApplied = 256,
    EvidenceIntegrityVerified = 257,
    EvidenceIntegrityFailed = 258,
    EvidenceChainBroken = 259,

    // ── Notification Delivery ───────────────────────────────────
    NotificationSent = 260,
    NotificationDelivered = 261,
    NotificationFailed = 262,
    NotificationRead = 263,
    NotificationExpired = 264,
    SmsSent = 265,
    SmsDelivered = 266,
    SmsFailed = 267,
    SmsReplyReceived = 268,
    VoiceCallInitiated = 269,
    VoiceCallAnswered = 270,
    VoiceCallCompleted = 271,
    VoiceCallFailed = 272,

    // ── Responder Communication (guardrails) ────────────────────
    ResponderMessageSent = 280,
    ResponderMessageApproved = 281,
    ResponderMessageRedacted = 282,
    ResponderMessageBlocked = 283,
    ResponderMessageRateLimited = 284,

    // ── Location Tracking ───────────────────────────────────────
    LocationUpdate = 11,
    GeofenceEntered = 290,
    GeofenceExited = 291,
    LocationSharingEnabled = 292,
    LocationSharingDisabled = 293,

    // ── Consent & Privacy ───────────────────────────────────────
    ConsentGranted = 300,
    ConsentRevoked = 301,
    ConsentUpdated = 302,
    TelephonyConsentGranted = 303,
    TelephonyConsentRevoked = 304,
    Emergency911ConsentUpdated = 305,
    DataExportRequested = 306,
    DataDeletionRequested = 307,
    DataDeletionCompleted = 308,
    PrivacyPolicyAccepted = 309,

    // ── Alarm System Integration ────────────────────────────────
    AlarmPanelRegistered = 310,
    AlarmPanelUnregistered = 311,
    AlarmEventReceived = 312,
    AlarmDuressDetected = 313,
    AlarmPlatformLinked = 314,
    AlarmPlatformUnlinked = 315,
    AlarmZoneMapUpdated = 316,

    // ── Telephony / Landline ────────────────────────────────────
    TelephonyDeviceRegistered = 320,
    TelephonyDeviceUnregistered = 321,
    DtmfEmergencyCodeTriggered = 322,
    LandlineOffHookTimeout = 323,
    WellnessCheckInScheduled = 324,
    WellnessCheckInCompleted = 325,
    WellnessCheckInMissed = 326,

    // ── Swarm Agent Operations ──────────────────────────────────
    SwarmCreated = 330,
    SwarmTaskDispatched = 331,
    SwarmTaskCompleted = 332,
    SwarmTaskFailed = 333,
    SwarmHandoff = 334,
    SwarmToolCalled = 335,
    SwarmAgentHeartbeat = 336,
    SwarmEscalationSweep = 337,
    SwarmGoalAggregation = 338,
    SwarmInventoryRefresh = 339,

    // ── Configuration & Administration ──────────────────────────
    ConfigChange = 12,
    UserProfileUpdated = 340,
    ParticipationPreferencesUpdated = 341,
    NotificationPreferencesUpdated = 342,
    EmergencyContactAdded = 343,
    EmergencyContactRemoved = 344,
    DwellingDetailsUpdated = 345,
    ValuablesInventoryUpdated = 346,

    // ── Audit Trail Self-Audit ──────────────────────────────────
    AuditIntegrityCheckPassed = 350,
    AuditIntegrityCheckFailed = 351,
    AuditExported = 352,
    AuditRetentionPurge = 353
}
