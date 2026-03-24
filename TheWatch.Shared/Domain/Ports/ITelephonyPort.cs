// =============================================================================
// ITelephonyPort — port interfaces for landline phone integration and PSTN bridging.
// =============================================================================
// Enables TheWatch to use landline phones as emergency detection and notification devices.
// This is CRITICAL for:
//   - Elderly users who don't own smartphones (largest at-risk demographic)
//   - Power outages where cell towers fail but copper PSTN lines stay active
//   - Rural areas with poor cell coverage but working phone lines
//   - Households with wall/desk phones near high-risk areas (kitchen, bathroom, basement)
//   - Users who want always-on speakerphone listening without draining a phone battery
//
// Architecture:
//   ┌──────────────┐     ┌────────────────────┐     ┌──────────────────┐
//   │  Landline     │────▶│  ATA / SIP Gateway  │────▶│  Local STT       │
//   │  Phone        │     │  (Obihai, Grand-    │     │  (Whisper, Vosk,  │
//   │  (speakerphone│     │   stream, Cisco)    │     │   Kaldi on-prem) │
//   │   or handset) │     │                    │     │                  │
//   └──────────────┘     └────────────────────┘     └────────┬─────────┘
//                                                             │
//                               phrase match / DTMF detected  │
//                                                             ▼
//                                                  ┌──────────────────┐
//                                                  │  TheWatch API    │
//                                                  │  /api/iot/alert  │
//                                                  │  source=Landline │
//                                                  └──────────────────┘
//
// Audio Privacy (WAL):
//   Audio is captured by the ATA adapter and streamed to ON-PREMISES speech-to-text ONLY.
//   Audio NEVER leaves the user's local network. Only structured metadata (matched phrase,
//   DTMF code, timestamp, caller ID) is transmitted to TheWatch backend.
//   If the user opts into cloud STT (e.g., Azure Speech, Google STT), they must explicitly
//   consent via the Telephony Privacy Consent flow in the dashboard settings.
//
// Supported Hardware:
//   - Obihai OBi200/OBi202 (Google Voice compatible ATA, widely deployed)
//   - Grandstream HT801/HT802 (SIP ATA, enterprise-grade)
//   - Cisco SPA112/SPA122 (end-of-life but massive installed base)
//   - Linksys PAP2T (legacy but still in homes)
//   - Any SIP-compliant ATA or IP phone with auto-answer + speakerphone
//   - Raspberry Pi + USB modem (DIY: cx93001 chipset for caller ID + audio)
//   - MagicJack (USB ATA, limited API but supported via audio tap)
//
// Supported Protocols:
//   - SIP/RTP (Session Initiation Protocol / Real-time Transport Protocol)
//   - DTMF (RFC 2833 in-band or SIP INFO out-of-band)
//   - Caller ID (FSK Bell 202 / V.23, DTMF-based CID for some regions)
//   - Contact ID (Ademco/SIA format — for alarm system integration over PSTN)
//   - T.38 fax relay (for legacy alarm panels that dial central stations)
//
// Cloud Telephony Providers (for outbound voice call notifications):
//   - Twilio Programmable Voice (primary — TwiML for IVR menus)
//   - Vonage Voice API (NCCO for call control)
//   - Bandwidth.com (BXML for call control)
//   - Azure Communication Services (direct PSTN calling)
//   - Amazon Connect (for high-volume call center-style outbound)
//   - SignalWire (Twilio-compatible API, lower cost)
//
// Example: Elderly user with speakerphone always-on
//   1. User registers their landline number + ATA device in TheWatch dashboard
//   2. ATA is configured to auto-answer a SIP "keep-alive" channel from local gateway
//   3. Local gateway (Raspberry Pi or NUC) runs Whisper/Vosk, continuously transcribes
//   4. PhraseMatchingEngine evaluates transcriptions against user's phrases
//   5. Match detected → POST /api/iot/alert { source: LandlinePhone, ... }
//   6. Responders notified. User's landline rings back with TTS confirmation:
//      "Help is on the way. 3 volunteers have been notified."
//
// Example: DTMF emergency code (eyes-free, no speech needed)
//   1. User picks up landline handset (or it's on speakerphone)
//   2. User dials *-5-5-5 (their configured emergency DTMF sequence)
//   3. ATA detects DTMF via RFC 2833 or SIP INFO events
//   4. Gateway → POST /api/iot/alert { triggerMethod: "DTMF_EMERGENCY_CODE", ... }
//
// Example: Off-hook timeout detection (potential incapacitation)
//   1. ATA detects phone has been off-hook for > configured threshold (default: 5 min)
//   2. No DTMF input, no speech detected — phone may have been knocked off hook
//   3. Gateway → POST /api/iot/alert { triggerMethod: "LANDLINE_OFF_HOOK_TIMEOUT", ... }
//   4. System initiates check-in: calls the line back with TTS prompt:
//      "This is The Watch. Press 1 if you're okay, or stay on the line for help."
//
// Example: Scheduled wellness check-in via landline
//   1. At 9 AM daily, system calls user's landline via Twilio
//   2. TTS: "Good morning! This is your daily The Watch check-in. Press 1 if okay."
//   3. User presses 1 → check-in recorded as OK
//   4. No answer after 3 attempts → escalate per user's escalation policy
// =============================================================================

namespace TheWatch.Shared.Domain.Ports;

// ═══════════════════════════════════════════════════════════════
// Telephony Device Models
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Type of analog telephone adapter or gateway device bridging PSTN to SIP/IP.
/// </summary>
public enum TelephonyGatewayType
{
    /// <summary>Obihai OBi200/OBi202 — popular consumer ATA, Google Voice compatible.</summary>
    ObihaiAta,

    /// <summary>Grandstream HT801/HT802 — enterprise SIP ATA with strong DTMF support.</summary>
    GrandstreamAta,

    /// <summary>Cisco SPA112/SPA122 — legacy but massive installed base.</summary>
    CiscoAta,

    /// <summary>Linksys PAP2T — legacy ATA, still in many homes.</summary>
    LinksysAta,

    /// <summary>Raspberry Pi with USB modem (cx93001 chipset) — DIY option for caller ID + audio.</summary>
    RaspberryPiModem,

    /// <summary>MagicJack USB ATA — limited API but audio tap supported.</summary>
    MagicJack,

    /// <summary>
    /// Cloud SIP trunk — no physical ATA. Twilio/Vonage/Bandwidth provides a virtual phone number.
    /// Audio arrives as RTP stream over the internet. Used for cloud-hosted STT.
    /// </summary>
    CloudSipTrunk,

    /// <summary>Any SIP-compliant ATA not in this list. User provides SIP credentials.</summary>
    GenericSipAta,

    /// <summary>IP desk phone with speakerphone (Polycom, Yealink, Cisco IP Phone).</summary>
    IpDeskPhone
}

/// <summary>
/// How the landline phone is being used for emergency detection.
/// </summary>
public enum TelephonyListenMode
{
    /// <summary>
    /// Always-on speakerphone — ATA auto-answers a persistent SIP channel.
    /// Audio is continuously streamed to local STT. Highest sensitivity, always listening.
    /// Best for: elderly users, users with mobility issues, high-risk environments.
    /// Power draw: minimal (ATA + phone are mains-powered).
    /// </summary>
    AlwaysOnSpeakerphone,

    /// <summary>
    /// Off-hook detection — triggers STT only when handset is lifted.
    /// Lower false-positive rate. Good for: general household use.
    /// </summary>
    OffHookTriggered,

    /// <summary>
    /// DTMF-only — no speech detection. User dials emergency code sequences.
    /// Most reliable (no STT needed). Good for: noisy environments, users with speech difficulties.
    /// </summary>
    DtmfOnly,

    /// <summary>
    /// Inbound call mode — system calls the user for scheduled check-ins.
    /// User responds via DTMF or voice. No always-on listening.
    /// </summary>
    InboundCheckInOnly,

    /// <summary>
    /// Ring detection only — detects if the phone rings and goes unanswered.
    /// Used for missed-call wellness checks. Minimal privacy impact.
    /// </summary>
    RingDetectOnly
}

/// <summary>
/// Registration record for a landline phone device linked to a TheWatch user.
/// </summary>
public record TelephonyDeviceRegistration(
    string DeviceId,
    string UserId,

    /// <summary>The phone number in E.164 format (e.g., "+12125551234").</summary>
    string PhoneNumber,

    /// <summary>The type of ATA/gateway device.</summary>
    TelephonyGatewayType GatewayType,

    /// <summary>How the phone is used for detection.</summary>
    TelephonyListenMode ListenMode,

    /// <summary>
    /// SIP URI for the device (e.g., "sip:user@192.168.1.50:5060").
    /// Used for direct SIP communication from the local gateway.
    /// </summary>
    string? SipUri,

    /// <summary>User-friendly name (e.g., "Kitchen wall phone", "Bedroom cordless base").</summary>
    string DeviceName,

    /// <summary>Where in the dwelling this phone is located (kitchen, bedroom, living room, etc.).</summary>
    string? InstallationZone,

    /// <summary>Whether the device is currently registered and reachable via SIP.</summary>
    bool IsOnline,

    /// <summary>When this device was registered.</summary>
    DateTime RegisteredAt,

    /// <summary>When the gateway last reported a heartbeat.</summary>
    DateTime LastHeartbeat,

    /// <summary>
    /// The configured DTMF emergency sequence (e.g., "*555", "911#", "55555").
    /// Null if DTMF triggering is not configured.
    /// </summary>
    string? DtmfEmergencyCode,

    /// <summary>
    /// Off-hook timeout in seconds. If phone is off-hook with no activity for this long,
    /// trigger an alert. Default: 300 (5 minutes). Null to disable.
    /// </summary>
    int? OffHookTimeoutSeconds,

    /// <summary>
    /// Whether to use on-premises STT (true) or cloud STT (false).
    /// On-premises is the default for privacy. Cloud requires explicit consent.
    /// </summary>
    bool UseOnPremisesStt = true,

    /// <summary>
    /// The on-premises STT engine to use.
    /// "WHISPER_CPP" (OpenAI Whisper, local), "VOSK" (Kaldi-based), "COQUI" (Mozilla DeepSpeech successor).
    /// </summary>
    string? SttEngine = "WHISPER_CPP"
);

// ═══════════════════════════════════════════════════════════════
// DTMF Models
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// A DTMF (Dual-Tone Multi-Frequency) event detected on a landline.
/// DTMF tones: 0-9, *, #, A-D (extended).
/// </summary>
public record DtmfEvent(
    string DeviceId,
    string UserId,

    /// <summary>The DTMF digits detected (e.g., "*555", "911#").</summary>
    string DtmfSequence,

    /// <summary>How long the sequence took to enter (for timing-based validation).</summary>
    TimeSpan EntryDuration,

    /// <summary>
    /// Whether this was detected in-band (RFC 2833 RTP events) or out-of-band (SIP INFO).
    /// </summary>
    DtmfDetectionMethod DetectionMethod,

    DateTime DetectedAt
);

/// <summary>
/// How DTMF tones were detected in the audio/signaling stream.
/// </summary>
public enum DtmfDetectionMethod
{
    /// <summary>RFC 2833 — DTMF events carried as named telephone events in RTP.</summary>
    Rfc2833,

    /// <summary>SIP INFO — DTMF digits carried in SIP INFO messages (application/dtmf-relay).</summary>
    SipInfo,

    /// <summary>In-band audio detection — DTMF tones detected in the raw audio stream.</summary>
    InBandAudio
}

// ═══════════════════════════════════════════════════════════════
// Voice Call Notification Models
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Configuration for an outbound voice call notification (TTS + DTMF menu).
/// Used for: wellness check-ins, alert confirmations, responder dispatch to landlines.
/// </summary>
public record VoiceCallRequest(
    string CallId,
    string RecipientUserId,

    /// <summary>Phone number to call in E.164 format.</summary>
    string PhoneNumber,

    /// <summary>TTS message to speak when the call is answered.</summary>
    string TtsMessage,

    /// <summary>Language/voice for TTS (e.g., "en-US", "es-MX").</summary>
    string TtsLanguage,

    /// <summary>
    /// DTMF menu options after TTS message plays.
    /// Key = DTMF digit, Value = action description.
    /// Example: { "1": "I'm okay", "2": "Send help", "9": "Call 911" }
    /// </summary>
    IReadOnlyDictionary<string, string> DtmfMenu,

    /// <summary>How many times to retry if call is not answered. Default: 3.</summary>
    int MaxRetries,

    /// <summary>Seconds to wait for answer before hanging up. Default: 30.</summary>
    int RingTimeoutSeconds,

    /// <summary>Seconds to wait for DTMF input after TTS finishes. Default: 15.</summary>
    int DtmfInputTimeoutSeconds,

    /// <summary>If true, repeat the TTS message and menu once before timing out.</summary>
    bool RepeatMessageOnTimeout,

    /// <summary>Priority — determines call scheduling order.</summary>
    NotificationPriority Priority,

    /// <summary>The response request ID this call is related to (if any).</summary>
    string? ResponseRequestId,

    DateTime CreatedAt
);

/// <summary>
/// Result of an outbound voice call attempt.
/// </summary>
public record VoiceCallResult(
    string CallId,
    string RecipientUserId,
    VoiceCallStatus Status,

    /// <summary>The DTMF digit pressed by the user, if any.</summary>
    string? DtmfResponse,

    /// <summary>The action mapped to the pressed DTMF digit.</summary>
    string? ResponseAction,

    /// <summary>External call SID from the provider (Twilio SID, etc.).</summary>
    string? ExternalCallId,

    /// <summary>How long the call lasted.</summary>
    TimeSpan? CallDuration,

    /// <summary>How many attempts were made before this result.</summary>
    int AttemptNumber,

    /// <summary>Error message if the call failed.</summary>
    string? ErrorMessage,

    DateTime CompletedAt
);

/// <summary>
/// Status of an outbound voice call.
/// </summary>
public enum VoiceCallStatus
{
    /// <summary>Call queued, not yet initiated.</summary>
    Queued,

    /// <summary>Call is ringing.</summary>
    Ringing,

    /// <summary>Call answered, TTS playing.</summary>
    InProgress,

    /// <summary>Call answered, user pressed a DTMF digit — response captured.</summary>
    Responded,

    /// <summary>Call answered but user did not press any DTMF digit before timeout.</summary>
    NoInput,

    /// <summary>Call was not answered after all retries.</summary>
    NoAnswer,

    /// <summary>Line was busy after all retries.</summary>
    Busy,

    /// <summary>Call failed (network error, invalid number, provider issue).</summary>
    Failed,

    /// <summary>Voicemail detected — TTS message left as voicemail.</summary>
    Voicemail,

    /// <summary>Call cancelled before completion.</summary>
    Cancelled
}

// ═══════════════════════════════════════════════════════════════
// Telephony Privacy Consent
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// User's consent record for telephony audio processing.
/// Required by GDPR, CCPA, HIPAA (if health-related), and wiretap laws (state-level).
/// Two-party consent states (CA, FL, IL, etc.) require explicit opt-in for speakerphone listening.
/// </summary>
public record TelephonyPrivacyConsent(
    string UserId,
    string DeviceId,

    /// <summary>User has consented to on-premises audio processing for phrase detection.</summary>
    bool ConsentOnPremisesStt,

    /// <summary>User has consented to cloud-based audio processing (if on-prem is unavailable).</summary>
    bool ConsentCloudStt,

    /// <summary>User acknowledges they are in a one-party or all-party consent jurisdiction.</summary>
    bool AcknowledgesRecordingLaws,

    /// <summary>The jurisdiction (state/country) where the phone is located.</summary>
    string? Jurisdiction,

    /// <summary>
    /// Whether all household members have been informed of the always-on listening.
    /// Required in all-party consent jurisdictions (CA, FL, IL, MA, MD, MT, NH, PA, WA, etc.).
    /// </summary>
    bool AllHouseholdMembersInformed,

    DateTime ConsentedAt,
    DateTime? RevokedAt
);

// ═══════════════════════════════════════════════════════════════
// Port Interface
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Port for landline phone / PSTN telephony integration.
/// Handles: device registration, DTMF event processing, outbound voice calls,
/// privacy consent management, and speakerphone monitoring configuration.
///
/// Adapters:
///   - MockTelephonyAdapter (dev/test)
///   - TwilioTelephonyAdapter (prod — Twilio Programmable Voice for outbound calls)
///   - VonageTelephonyAdapter (prod — Vonage Voice API alternative)
///   - LocalGatewayTelephonyAdapter (prod — on-premises ATA/SIP gateway management)
///
/// Integration with IIoTAlertPort:
///   Landline alerts flow through the standard IoT alert pipeline:
///   1. Local gateway detects phrase/DTMF → POST /api/iot/alert { source: LandlinePhone }
///   2. IIoTAlertPort.TriggerIoTAlertAsync() processes the alert
///   3. ITelephonyPort handles the telephony-specific aspects (device management, voice calls)
/// </summary>
public interface ITelephonyPort
{
    // ── Device Registration ─────────────────────────────────────

    /// <summary>
    /// Register a landline phone device (ATA/SIP gateway) for a user.
    /// Validates SIP connectivity and caller ID if applicable.
    /// </summary>
    Task<TelephonyDeviceRegistration> RegisterDeviceAsync(
        TelephonyDeviceRegistration registration,
        CancellationToken ct = default);

    /// <summary>Unregister a landline device.</summary>
    Task<bool> UnregisterDeviceAsync(
        string deviceId,
        CancellationToken ct = default);

    /// <summary>Get all registered landline devices for a user.</summary>
    Task<IReadOnlyList<TelephonyDeviceRegistration>> GetDevicesAsync(
        string userId,
        CancellationToken ct = default);

    /// <summary>Update device configuration (listen mode, DTMF code, timeouts, etc.).</summary>
    Task<TelephonyDeviceRegistration> UpdateDeviceAsync(
        TelephonyDeviceRegistration registration,
        CancellationToken ct = default);

    /// <summary>
    /// Check if a device is online and responsive (SIP REGISTER/OPTIONS ping).
    /// </summary>
    Task<bool> PingDeviceAsync(
        string deviceId,
        CancellationToken ct = default);

    // ── DTMF Event Processing ───────────────────────────────────

    /// <summary>
    /// Process an inbound DTMF event from a landline device.
    /// Validates the sequence against the user's configured emergency codes.
    /// Returns true if the sequence triggered an alert.
    /// </summary>
    Task<bool> ProcessDtmfEventAsync(
        DtmfEvent dtmfEvent,
        CancellationToken ct = default);

    /// <summary>
    /// Configure the DTMF emergency code for a device.
    /// Validates that the code doesn't conflict with standard telephony codes
    /// (e.g., *67 for caller ID block, *69 for call return).
    /// </summary>
    Task<bool> ConfigureDtmfCodeAsync(
        string deviceId,
        string dtmfCode,
        CancellationToken ct = default);

    // ── Outbound Voice Calls ────────────────────────────────────

    /// <summary>
    /// Initiate an outbound voice call with TTS message and DTMF response menu.
    /// Used for: wellness check-ins, alert confirmations, responder notifications.
    /// </summary>
    Task<VoiceCallResult> InitiateCallAsync(
        VoiceCallRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Cancel a pending or in-progress outbound call.
    /// </summary>
    Task<bool> CancelCallAsync(
        string callId,
        CancellationToken ct = default);

    /// <summary>
    /// Get the result of a completed voice call.
    /// </summary>
    Task<VoiceCallResult?> GetCallResultAsync(
        string callId,
        CancellationToken ct = default);

    /// <summary>
    /// Schedule a recurring wellness check-in call (e.g., daily at 9 AM).
    /// Implemented via Hangfire or platform scheduler.
    /// </summary>
    Task<string> ScheduleCheckInCallAsync(
        string userId,
        string phoneNumber,
        string ttsMessage,
        TimeOnly callTime,
        DayOfWeek[]? days,
        CancellationToken ct = default);

    /// <summary>Cancel a scheduled check-in call series.</summary>
    Task<bool> CancelScheduledCheckInAsync(
        string scheduleId,
        CancellationToken ct = default);

    // ── Privacy Consent ─────────────────────────────────────────

    /// <summary>Record telephony privacy consent for a user/device.</summary>
    Task<TelephonyPrivacyConsent> RecordConsentAsync(
        TelephonyPrivacyConsent consent,
        CancellationToken ct = default);

    /// <summary>Revoke telephony privacy consent. Stops all listening on the device.</summary>
    Task<bool> RevokeConsentAsync(
        string userId,
        string deviceId,
        CancellationToken ct = default);

    /// <summary>Check if a user has valid consent for a device.</summary>
    Task<TelephonyPrivacyConsent?> GetConsentAsync(
        string userId,
        string deviceId,
        CancellationToken ct = default);
}
