// =============================================================================
// IEmergencyServicesPort — port for initiating 911/emergency service calls
// on behalf of a user during a Watch call or escalation event.
// =============================================================================
//
// This is the most legally and operationally sensitive port in TheWatch.
// It bridges our volunteer response system to the government PSAP (Public Safety
// Answering Point) / 911 dispatch infrastructure.
//
// WHY this exists:
//   - User may be incapacitated (fall, assault, medical event) and unable to dial 911
//   - User may have triggered SOS via phrase detection, quick-tap, or duress code
//   - Escalation policy (Immediate911, Conditional911) may have fired automatically
//   - A Watch call responder may see the situation warrants 911 and trigger it manually
//   - User opted into "auto-notify first responders" at signup (defaults to YES)
//
// HOW 911 is contacted:
//   ┌──────────────────────────────────────────────────────────────────────────┐
//   │ Method               │ Provider               │ Latency  │ Location     │
//   ├──────────────────────┼────────────────────────┼──────────┼──────────────┤
//   │ Twilio <]>           │ Twilio Programmable     │ ~2-4s    │ Caller ID +  │
//   │   SIP INVITE to PSAP │   Voice + PSTN          │          │ TTS context  │
//   │                      │                        │          │              │
//   │ RapidSOS NG911 API   │ RapidSOS Emergency     │ ~1-3s    │ GPS coords   │
//   │   (data-only push)   │   Data Platform         │          │ pushed to    │
//   │                      │   (used by 97% of US   │          │ PSAP map     │
//   │                      │    PSAPs)               │          │              │
//   │                      │                        │          │              │
//   │ Bandwidth 911 API    │ Bandwidth.com          │ ~2-5s    │ E911 reg     │
//   │   (direct PSTN)      │   Emergency Calling     │          │ address      │
//   │                      │                        │          │              │
//   │ Azure Comm Services  │ Microsoft ACS          │ ~3-5s    │ Caller ID    │
//   │   (PSTN calling)     │   Direct Routing        │          │              │
//   │                      │                        │          │              │
//   │ Vonage 911 API       │ Vonage Emergency       │ ~2-5s    │ E911 reg     │
//   │                      │   Services              │          │              │
//   │                      │                        │          │              │
//   │ NG911 i3 (future)    │ Direct PSAP HTTPS      │ ~1s      │ PIDF-LO      │
//   │                      │   (NENA i3 standard)    │          │ (GPS+floor)  │
//   └──────────────────────────────────────────────────────────────────────────┘
//
// CRITICAL: E911 location registration
//   Under FCC rules (47 CFR 9.5), any service that enables 911 calling must provide
//   location data. For VoIP-originated calls (Twilio, Bandwidth), the user's E911
//   address must be pre-registered via the provider's E911 API. For RapidSOS, GPS
//   coordinates are pushed in real-time via the Emergency Data Platform.
//
// CRITICAL: TTS context relay
//   When TheWatch initiates a 911 call on behalf of a user, the call connects to
//   the local PSAP. A TTS message plays for the dispatcher:
//     "This is an automated emergency call from The Watch safety application.
//      [User name] at [address/coordinates] has triggered an emergency alert.
//      Alert type: [SOS / Medical / Fire / Intrusion / Duress].
//      [N] volunteer responders have been notified and are en route.
//      The user's phone number is [number]. Repeating..."
//
//   This gives the 911 dispatcher actionable context immediately, even if the user
//   cannot speak. The call stays open for the dispatcher to listen/speak.
//
// CRITICAL: User consent
//   Auto-911 is OPT-IN at signup (defaults to YES for maximum safety).
//   User can change this in Profile → Emergency Settings:
//     - "Automatically notify 911 during emergencies" [toggle, default ON]
//     - "Require my confirmation before calling 911" [toggle, default OFF]
//     - "Never call 911 automatically" [toggle, default OFF]
//   The consent record is stored and checked before every 911 dispatch.
//
// CRITICAL: Abuse prevention
//   False 911 calls are a crime (most jurisdictions). Safeguards:
//     1. Rate limiting: max 1 auto-911 call per user per 30-minute window
//     2. Confirmation window: 15-second countdown with cancel button before call is placed
//     3. Audit trail: every 911 dispatch is logged with full context for legal defense
//     4. Escalation-only: auto-911 only fires from validated escalation policies, not raw input
//     5. Cool-down after cancel: if user cancels, 5-minute cool-down before next auto-911
//
// Example: SOS phrase detected → auto-911
//   1. User says "help me now" → PhraseMatchingEngine matches → SOS alert created
//   2. ResponseRequest created with EscalationPolicy.Immediate911
//   3. Volunteer dispatch fires in parallel with:
//   4. IEmergencyServicesPort.Initiate911CallAsync() — checks consent, places call via Twilio
//   5. RapidSOS receives GPS push (user's phone coordinates) — PSAP sees location on map
//   6. 911 dispatcher hears TTS with context, dispatches police/EMS
//   7. Volunteers arrive in ~2-4 min, police/EMS arrive in ~7-10 min
//
// Example: Watch call responder triggers 911
//   1. Responder arrives at scene, sees user is injured
//   2. Responder taps "Call 911" button in the incident channel
//   3. IEmergencyServicesPort.Initiate911CallAsync() fires with responder as initiator
//   4. Call placed with TTS: "...a community responder is on scene and reports injuries..."
//
// Example: Conditional escalation
//   1. User triggers SOS with EscalationPolicy.Conditional911
//   2. System waits 2 minutes for volunteer acknowledgments
//   3. Only 1 of 5 required responders acknowledged
//   4. IEscalationPort.CheckAndEscalateAsync() fires
//   5. IEmergencyServicesPort.Initiate911CallAsync() called automatically
//   6. 911 TTS includes: "...automated escalation: insufficient volunteer response..."
// =============================================================================

namespace TheWatch.Shared.Domain.Ports;

// ═══════════════════════════════════════════════════════════════
// Emergency Services Models
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// The type of emergency service to notify.
/// </summary>
public enum EmergencyServiceType
{
    /// <summary>Police — for intrusion, assault, duress, threats.</summary>
    Police,

    /// <summary>Fire department — for fire, gas leak, explosion.</summary>
    Fire,

    /// <summary>Emergency Medical Services — for medical emergency, injury, fall.</summary>
    Ems,

    /// <summary>All services — for unknown or multi-category emergencies.</summary>
    All
}

/// <summary>
/// How the 911 call was triggered.
/// </summary>
public enum Emergency911TriggerSource
{
    /// <summary>Automatic: escalation policy fired (Immediate911 or Conditional911).</summary>
    AutoEscalation,

    /// <summary>User pressed the "Call 911" button in the app.</summary>
    UserManual,

    /// <summary>A Watch call responder pressed "Call 911" from the incident channel.</summary>
    ResponderManual,

    /// <summary>Alarm system integration: alarm panel event mapped to Immediate911.</summary>
    AlarmSystemEvent,

    /// <summary>IoT sensor event: smoke, CO, flood mapped to Immediate911.</summary>
    IoTSensorEvent,

    /// <summary>Telephony: landline DTMF 911 code or off-hook timeout.</summary>
    TelephonyEvent,

    /// <summary>Fall detection: accelerometer/gyroscope detected impact + no movement.</summary>
    FallDetection
}

/// <summary>
/// User's consent preferences for automatic 911 dispatch.
/// </summary>
public record Emergency911Consent(
    string UserId,

    /// <summary>Master toggle: allow TheWatch to call 911 on user's behalf.</summary>
    bool AutoNotify911Enabled,

    /// <summary>
    /// If true, show a 15-second countdown with cancel button before placing the call.
    /// If false, call is placed immediately when policy triggers.
    /// Default: false (immediate for maximum safety).
    /// </summary>
    bool RequireConfirmation,

    /// <summary>
    /// Pre-registered E911 address for VoIP-originated calls (FCC requirement).
    /// Null if not yet registered — must be set before auto-911 can function.
    /// </summary>
    string? E911Address,

    /// <summary>
    /// Pre-registered E911 address components for structured PIDF-LO.
    /// </summary>
    string? E911City,
    string? E911State,
    string? E911Zip,
    string? E911Country,

    /// <summary>
    /// User's preferred language for TTS relay to 911 dispatcher.
    /// Default: "en-US". Spanish: "es-US". Other languages supported.
    /// </summary>
    string TtsLanguage = "en-US",

    /// <summary>
    /// Additional medical information to relay to 911 dispatcher.
    /// Example: "diabetic, takes insulin, allergic to penicillin, mobility impaired"
    /// </summary>
    string? MedicalInfo = null,

    /// <summary>
    /// Number of occupants at the registered address (helps EMS resource planning).
    /// </summary>
    int? OccupantCount = null,

    /// <summary>Any pets at the address (fire department needs this).</summary>
    string? PetInfo = null,

    /// <summary>Gate code, lock box code, or access instructions for first responders.</summary>
    string? AccessInstructions = null,

    DateTime ConsentedAt = default,
    DateTime? RevokedAt = null
);

/// <summary>
/// Request to initiate a 911 call on behalf of a user.
/// </summary>
public record Emergency911Request(
    string RequestId,
    string UserId,

    /// <summary>The ResponseRequest that triggered this 911 call (if any).</summary>
    string? ResponseRequestId,

    /// <summary>How this 911 call was triggered.</summary>
    Emergency911TriggerSource TriggerSource,

    /// <summary>Which emergency service to request.</summary>
    EmergencyServiceType ServiceType,

    /// <summary>User's current GPS coordinates.</summary>
    double Latitude,
    double Longitude,
    double? AccuracyMeters,

    /// <summary>
    /// Human-readable address (reverse-geocoded or from E911 registration).
    /// </summary>
    string? Address,

    /// <summary>
    /// Context summary for TTS relay to 911 dispatcher.
    /// Auto-generated from alert type, user profile, and incident data.
    /// </summary>
    string ContextSummary,

    /// <summary>
    /// The user's callback phone number (so 911 can call back).
    /// </summary>
    string UserPhoneNumber,

    /// <summary>User's name for the dispatcher.</summary>
    string UserName,

    /// <summary>Medical info from consent record.</summary>
    string? MedicalInfo,

    /// <summary>Access instructions from consent record.</summary>
    string? AccessInstructions,

    /// <summary>How many volunteer responders are already en route.</summary>
    int VolunteerRespondersEnRoute,

    /// <summary>Who initiated this 911 request (userId or responderId).</summary>
    string InitiatedBy,

    DateTime CreatedAt
);

/// <summary>
/// Result of a 911 call attempt.
/// </summary>
public record Emergency911Result(
    string RequestId,
    string UserId,
    Emergency911CallStatus Status,

    /// <summary>External call SID from the telephony provider.</summary>
    string? ExternalCallId,

    /// <summary>
    /// Whether location data was successfully pushed to RapidSOS.
    /// </summary>
    bool RapidSosLocationPushed,

    /// <summary>Duration of the 911 call (null if still in progress or failed).</summary>
    TimeSpan? CallDuration,

    /// <summary>
    /// Whether the 15-second confirmation countdown was shown and completed.
    /// False if RequireConfirmation was off or the call was immediate.
    /// </summary>
    bool ConfirmationRequired,
    bool? ConfirmationGiven,

    /// <summary>Error message if the call failed.</summary>
    string? ErrorMessage,

    /// <summary>Audit log entry ID for legal/compliance tracking.</summary>
    string AuditEntryId,

    DateTime CompletedAt
);

/// <summary>
/// Status of a 911 call.
/// </summary>
public enum Emergency911CallStatus
{
    /// <summary>Queued — waiting for confirmation countdown or rate-limit cool-down.</summary>
    Pending,

    /// <summary>Confirmation countdown in progress (15 seconds).</summary>
    AwaitingConfirmation,

    /// <summary>User cancelled during confirmation countdown.</summary>
    CancelledByUser,

    /// <summary>Call is being placed to 911 via telephony provider.</summary>
    Dialing,

    /// <summary>Connected to PSAP — TTS playing or dispatcher listening.</summary>
    Connected,

    /// <summary>Call completed successfully — dispatcher acknowledged.</summary>
    Completed,

    /// <summary>Call failed — telephony error, PSAP unreachable, etc.</summary>
    Failed,

    /// <summary>Blocked — user has auto-911 disabled, or rate limit exceeded.</summary>
    Blocked,

    /// <summary>Skipped — duplicate request within cool-down window.</summary>
    RateLimited
}

/// <summary>
/// RapidSOS location push record — sent to the RapidSOS Emergency Data Platform
/// so the PSAP can see the user's GPS location on their dispatch map.
/// 97% of US PSAPs use RapidSOS (as of 2025).
/// </summary>
public record RapidSosLocationPush(
    string UserId,
    string RequestId,
    double Latitude,
    double Longitude,
    double? AccuracyMeters,
    double? Altitude,
    double? Floor,
    string? Address,
    string? CallerName,
    string? CallbackNumber,
    string? MedicalInfo,
    string? IncidentType,
    DateTime Timestamp
);

// ═══════════════════════════════════════════════════════════════
// Port Interface
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Port for initiating 911/emergency service calls on behalf of a user.
///
/// This port is called by:
///   - IEscalationPort when an escalation policy fires (Immediate911, Conditional911)
///   - SwarmCoordinationService when a swarm agent decides 911 is needed
///   - ResponseController when a user or responder taps "Call 911"
///   - IAlarmSystemPort when an alarm event maps to Immediate911 escalation
///
/// Adapters:
///   - MockEmergencyServicesAdapter (dev/test — logs calls, does not dial)
///   - TwilioEmergencyServicesAdapter (prod — Twilio Programmable Voice to PSTN 911)
///   - RapidSosAdapter (prod — RapidSOS Emergency Data Platform for NG911 location push)
///   - BandwidthEmergencyAdapter (prod — Bandwidth.com 911 API)
///   - CompositeEmergencyServicesAdapter (prod — Twilio call + RapidSOS location in parallel)
///
/// LEGAL: Every call placed through this port is logged in the audit trail with
///        full context for legal defense against false-call claims.
/// </summary>
public interface IEmergencyServicesPort
{
    // ── 911 Call Initiation ─────────────────────────────────────

    /// <summary>
    /// Initiate a 911 call on behalf of a user.
    ///
    /// Flow:
    ///   1. Check user consent (AutoNotify911Enabled must be true)
    ///   2. Check rate limit (max 1 call per 30 min per user)
    ///   3. If RequireConfirmation: start 15-second countdown, wait for confirm/cancel
    ///   4. Place call via telephony provider (Twilio/Bandwidth/ACS)
    ///   5. Push location to RapidSOS (in parallel)
    ///   6. TTS plays context summary for 911 dispatcher
    ///   7. Call stays open for 2-way audio
    ///   8. Log audit entry
    /// </summary>
    Task<Emergency911Result> Initiate911CallAsync(
        Emergency911Request request,
        CancellationToken ct = default);

    /// <summary>
    /// Cancel a pending 911 call (during confirmation countdown).
    /// Cannot cancel a call that is already dialing or connected.
    /// </summary>
    Task<bool> Cancel911CallAsync(
        string requestId,
        string cancelledBy,
        CancellationToken ct = default);

    /// <summary>
    /// Confirm a pending 911 call (user confirmed during countdown).
    /// </summary>
    Task<Emergency911Result> Confirm911CallAsync(
        string requestId,
        CancellationToken ct = default);

    /// <summary>
    /// Get the status/result of a 911 call.
    /// </summary>
    Task<Emergency911Result?> Get911CallResultAsync(
        string requestId,
        CancellationToken ct = default);

    // ── RapidSOS Location Push ──────────────────────────────────

    /// <summary>
    /// Push the user's real-time GPS location to RapidSOS Emergency Data Platform.
    /// This makes the location visible to 911 dispatchers on their map display.
    /// Can be called independently of a 911 call (e.g., when SOS is first triggered).
    /// </summary>
    Task<bool> PushLocationToRapidSosAsync(
        RapidSosLocationPush locationData,
        CancellationToken ct = default);

    /// <summary>
    /// Update a previously pushed location (user is moving).
    /// RapidSOS supports location updates during an active incident.
    /// </summary>
    Task<bool> UpdateRapidSosLocationAsync(
        string requestId,
        double latitude,
        double longitude,
        double? accuracyMeters,
        CancellationToken ct = default);

    /// <summary>
    /// Clear/close the RapidSOS location session (incident resolved).
    /// </summary>
    Task<bool> CloseRapidSosSessionAsync(
        string requestId,
        CancellationToken ct = default);

    // ── E911 Address Registration ───────────────────────────────

    /// <summary>
    /// Register or update a user's E911 address with the telephony provider.
    /// Required by FCC for VoIP-originated 911 calls.
    /// Must be called during user onboarding or when address changes.
    /// </summary>
    Task<bool> RegisterE911AddressAsync(
        string userId,
        string address,
        string city,
        string state,
        string zip,
        string country,
        string callbackNumber,
        CancellationToken ct = default);

    /// <summary>
    /// Validate that a user has a registered E911 address (prerequisite for auto-911).
    /// </summary>
    Task<bool> HasRegisteredE911AddressAsync(
        string userId,
        CancellationToken ct = default);

    // ── Consent Management ──────────────────────────────────────

    /// <summary>
    /// Get a user's 911 consent preferences.
    /// </summary>
    Task<Emergency911Consent?> GetConsentAsync(
        string userId,
        CancellationToken ct = default);

    /// <summary>
    /// Update a user's 911 consent preferences.
    /// Changing AutoNotify911Enabled requires re-confirmation (legal safeguard).
    /// </summary>
    Task<Emergency911Consent> UpdateConsentAsync(
        Emergency911Consent consent,
        CancellationToken ct = default);

    // ── Audit & History ─────────────────────────────────────────

    /// <summary>
    /// Get the 911 call history for a user (audit trail).
    /// Includes all attempts (successful, failed, cancelled, rate-limited).
    /// </summary>
    Task<IReadOnlyList<Emergency911Result>> GetCallHistoryAsync(
        string userId,
        int maxResults = 50,
        CancellationToken ct = default);
}
