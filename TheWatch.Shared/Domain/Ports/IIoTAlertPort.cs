// =============================================================================
// IIoTAlertPort — port interfaces for IoT device alert ingestion and management.
// =============================================================================
// Handles alert ingestion from smart home, IoT, telephony, and alarm ecosystems:
//   - Amazon Alexa (Skills Kit custom skill + Smart Home API)
//   - Google Home / Google Assistant (Actions on Google, conversational + home graph)
//   - Samsung SmartThings (SmartApp webhooks, device capability model)
//   - Apple HomeKit (via HomeKit Accessory Protocol bridged through companion app)
//   - IFTTT (applets with TheWatch service, webhook triggers)
//   - Custom webhooks (Z-Wave hubs, Hubitat, Home Assistant, openHAB, etc.)
//   - Landline phones (ATA/SIP-bridged PSTN with speakerphone phrase detection + DTMF codes)
//   - Alarm systems (ADT, SimpliSafe, Vivint, Honeywell, DSC — via Alarm.com, Contact ID, SIA DC-07)
//
// Flow:
//   IoT Device → Skill/Action/SmartApp triggers → Webhook/OAuth callback
//     → IoTAlertController validates + ingests → IIoTAlertPort.TriggerIoTAlertAsync()
//     → Maps external user to TheWatch user → Dispatches via IResponseCoordinationService
//     → SignalR broadcast to dashboard → Push notification to nearby responders
//
// Account Linking:
//   Each IoT platform uses OAuth2 account linking. The user authorizes TheWatch
//   in their Alexa/Google Home app, which provides an access token. We map the
//   platform's external user ID to our internal TheWatch user ID.
//
// Example: Alexa Skill invocation
//   User: "Alexa, tell The Watch I need help"
//   Alexa: sends IntentRequest with userId + accessToken to our Lambda/webhook
//   Lambda: POST /api/iot/alert { source: "Alexa", externalUserId: "amzn1.ask...", ... }
//   Backend: maps to TheWatch user, triggers SOS, fans out to responders
//
// Example: Google Home
//   User: "Hey Google, activate The Watch emergency"
//   Google: sends webhook with userId + conv token
//   Backend: same pipeline as above
//
// Example: SmartThings panic button
//   User: presses physical Zigbee/Z-Wave panic button
//   SmartThings: SmartApp webhook fires with device event
//   Backend: POST /api/iot/alert { source: "SmartThings", triggerMethod: "PANIC_BUTTON", ... }
//
// Example: IFTTT applet
//   Trigger: "If I say 'trigger The Watch emergency' on my Google Assistant"
//   Action: webhook to /api/iot/webhook/ifttt with maker key
//
// Example: Landline phone speakerphone pickup
//   User: speaks emergency phrase near landline on speakerphone (or picked up)
//   ATA/SIP gateway: captures audio → streams to on-prem STT (Whisper/Vosk)
//   Gateway: phrase matched → POST /api/iot/alert { source: "LandlinePhone",
//     triggerMethod: "LANDLINE_PHRASE_DETECTED", deviceType: "ATA_GRANDSTREAM", ... }
//   NOTE: Audio NEVER leaves the local network. STT runs on gateway/local server.
//
// Example: Landline DTMF emergency code
//   User: dials *-5-5-5 on landline keypad (eyes-free panic trigger)
//   ATA: detects DTMF sequence → POST /api/iot/alert { source: "LandlinePhone",
//     triggerMethod: "DTMF_EMERGENCY_CODE", ... }
//
// Example: Alarm system panic button
//   User: presses panic button on Honeywell Vista keypad (or enters duress code)
//   Alarm panel → Alarm.com API or Contact ID to central station
//   Central station / Alarm.com webhook: POST /api/iot/alert { source: "AlarmSystem",
//     triggerMethod: "ALARM_PANIC_BUTTON", deviceType: "HONEYWELL_VISTA", ... }
//
// Example: Alarm system duress code
//   User: enters duress code (e.g., code+1) on alarm keypad while being coerced
//   Alarm panel: recognizes duress → triggers SilentDuress scope
//   POST /api/iot/alert { source: "AlarmSystem", triggerMethod: "ALARM_DURESS_CODE",
//     scope: SilentDuress, ... }
//
// WAL: Audio NEVER leaves the device through this port.
//      IoT devices transmit ONLY structured metadata (who, where, what type).
//      No PII beyond what is required for user mapping (external ID + tokens).
//      All tokens are encrypted at rest and rotated per platform policy.
// =============================================================================

namespace TheWatch.Shared.Domain.Ports;

// ═══════════════════════════════════════════════════════════════
// IoT Source Enumeration
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// The originating IoT platform for an alert or device registration.
/// Each source has its own authentication model, webhook format, and capabilities.
/// </summary>
public enum IoTSource
{
    /// <summary>Amazon Alexa — Skills Kit custom skill or Smart Home skill.</summary>
    Alexa,

    /// <summary>Google Home / Google Assistant — Actions on Google conversational or home control.</summary>
    GoogleHome,

    /// <summary>Samsung SmartThings — SmartApp webhook with device capability model.</summary>
    SmartThings,

    /// <summary>Apple HomeKit — bridged via companion iOS/macOS app (HAP protocol).</summary>
    HomeKit,

    /// <summary>IFTTT — applet with TheWatch service, uses Maker Webhooks for custom triggers.</summary>
    IFTTT,

    /// <summary>Custom webhook — Home Assistant, openHAB, Hubitat, Z-Wave hubs, etc.</summary>
    CustomWebhook,

    /// <summary>Ring — Ring Alarm panic button or doorbell-triggered alert.</summary>
    Ring,

    /// <summary>Wyze — Wyze Sense motion/contact sensors or Wyze Cam person detection.</summary>
    Wyze,

    /// <summary>Tuya — Tuya-based smart devices (covers hundreds of white-label brands).</summary>
    Tuya,

    /// <summary>Zigbee direct — Zigbee coordinator (ConBee, HUSBZB-1) via zigbee2mqtt or deCONZ.</summary>
    ZigbeeDirect,

    /// <summary>Z-Wave direct — Z-Wave controller (Aeotec Z-Stick) via zwave-js or OpenZWave.</summary>
    ZWaveDirect,

    /// <summary>Matter — Matter-compatible devices (Thread/WiFi) via Matter SDK.</summary>
    Matter,

    /// <summary>
    /// Landline phone — PSTN/SIP-bridged landline with speakerphone phrase detection.
    /// Uses ATA adapter (Obihai, Grandstream) or SIP trunk (Twilio, Vonage, Bandwidth)
    /// to bridge audio from a traditional landline phone to our speech-to-text pipeline.
    /// Supports: speakerphone always-on listening, DTMF emergency codes, caller-ID mapping.
    /// See ITelephonyPort for full protocol details.
    /// </summary>
    LandlinePhone,

    /// <summary>
    /// Alarm system — residential/commercial alarm panel integration.
    /// Covers: ADT, SimpliSafe, Vivint, Brinks, Ring Alarm, Honeywell, DSC, Qolsys, Elk.
    /// Protocols: Alarm.com API, Contact ID (Ademco), SIA DC-07 (IP alarm monitoring),
    /// Z-Wave/Zigbee sensor bridging, and proprietary panel APIs.
    /// Triggers: panic buttons, duress codes, zone violations, smoke/CO/flood sensors.
    /// See IAlarmSystemPort for full protocol details.
    /// </summary>
    AlarmSystem
}

// ═══════════════════════════════════════════════════════════════
// IoT Alert Models
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Inbound alert request from an IoT device or platform.
/// Platform-agnostic — the controller normalizes each platform's webhook format
/// into this record before passing to the port.
/// </summary>
public record IoTAlertRequest(
    /// <summary>Which IoT platform originated this alert.</summary>
    IoTSource Source,

    /// <summary>
    /// The platform-specific user identifier.
    /// Alexa: "amzn1.ask.account.XXXX"
    /// Google: "google-uid-XXXX"
    /// SmartThings: "smartthings-user-XXXX"
    /// </summary>
    string ExternalUserId,

    /// <summary>
    /// How the alert was triggered.
    /// "VOICE_COMMAND", "PANIC_BUTTON", "MOTION_SENSOR", "DOOR_SENSOR",
    /// "SMOKE_DETECTOR", "CO_DETECTOR", "GLASS_BREAK", "WATER_LEAK",
    /// "TEMPERATURE_ALERT", "GEOFENCE_EXIT", "SCHEDULED_CHECK_IN_MISSED",
    /// "CAMERA_PERSON_DETECTION", "CUSTOM_ROUTINE",
    /// Landline: "LANDLINE_PHRASE_DETECTED", "DTMF_EMERGENCY_CODE", "LANDLINE_SPEAKERPHONE_PICKUP",
    ///           "LANDLINE_OFF_HOOK_TIMEOUT", "LANDLINE_SCHEDULED_CHECK_IN"
    /// Alarm:   "ALARM_PANIC_BUTTON", "ALARM_DURESS_CODE", "ALARM_ZONE_VIOLATION",
    ///           "ALARM_SMOKE", "ALARM_CO", "ALARM_FLOOD", "ALARM_MEDICAL",
    ///           "ALARM_FIRE", "ALARM_BURGLAR", "ALARM_HOLDUP", "ALARM_TAMPER",
    ///           "ALARM_AC_LOSS", "ALARM_LOW_BATTERY", "ALARM_COMMUNICATION_FAIL"
    /// </summary>
    string TriggerMethod,

    /// <summary>
    /// The type of device that triggered the alert.
    /// "ECHO_DOT", "NEST_HUB", "SMARTTHINGS_BUTTON", "RING_ALARM",
    /// "ZIGBEE_PANIC_BUTTON", "ZWAVE_SMOKE_DETECTOR",
    /// Landline: "ATA_OBIHAI", "ATA_GRANDSTREAM", "SIP_TRUNK_GATEWAY", "PSTN_MODEM",
    ///           "CORDLESS_PHONE_BASE", "WALL_PHONE", "DESK_PHONE_SPEAKERPHONE"
    /// Alarm:   "HONEYWELL_VISTA", "HONEYWELL_LYRIC", "DSC_POWERSERIES", "QOLSYS_IQ",
    ///           "ELK_M1", "ADEMCO_PANEL", "SIMPLISAFE_BASE", "VIVINT_PANEL",
    ///           "ADT_COMMAND", "RING_ALARM_BASE", "BRINKS_PANEL", "ALARM_COM_PANEL"
    /// </summary>
    string DeviceType,

    /// <summary>GPS coordinates of the device/dwelling, if known. Nullable for privacy.</summary>
    IoTLocation? Location,

    /// <summary>
    /// The emergency phrase spoken by the user, if voice-triggered.
    /// Null for sensor-based triggers (panic button, motion, smoke, etc.).
    /// </summary>
    string? EmergencyPhrase,

    /// <summary>
    /// The response scope to use for this alert.
    /// Maps to ResponseScope enum — "CheckIn", "SOS", "Evacuation", etc.
    /// </summary>
    ResponseScope Scope,

    /// <summary>UTC timestamp when the IoT platform received the trigger.</summary>
    DateTime Timestamp,

    /// <summary>Optional platform-specific request ID for idempotency and correlation.</summary>
    string? PlatformRequestId = null,

    /// <summary>Optional OAuth2 access token for the linked account.</summary>
    string? AccessToken = null,

    /// <summary>Optional additional metadata from the platform (JSON-serializable).</summary>
    IDictionary<string, string>? Metadata = null
);

/// <summary>GPS location from an IoT device or user dwelling registration.</summary>
public record IoTLocation(
    double Latitude,
    double Longitude,
    double? AccuracyMeters = null,
    string? Address = null,
    string? GeoHash = null
);

/// <summary>
/// Result of processing an IoT alert — returned to the IoT platform's webhook
/// so it can provide spoken/visual feedback to the user.
/// </summary>
public record IoTAlertResult(
    /// <summary>Internal alert ID for tracking.</summary>
    string AlertId,

    /// <summary>Correlation ID back to the platform's request.</summary>
    string RequestId,

    /// <summary>Processing status: "Dispatched", "UserNotMapped", "Throttled", "Error".</summary>
    IoTAlertStatus Status,

    /// <summary>Number of responders notified (0 if user not mapped or error).</summary>
    int RespondersNotified,

    /// <summary>
    /// Human-readable message for the IoT device to speak/display.
    /// Alexa: "Emergency alert sent. 5 responders have been notified."
    /// Google: "I've activated The Watch. Help is on the way."
    /// </summary>
    string Message,

    /// <summary>The internal TheWatch request ID if a response was created.</summary>
    string? ResponseRequestId = null,

    /// <summary>Estimated time for first responder arrival, if calculable.</summary>
    TimeSpan? EstimatedResponseTime = null
);

/// <summary>Status of an IoT alert after processing.</summary>
public enum IoTAlertStatus
{
    /// <summary>Alert accepted and responders dispatched.</summary>
    Dispatched,

    /// <summary>Alert accepted but pending user confirmation (two-step for sensor triggers).</summary>
    PendingConfirmation,

    /// <summary>External user ID not mapped to a TheWatch account.</summary>
    UserNotMapped,

    /// <summary>Alert throttled — too many alerts in short period (debounce).</summary>
    Throttled,

    /// <summary>Alert cancelled by user before dispatch completed.</summary>
    Cancelled,

    /// <summary>Processing error — platform will retry.</summary>
    Error
}

// ═══════════════════════════════════════════════════════════════
// IoT Check-In Models
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Check-in request from an IoT device. Used for scheduled well-being checks.
/// Example: "Alexa, tell The Watch I'm okay" or automatic daily check-in routine.
/// </summary>
public record IoTCheckInRequest(
    IoTSource Source,
    string ExternalUserId,

    /// <summary>"OK", "NEED_HELP", "FEELING_UNWELL", "MISSED" (auto-generated on timeout).</summary>
    IoTCheckInStatus Status,

    /// <summary>Optional message from the user ("I'm fine, just busy today").</summary>
    string? Message,

    /// <summary>Device location at time of check-in.</summary>
    IoTLocation? Location,

    /// <summary>Optional vital signs from health-monitoring IoT devices (heart rate, SpO2, etc.).</summary>
    IDictionary<string, string>? VitalSigns = null
);

/// <summary>Status of an IoT check-in.</summary>
public enum IoTCheckInStatus
{
    Ok,
    NeedHelp,
    FeelingUnwell,
    Missed
}

/// <summary>Result of processing a check-in from an IoT device.</summary>
public record IoTCheckInResult(
    string CheckInId,
    IoTCheckInResultStatus Status,

    /// <summary>Message to speak/display on the device.</summary>
    string Message,

    /// <summary>When the next check-in is expected.</summary>
    DateTime? NextCheckInDue = null
);

/// <summary>Processing result for a check-in.</summary>
public enum IoTCheckInResultStatus
{
    Recorded,
    EscalationTriggered,
    UserNotMapped,
    Error
}

// ═══════════════════════════════════════════════════════════════
// IoT Device Management Models
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Current status summary for a user's IoT devices — displayed on the dashboard.
/// </summary>
public record IoTDeviceStatus(
    string UserId,
    int ActiveAlerts,
    int NearbyResponders,
    DateTime? LastCheckIn,
    IReadOnlyList<IoTDeviceRegistration> RegisteredDevices,
    IReadOnlyList<IoTActiveAlertSummary> ActiveAlertDetails
);

/// <summary>Summary of an active alert for the device status view.</summary>
public record IoTActiveAlertSummary(
    string AlertId,
    IoTSource Source,
    string TriggerMethod,
    IoTAlertStatus Status,
    DateTime TriggeredAt
);

/// <summary>
/// Registration record for an IoT device linked to a TheWatch user.
/// Tracks device identity, capabilities, and health.
/// </summary>
public record IoTDeviceRegistration(
    string DeviceId,
    string UserId,
    IoTSource Source,
    string DeviceName,

    /// <summary>
    /// What this device can do. Determines which alerts it can trigger.
    /// "VOICE_COMMAND", "PANIC_BUTTON", "MOTION_SENSOR", "SMOKE_DETECTOR",
    /// "CAMERA", "DOOR_LOCK", "SIREN", "DISPLAY", "SPEAKER",
    /// Landline: "SPEAKERPHONE_LISTEN", "DTMF_INPUT", "CALLER_ID", "AUTO_ANSWER",
    ///           "OFF_HOOK_DETECT", "RING_DETECT", "AUDIO_PLAYBACK"
    /// Alarm:   "ALARM_PANIC", "ALARM_DURESS", "ALARM_ZONE_MONITOR", "ALARM_SMOKE_CO",
    ///           "ALARM_FLOOD", "ALARM_MEDICAL", "ALARM_SIREN", "ALARM_ARM_DISARM",
    ///           "ALARM_ENTRY_EXIT", "ALARM_TAMPER_DETECT", "ALARM_BATTERY_MONITOR"
    /// </summary>
    IReadOnlyList<string> Capabilities,

    DateTime RegisteredAt,
    DateTime LastSeenAt,

    /// <summary>Device firmware version, if reported by the platform.</summary>
    string? FirmwareVersion = null,

    /// <summary>Whether the device is currently online and responsive.</summary>
    bool IsOnline = true,

    /// <summary>Battery level percentage (0-100), null if mains-powered.</summary>
    int? BatteryLevel = null,

    /// <summary>Location where this device is installed (kitchen, front door, etc.).</summary>
    string? InstallationZone = null
);

// ═══════════════════════════════════════════════════════════════
// IoT User Mapping Models
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Maps an external IoT platform user ID to an internal TheWatch user ID.
/// Created during OAuth2 account linking when the user authorizes TheWatch
/// in their Alexa/Google Home/SmartThings app.
/// </summary>
public record IoTUserMapping(
    IoTSource Source,

    /// <summary>Platform-specific user ID (e.g., "amzn1.ask.account.XXXX").</summary>
    string ExternalUserId,

    /// <summary>Internal TheWatch user ID.</summary>
    string TheWatchUserId,

    /// <summary>OAuth2 access token for API calls back to the platform (e.g., proactive events).</summary>
    string? AccessToken = null,

    /// <summary>OAuth2 refresh token for token rotation.</summary>
    string? RefreshToken = null,

    /// <summary>When the access token expires.</summary>
    DateTime? TokenExpiresAt = null,

    /// <summary>When this mapping was created.</summary>
    DateTime LinkedAt = default,

    /// <summary>When the user last used this platform to interact with TheWatch.</summary>
    DateTime? LastUsedAt = null
);

// ═══════════════════════════════════════════════════════════════
// Port Interface
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Port for IoT alert ingestion, device management, and user mapping.
/// Adapters: MockIoTAlertAdapter (dev), AlexaAdapter + GoogleHomeAdapter + SmartThingsAdapter
///           + TelephonyAdapter (landline/PSTN) + AlarmSystemAdapter (alarm panels) (prod).
///
/// Production adapters will:
///   - Validate platform-specific signatures (Alexa request signing, Google JWT, SmartThings HMAC)
///   - Handle OAuth2 token refresh for proactive event APIs
///   - Implement platform-specific rate limiting and retry policies
///   - Persist device registrations and user mappings to durable storage
/// </summary>
public interface IIoTAlertPort
{
    // ── Alert Ingestion ──────────────────────────────────────────

    /// <summary>
    /// Process an inbound alert from any IoT platform.
    /// Maps external user → TheWatch user, validates, and dispatches.
    /// </summary>
    Task<IoTAlertResult> TriggerIoTAlertAsync(
        IoTAlertRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Process a check-in from an IoT device (daily wellness check, "I'm okay" voice command).
    /// </summary>
    Task<IoTCheckInResult> ProcessIoTCheckInAsync(
        IoTCheckInRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Get the current IoT device status for a user (active alerts, devices, last check-in).
    /// </summary>
    Task<IoTDeviceStatus> GetIoTDeviceStatusAsync(
        string userId,
        CancellationToken ct = default);

    /// <summary>
    /// Cancel an active IoT-originated alert.
    /// Called when user says "Alexa, tell The Watch I'm okay" or presses cancel on device.
    /// </summary>
    Task<IoTAlertResult> CancelIoTAlertAsync(
        string alertId,
        string reason,
        CancellationToken ct = default);

    // ── Device Management ────────────────────────────────────────

    /// <summary>
    /// Register a new IoT device for a user. Called during device setup or discovery.
    /// </summary>
    Task<IoTDeviceRegistration> RegisterIoTDeviceAsync(
        IoTDeviceRegistration registration,
        CancellationToken ct = default);

    /// <summary>
    /// Unregister an IoT device. Called when user removes device from their account.
    /// </summary>
    Task<bool> UnregisterIoTDeviceAsync(
        string deviceId,
        CancellationToken ct = default);

    /// <summary>
    /// Get all registered IoT devices for a user.
    /// </summary>
    Task<IReadOnlyList<IoTDeviceRegistration>> GetRegisteredDevicesAsync(
        string userId,
        CancellationToken ct = default);

    // ── User Mapping ─────────────────────────────────────────────

    /// <summary>
    /// Create or update a mapping between an external IoT platform user and a TheWatch user.
    /// Called during OAuth2 account linking flow.
    /// </summary>
    Task<IoTUserMapping> MapExternalUserAsync(
        IoTUserMapping mapping,
        CancellationToken ct = default);

    /// <summary>
    /// Resolve an external user ID to a TheWatch user ID.
    /// Returns null if no mapping exists (user hasn't linked their account).
    /// </summary>
    Task<IoTUserMapping?> ResolveExternalUserAsync(
        IoTSource source,
        string externalUserId,
        CancellationToken ct = default);

    /// <summary>
    /// Revoke a user mapping (unlink IoT account). Invalidates tokens and removes mapping.
    /// </summary>
    Task<bool> RevokeExternalUserMappingAsync(
        IoTSource source,
        string externalUserId,
        CancellationToken ct = default);

    /// <summary>
    /// Get all IoT platform mappings for a TheWatch user.
    /// Used by the settings screen to show linked accounts.
    /// </summary>
    Task<IReadOnlyList<IoTUserMapping>> GetUserMappingsAsync(
        string theWatchUserId,
        CancellationToken ct = default);
}
