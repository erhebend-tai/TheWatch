// =============================================================================
// IAlarmSystemPort — port interfaces for residential/commercial alarm system integration.
// =============================================================================
// Enables TheWatch to receive alerts from professional and DIY alarm systems,
// creating a bridge between the alarm industry's monitoring infrastructure and
// TheWatch's community-based volunteer response network.
//
// Why this matters:
//   - ~40 million US homes have alarm systems (Security Industry Association, 2024)
//   - Average police response time to alarm dispatch: 7-10 minutes (FBI UCR)
//   - Average neighbor/volunteer response time with TheWatch: 2-4 minutes
//   - Alarm duress codes are the #1 underused safety feature — TheWatch makes them actionable
//   - Smoke/CO/flood sensors in alarm panels can trigger evacuation scope automatically
//   - Alarm panels work during power outages (battery backup) and cell outages (POTS backup)
//
// Supported Alarm Platforms:
//   ┌──────────────────────────────────────────────────────────────────────────┐
//   │ Platform         │ Protocol        │ API                │ Market Share  │
//   ├──────────────────┼─────────────────┼────────────────────┼───────────────┤
//   │ Alarm.com        │ REST API        │ Partner API v4     │ ~30% (7.6M)   │
//   │ ADT              │ Alarm.com API   │ (via Alarm.com)    │ ~25% (6.5M)   │
//   │ Honeywell Total  │ REST API        │ TotalConnect 2.0   │ ~15%          │
//   │   Connect        │                 │                    │               │
//   │ SimpliSafe       │ REST API        │ SimpliSafe Web API │ ~10% (3.9M)   │
//   │ Vivint           │ REST API        │ Vivint Public API  │ ~7% (2M)      │
//   │ Ring Alarm       │ REST API        │ Ring API           │ ~8%           │
//   │ Brinks Home      │ Alarm.com API   │ (via Alarm.com)    │ ~3%           │
//   │ Abode            │ REST API        │ Abode API          │ <1%           │
//   │ Frontpoint       │ Alarm.com API   │ (via Alarm.com)    │ ~2%           │
//   │ Cove             │ REST API        │ Cove API           │ <1%           │
//   │ DIY (DSC, Elk,   │ Contact ID /    │ Direct serial/IP   │ ~5% (custom)  │
//   │  Qolsys, HAI)    │ SIA DC-07      │                    │               │
//   └──────────────────────────────────────────────────────────────────────────┘
//
// Alarm Industry Protocols:
//   - Contact ID (Ademco CID, SIA DC-05-1999): THE standard for alarm panel → central station
//     communication over PSTN. 16-digit event code format: ACCT MT QEEE GG ZZZ C
//     (account, message type, event qualifier, event code, group, zone, checksum).
//     TheWatch can receive Contact ID by acting as a secondary receiver or by integrating
//     with an IP alarm receiver (e.g., DSC Sur-Gard, Honeywell 7810iR).
//
//   - SIA DC-07 (IP-based alarm monitoring): Contact ID's modern successor.
//     Alarm panels transmit over TCP/IP (encrypted AES-128/256) instead of PSTN.
//     Supports: heartbeat supervision, event acknowledgment, encryption, authentication.
//     Most modern alarm communicators support SIA DC-07 (Honeywell IP-GSM, DSC TL280).
//
//   - SIA DC-09 (Internet Protocol Event Reporting): Extension of DC-07 for
//     internet-native alarm reporting with JSON/XML event payloads.
//
//   - Z-Wave / Zigbee: Many alarm panels (Qolsys IQ Panel, Ring Alarm, SmartThings)
//     use Z-Wave or Zigbee sensors. TheWatch can bridge these via existing IoTSource
//     integrations (ZigbeeDirect, ZWaveDirect) when the panel doesn't have a cloud API.
//
// Contact ID Event Codes (Ademco — key subset for TheWatch):
//   100 = Medical alarm              110 = Fire alarm
//   120 = Panic alarm                121 = Duress (silent panic under coercion)
//   122 = Silent panic               130 = Burglar alarm
//   131 = Perimeter alarm            132 = Interior alarm
//   133 = 24-hour sensor alarm       134 = Entry/exit alarm
//   150 = 24-hour non-burglar        151 = Gas detected
//   154 = Water leak                 158 = High temperature
//   159 = Low temperature            162 = CO detected
//   301 = AC power loss              302 = Low battery
//   350 = Communication failure      380 = Sensor trouble
//   401 = Arm/disarm by user         403 = Automatic arm
//   570 = Zone bypass                602 = Periodic test
//   623 = Event log 80% full         625 = System reset
//
// Example: ADT alarm triggers via Alarm.com
//   1. User's ADT panel detects burglar (Contact ID code 130)
//   2. ADT panel → ADT central station (Contact ID over PSTN or SIA DC-07 over IP)
//   3. ADT central station → Alarm.com cloud (API event)
//   4. Alarm.com webhook → TheWatch /api/iot/alert { source: AlarmSystem,
//        triggerMethod: "ALARM_BURGLAR", deviceType: "ADT_COMMAND", ... }
//   5. TheWatch dispatches volunteers + 911 per user's escalation policy
//
// Example: Honeywell panel duress code
//   1. User enters code+1 (duress code) on Honeywell Vista keypad while being coerced
//   2. Panel sends Contact ID code 121 (duress) to central station
//   3. Central station → Alarm.com/TotalConnect webhook → TheWatch
//   4. TheWatch triggers SilentDuress scope — trusted contacts notified silently
//
// Example: SimpliSafe smoke sensor
//   1. SimpliSafe smoke sensor triggers (Contact ID code 110)
//   2. SimpliSafe cloud → TheWatch webhook
//   3. TheWatch triggers Neighborhood scope with fire-specific dispatch
//   4. Nearby certified EMT/firefighter volunteers notified first
//
// Example: DIY DSC panel via IP receiver
//   1. DSC PowerSeries panel with TL280 IP communicator
//   2. Panel sends SIA DC-07 encrypted event to TheWatch's SIA receiver endpoint
//   3. TheWatch decrypts, parses Contact ID payload, maps to IoTAlertRequest
//   4. Standard alert pipeline processes the event
//
// WAL (Write-Ahead Log):
//   - All alarm events are logged BEFORE processing (crash recovery)
//   - Contact ID raw packets are stored for forensic/legal purposes
//   - Duress events are NEVER logged in user-visible history (coercion protection)
//   - Zone maps are encrypted at rest (reveals home layout)
// =============================================================================

namespace TheWatch.Shared.Domain.Ports;

// ═══════════════════════════════════════════════════════════════
// Alarm Platform Enumeration
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// The alarm system platform/manufacturer.
/// Determines which API adapter to use for communication.
/// </summary>
public enum AlarmPlatform
{
    /// <summary>Alarm.com — largest alarm cloud platform. Covers ADT, Brinks, Frontpoint, and many others.</summary>
    AlarmDotCom,

    /// <summary>Honeywell Total Connect 2.0 — Honeywell/Resideo alarm cloud.</summary>
    HoneywellTotalConnect,

    /// <summary>SimpliSafe — popular DIY alarm system with cloud API.</summary>
    SimpliSafe,

    /// <summary>Vivint — smart home + alarm with cloud API.</summary>
    Vivint,

    /// <summary>Ring Alarm — Amazon Ring's alarm system, integrates with Alexa.</summary>
    RingAlarm,

    /// <summary>Abode — DIY alarm with HomeKit, Z-Wave, Zigbee support.</summary>
    Abode,

    /// <summary>Cove — affordable DIY alarm with cellular monitoring.</summary>
    Cove,

    /// <summary>
    /// Direct panel connection — no cloud platform. TheWatch communicates directly
    /// with the alarm panel via Contact ID (PSTN), SIA DC-07 (IP), or serial.
    /// Used for: DSC PowerSeries, Elk M1, Qolsys IQ, HAI/Leviton OmniPro,
    /// Honeywell Vista (without TotalConnect), Paradox, Bosch, DMP.
    /// </summary>
    DirectPanel,

    /// <summary>Home Assistant alarm integration — bridges HA's alarm_control_panel entity.</summary>
    HomeAssistant,

    /// <summary>Hubitat — bridges Hubitat's HSM (Hubitat Safety Monitor).</summary>
    Hubitat
}

/// <summary>
/// The type of alarm panel hardware.
/// </summary>
public enum AlarmPanelType
{
    // ── Honeywell / Resideo ───────────────────
    /// <summary>Honeywell Vista 20P/21iP — most installed wired panel in North America.</summary>
    HoneywellVista,
    /// <summary>Honeywell Lyric — wireless successor to Vista, Z-Wave + WiFi.</summary>
    HoneywellLyric,
    /// <summary>Honeywell Tuxedo — touchscreen keypad with panel integration.</summary>
    HoneywellTuxedo,
    /// <summary>Resideo ProSeries — latest Honeywell/Resideo panel line.</summary>
    ResideoProSeries,

    // ── DSC ───────────────────────────────────
    /// <summary>DSC PowerSeries (PC1616/PC1832/PC1864) — popular wired panel.</summary>
    DscPowerSeries,
    /// <summary>DSC PowerSeries Neo — modern hybrid wired/wireless panel.</summary>
    DscPowerSeriesNeo,

    // ── Qolsys ────────────────────────────────
    /// <summary>Qolsys IQ Panel 2/4 — touchscreen all-in-one, popular with Alarm.com dealers.</summary>
    QolsysIq,

    // ── Elk ───────────────────────────────────
    /// <summary>Elk M1 Gold/EZ8 — automation-grade panel, popular in custom installs.</summary>
    ElkM1,

    // ── Other Panels ──────────────────────────
    /// <summary>Bosch B/G Series — commercial-grade intrusion panels.</summary>
    BoschBSeries,
    /// <summary>DMP XR Series — dealer-installed commercial/residential panel.</summary>
    DmpXr,
    /// <summary>Paradox EVO/SP — popular in international markets.</summary>
    ParadoxEvo,
    /// <summary>HAI/Leviton OmniPro — automation-focused panel (legacy but installed base).</summary>
    HaiOmniPro,
    /// <summary>2GIG GC3/GC3e — popular with Alarm.com dealers.</summary>
    TwoGigGc3,

    // ── DIY / Consumer ────────────────────────
    /// <summary>SimpliSafe base station.</summary>
    SimpliSafeBase,
    /// <summary>Ring Alarm base station.</summary>
    RingAlarmBase,
    /// <summary>Abode Gateway.</summary>
    AbodeGateway,
    /// <summary>Cove panel.</summary>
    CovePanel,

    /// <summary>Generic/unknown panel type. User provides protocol details manually.</summary>
    Generic
}

// ═══════════════════════════════════════════════════════════════
// Alarm Event Models
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Contact ID event code category — maps Ademco 3-digit codes to TheWatch event types.
/// </summary>
public enum AlarmEventCategory
{
    /// <summary>Code 100 — Medical alarm (pendant, wrist button, medical panic).</summary>
    Medical,

    /// <summary>Code 110 — Fire alarm (smoke detector, heat detector, manual pull station).</summary>
    Fire,

    /// <summary>Code 120 — Panic alarm (keypad panic button, key-fob panic).</summary>
    Panic,

    /// <summary>Code 121 — Duress alarm (user entered duress code while coerced).</summary>
    Duress,

    /// <summary>Code 122 — Silent panic (no siren, silent notification to monitoring).</summary>
    SilentPanic,

    /// <summary>Codes 130-134 — Burglar/intrusion alarm (perimeter, interior, entry/exit).</summary>
    Burglar,

    /// <summary>Code 151 — Gas detected (natural gas, propane leak sensor).</summary>
    Gas,

    /// <summary>Code 154 — Water leak detected.</summary>
    WaterLeak,

    /// <summary>Code 158/159 — Temperature out of range (freeze warning, heat alert).</summary>
    Temperature,

    /// <summary>Code 162 — Carbon monoxide detected.</summary>
    CarbonMonoxide,

    /// <summary>Codes 133, 150 — 24-hour sensor alarm (tamper, environmental).</summary>
    TwentyFourHourSensor,

    /// <summary>Code 301 — AC power loss (panel on battery backup).</summary>
    AcPowerLoss,

    /// <summary>Code 302 — Low battery (panel or sensor).</summary>
    LowBattery,

    /// <summary>Code 350 — Communication failure (panel can't reach central station).</summary>
    CommunicationFailure,

    /// <summary>Code 380 — Sensor trouble (offline, tampered, low signal).</summary>
    SensorTrouble,

    /// <summary>Code 401/403 — Arm/disarm event (user or automatic).</summary>
    ArmDisarm,

    /// <summary>Code 570 — Zone bypass (user deliberately bypassed a zone).</summary>
    ZoneBypass,

    /// <summary>Code 602 — Periodic test signal (panel health heartbeat).</summary>
    PeriodicTest,

    /// <summary>Glass break sensor triggered.</summary>
    GlassBreak,

    /// <summary>Any event code not in the above categories.</summary>
    Other
}

/// <summary>
/// An alarm event received from an alarm panel or monitoring platform.
/// Normalized from Contact ID, SIA DC-07, or platform API webhooks.
/// </summary>
public record AlarmEvent(
    string EventId,
    string UserId,
    string PanelId,
    AlarmPlatform Platform,

    /// <summary>The category of alarm event.</summary>
    AlarmEventCategory Category,

    /// <summary>
    /// Raw Contact ID event code (3 digits, e.g., "130" for burglar).
    /// Null if the event came from a platform API that doesn't expose raw codes.
    /// </summary>
    string? ContactIdCode,

    /// <summary>
    /// The zone number that triggered the event (1-based).
    /// Zone maps to a physical sensor location (e.g., zone 1 = front door, zone 5 = basement motion).
    /// </summary>
    int? ZoneNumber,

    /// <summary>
    /// User-assigned zone name (e.g., "Front Door", "Master Bedroom Motion", "Kitchen Smoke").
    /// </summary>
    string? ZoneName,

    /// <summary>
    /// The alarm partition (1-based). Multi-partition panels can have independent arm/disarm.
    /// Partition 1 is typically the main house, partition 2 might be a garage or guest suite.
    /// </summary>
    int Partition,

    /// <summary>
    /// The user code slot used (if arm/disarm or duress). Identifies WHICH user performed the action.
    /// Code slot 1 is typically the master code, subsequent slots are user codes.
    /// Duress is often the user's code + 1 (e.g., if code is 1234, duress is 1235).
    /// </summary>
    int? UserCodeSlot,

    /// <summary>
    /// Whether the event is a new alarm (true) or a restore/clear (false).
    /// Contact ID uses qualifier 1=new event, 3=restore.
    /// </summary>
    bool IsNewEvent,

    /// <summary>
    /// The ResponseScope that should be used for this alarm event.
    /// Mapped by AlarmEventToScopeMapper based on event category.
    /// </summary>
    ResponseScope RecommendedScope,

    /// <summary>GPS coordinates of the alarm panel installation.</summary>
    IoTLocation? Location,

    /// <summary>When the alarm panel generated the event.</summary>
    DateTime PanelTimestamp,

    /// <summary>When TheWatch received the event.</summary>
    DateTime ReceivedAt,

    /// <summary>
    /// Raw protocol data for forensic logging.
    /// Contact ID: "1234 18 1130 01 015 5" (acct, MT, event, group, zone, checksum)
    /// SIA DC-07: hex-encoded encrypted packet
    /// Platform API: JSON webhook body
    /// </summary>
    string? RawProtocolData,

    /// <summary>Additional metadata from the platform (JSON-serializable).</summary>
    IDictionary<string, string>? Metadata = null
);

// ═══════════════════════════════════════════════════════════════
// Alarm Panel Registration
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Registration record for an alarm panel linked to a TheWatch user.
/// </summary>
public record AlarmPanelRegistration(
    string PanelId,
    string UserId,
    AlarmPlatform Platform,
    AlarmPanelType PanelType,

    /// <summary>User-friendly name (e.g., "Home Alarm", "Office Security").</summary>
    string PanelName,

    /// <summary>
    /// Alarm.com / platform account ID for API access.
    /// </summary>
    string? PlatformAccountId,

    /// <summary>
    /// Contact ID account number (4 digits, assigned by monitoring company).
    /// Used to identify this panel in Contact ID transmissions.
    /// </summary>
    string? ContactIdAccount,

    /// <summary>
    /// SIA DC-07 account/line number for IP-based panels.
    /// </summary>
    string? SiaAccountNumber,

    /// <summary>GPS location of the panel installation.</summary>
    IoTLocation? Location,

    /// <summary>
    /// Zone map — maps zone numbers to human-readable names and sensor types.
    /// Key = zone number, Value = (name, sensor type).
    /// Example: { 1: ("Front Door", "DOOR_CONTACT"), 5: ("Basement", "MOTION") }
    /// Encrypted at rest — reveals home layout.
    /// </summary>
    IReadOnlyDictionary<int, AlarmZoneInfo>? ZoneMap,

    /// <summary>Number of partitions on this panel.</summary>
    int PartitionCount,

    /// <summary>Whether the panel has a duress code configured.</summary>
    bool DuressCodeConfigured,

    /// <summary>Whether the panel supports two-way voice (talk-down from monitoring).</summary>
    bool SupportsTwoWayVoice,

    /// <summary>Whether the panel has cellular backup (keeps working if internet is down).</summary>
    bool HasCellularBackup,

    /// <summary>Whether the panel has battery backup (keeps working during power outages).</summary>
    bool HasBatteryBackup,

    /// <summary>Battery backup duration in hours (typical: 4-24 hours).</summary>
    int? BatteryBackupHours,

    DateTime RegisteredAt,
    DateTime LastHeartbeat,
    bool IsOnline
);

/// <summary>
/// Information about a single alarm zone (sensor location).
/// </summary>
public record AlarmZoneInfo(
    /// <summary>Human-readable zone name (e.g., "Front Door", "Kitchen Smoke Detector").</summary>
    string Name,

    /// <summary>
    /// Sensor type: "DOOR_CONTACT", "WINDOW_CONTACT", "MOTION", "GLASS_BREAK",
    /// "SMOKE", "CO", "HEAT", "FLOOD", "FREEZE", "GAS", "MEDICAL_PENDANT",
    /// "PANIC_BUTTON", "KEY_FOB", "TILT_SENSOR" (garage door), "SHOCK_SENSOR"
    /// </summary>
    string SensorType,

    /// <summary>Room/area where the sensor is installed.</summary>
    string? Room,

    /// <summary>Whether this zone is currently bypassed by the user.</summary>
    bool IsBypassed
);

// ═══════════════════════════════════════════════════════════════
// Alarm-to-Scope Mapping
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Maps alarm event categories to TheWatch response scopes.
/// Deterministic mapping — same event always produces same scope recommendation.
/// Users can override these defaults in their alarm panel settings.
/// </summary>
public static class AlarmEventToScopeMapper
{
    public static ResponseScope MapToScope(AlarmEventCategory category) => category switch
    {
        // Life-safety events → immediate high-scope response
        AlarmEventCategory.Medical => ResponseScope.Neighborhood,
        AlarmEventCategory.Fire => ResponseScope.Neighborhood,
        AlarmEventCategory.CarbonMonoxide => ResponseScope.Neighborhood,
        AlarmEventCategory.Gas => ResponseScope.Evacuation,

        // Panic/duress → depends on type
        AlarmEventCategory.Panic => ResponseScope.Neighborhood,
        AlarmEventCategory.Duress => ResponseScope.SilentDuress,
        AlarmEventCategory.SilentPanic => ResponseScope.SilentDuress,

        // Property events → check-in level
        AlarmEventCategory.Burglar => ResponseScope.Neighborhood,
        AlarmEventCategory.GlassBreak => ResponseScope.Neighborhood,
        AlarmEventCategory.WaterLeak => ResponseScope.CheckIn,
        AlarmEventCategory.Temperature => ResponseScope.CheckIn,

        // System events → informational, no dispatch
        AlarmEventCategory.AcPowerLoss => ResponseScope.CheckIn,
        AlarmEventCategory.LowBattery => ResponseScope.Custom, // notification only
        AlarmEventCategory.CommunicationFailure => ResponseScope.Custom,
        AlarmEventCategory.SensorTrouble => ResponseScope.Custom,
        AlarmEventCategory.ArmDisarm => ResponseScope.Custom, // log only
        AlarmEventCategory.ZoneBypass => ResponseScope.Custom, // log only
        AlarmEventCategory.PeriodicTest => ResponseScope.Custom, // heartbeat, no dispatch
        AlarmEventCategory.TwentyFourHourSensor => ResponseScope.CheckIn,
        AlarmEventCategory.Other => ResponseScope.Custom,

        _ => ResponseScope.CheckIn
    };

    /// <summary>
    /// Determine the escalation policy for an alarm event category.
    /// </summary>
    public static EscalationPolicy MapToEscalation(AlarmEventCategory category) => category switch
    {
        AlarmEventCategory.Medical => EscalationPolicy.Immediate911,
        AlarmEventCategory.Fire => EscalationPolicy.Immediate911,
        AlarmEventCategory.CarbonMonoxide => EscalationPolicy.Immediate911,
        AlarmEventCategory.Gas => EscalationPolicy.Immediate911,
        AlarmEventCategory.Panic => EscalationPolicy.TimedEscalation,
        AlarmEventCategory.Duress => EscalationPolicy.Conditional911,
        AlarmEventCategory.SilentPanic => EscalationPolicy.Conditional911,
        AlarmEventCategory.Burglar => EscalationPolicy.TimedEscalation,
        AlarmEventCategory.GlassBreak => EscalationPolicy.TimedEscalation,
        _ => EscalationPolicy.Manual
    };
}

// ═══════════════════════════════════════════════════════════════
// Port Interface
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Port for alarm system integration — panel registration, event processing,
/// zone management, and alarm platform account linking.
///
/// Adapters:
///   - MockAlarmSystemAdapter (dev/test)
///   - AlarmDotComAdapter (prod — covers ADT, Brinks, Frontpoint, Qolsys, 2GIG)
///   - HoneywellTotalConnectAdapter (prod — Honeywell Vista, Lyric, Tuxedo, Resideo)
///   - SimpliSafeAdapter (prod — SimpliSafe Web API)
///   - VivintAdapter (prod — Vivint cloud API)
///   - RingAlarmAdapter (prod — Ring API, also bridges to Alexa IoTSource)
///   - ContactIdReceiverAdapter (prod — direct Contact ID over IP/PSTN for DIY panels)
///   - SiaDc07ReceiverAdapter (prod — SIA DC-07 encrypted IP receiver)
///
/// Integration with IIoTAlertPort:
///   Alarm events flow through the standard IoT alert pipeline:
///   1. Alarm platform webhook or Contact ID receiver → IAlarmSystemPort.ProcessEventAsync()
///   2. IAlarmSystemPort maps alarm event → IoTAlertRequest { source: AlarmSystem }
///   3. IIoTAlertPort.TriggerIoTAlertAsync() processes the normalized alert
///   4. Standard response coordination pipeline takes over
/// </summary>
public interface IAlarmSystemPort
{
    // ── Panel Registration ──────────────────────────────────────

    /// <summary>Register an alarm panel for a user.</summary>
    Task<AlarmPanelRegistration> RegisterPanelAsync(
        AlarmPanelRegistration registration,
        CancellationToken ct = default);

    /// <summary>Unregister an alarm panel.</summary>
    Task<bool> UnregisterPanelAsync(
        string panelId,
        CancellationToken ct = default);

    /// <summary>Get all registered alarm panels for a user.</summary>
    Task<IReadOnlyList<AlarmPanelRegistration>> GetPanelsAsync(
        string userId,
        CancellationToken ct = default);

    /// <summary>Update alarm panel registration (zone map, capabilities, etc.).</summary>
    Task<AlarmPanelRegistration> UpdatePanelAsync(
        AlarmPanelRegistration registration,
        CancellationToken ct = default);

    // ── Event Processing ────────────────────────────────────────

    /// <summary>
    /// Process an inbound alarm event from any platform or protocol.
    /// Maps the event to the appropriate ResponseScope and triggers the alert pipeline.
    /// Returns the IoTAlertResult from the downstream IIoTAlertPort.
    /// </summary>
    Task<IoTAlertResult> ProcessEventAsync(
        AlarmEvent alarmEvent,
        CancellationToken ct = default);

    /// <summary>
    /// Process a raw Contact ID packet received over IP or PSTN.
    /// Parses the 16-digit Contact ID format, maps to AlarmEvent, and processes.
    /// Format: ACCT MT QEEE GG ZZZ C
    ///   ACCT = 4-digit account number
    ///   MT   = message type (18 = Contact ID)
    ///   Q    = event qualifier (1=new, 3=restore, 6=status)
    ///   EEE  = 3-digit event code (see AlarmEventCategory mapping)
    ///   GG   = group/partition (2 digits)
    ///   ZZZ  = zone/user number (3 digits)
    ///   C    = checksum
    /// </summary>
    Task<IoTAlertResult> ProcessContactIdAsync(
        string rawContactIdPacket,
        string sourceIpOrPhone,
        CancellationToken ct = default);

    /// <summary>
    /// Process a raw SIA DC-07 encrypted packet received over TCP/IP.
    /// Decrypts (AES-128/256), parses, maps to AlarmEvent, and processes.
    /// </summary>
    Task<IoTAlertResult> ProcessSiaDc07Async(
        byte[] encryptedPacket,
        string sourceIp,
        int sourcePort,
        CancellationToken ct = default);

    /// <summary>
    /// Get recent alarm events for a user (audit trail / history).
    /// Duress events are EXCLUDED from this list (coercion protection).
    /// </summary>
    Task<IReadOnlyList<AlarmEvent>> GetRecentEventsAsync(
        string userId,
        int maxResults = 50,
        CancellationToken ct = default);

    // ── Zone Management ─────────────────────────────────────────

    /// <summary>
    /// Update the zone map for a panel (maps zone numbers to names and sensor types).
    /// Can be auto-discovered from some platforms (Alarm.com, SimpliSafe) or manually entered.
    /// </summary>
    Task<bool> UpdateZoneMapAsync(
        string panelId,
        IReadOnlyDictionary<int, AlarmZoneInfo> zoneMap,
        CancellationToken ct = default);

    /// <summary>
    /// Auto-discover zones from the alarm platform API (if supported).
    /// Pulls zone names, types, and current status from Alarm.com/SimpliSafe/etc.
    /// </summary>
    Task<IReadOnlyDictionary<int, AlarmZoneInfo>?> DiscoverZonesAsync(
        string panelId,
        CancellationToken ct = default);

    // ── Platform Account Linking ────────────────────────────────

    /// <summary>
    /// Link a user's alarm platform account (Alarm.com, TotalConnect, SimpliSafe, etc.)
    /// via OAuth2 or API key. Enables webhook delivery and zone discovery.
    /// </summary>
    Task<bool> LinkPlatformAccountAsync(
        string userId,
        AlarmPlatform platform,
        string? oauthCode,
        string? apiKey,
        string? redirectUri,
        CancellationToken ct = default);

    /// <summary>Unlink a platform account. Stops webhook delivery for that platform.</summary>
    Task<bool> UnlinkPlatformAccountAsync(
        string userId,
        AlarmPlatform platform,
        CancellationToken ct = default);

    /// <summary>
    /// Get the current arm/disarm status of a panel (if the platform API supports it).
    /// </summary>
    Task<AlarmPanelStatus?> GetPanelStatusAsync(
        string panelId,
        CancellationToken ct = default);
}

/// <summary>
/// Current operational status of an alarm panel.
/// </summary>
public record AlarmPanelStatus(
    string PanelId,
    AlarmArmState ArmState,
    bool HasActiveAlarm,
    int OpenZones,
    int BypassedZones,
    bool OnBatteryBackup,
    int? BatteryPercentage,
    bool CellularBackupActive,
    DateTime LastCommunication
);

/// <summary>
/// Arm/disarm state of an alarm panel.
/// </summary>
public enum AlarmArmState
{
    /// <summary>System is disarmed — no monitoring active.</summary>
    Disarmed,

    /// <summary>Armed Away — all zones active (perimeter + interior + motion).</summary>
    ArmedAway,

    /// <summary>Armed Stay — perimeter zones active, interior motion bypassed.</summary>
    ArmedStay,

    /// <summary>Armed Night — perimeter + select interior zones (varies by panel).</summary>
    ArmedNight,

    /// <summary>Armed Instant — armed stay with no entry delay (instant alarm on any zone).</summary>
    ArmedInstant,

    /// <summary>Entry delay in progress — user has N seconds to disarm.</summary>
    EntryDelay,

    /// <summary>Exit delay in progress — user has N seconds to leave.</summary>
    ExitDelay,

    /// <summary>Alarm is currently sounding.</summary>
    Alarming,

    /// <summary>Panel status unknown (communication failure or unsupported query).</summary>
    Unknown
}
