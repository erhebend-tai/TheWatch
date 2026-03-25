// ThreatModels.cs — domain models for threat tracking, violence detection, acoustic analysis,
// sensor integration, safe harbor routing, stealth mode, and the DOJ/Johns Hopkins Lethality
// Assessment Protocol (LAP).
//
// This file provides the complete data model for:
//   - Real-time threat source tracking with position history
//   - Acoustic event classification (MIL-STD-1474E gunshot detection, UL 639 glass break)
//   - Door sensor integration (Z-Wave/Zigbee forced entry detection)
//   - Glass break sensor readings (UL 639 frequency/pattern analysis)
//   - Egress route blocking computation (which exits are cut off by the threat)
//   - Safe harbor discovery (nearest safe rooms, excluding locations known to DV perpetrators)
//   - Stealth mode for DV victims (silent notifications, screen dimming, duress codes)
//   - 11-question Lethality Assessment Protocol for DV risk scoring
//
// Example — report a threat:
//   var threat = new ThreatSource
//   {
//       Type = ThreatType.Intruder,
//       ArmedStatus = ThreatArmedStatus.Edged,
//       DetectionMethod = ThreatDetectionMethod.DoorSensor,
//       Confidence = 0.85f,
//       Latitude = 38.8977,
//       Longitude = -77.0365,
//       IsActivelyViolent = false
//   };
//   var tracked = await threatPort.ReportThreatAsync(threat);
//
// Example — assess DV lethality:
//   var answers = new[] {
//       new LAPAnswer { QuestionNumber = 1, Answer = true, Weight = 4 },
//       new LAPAnswer { QuestionNumber = 2, Answer = true, Weight = 1 },
//       // ... all 11 questions
//   };
//   int score = await threatPort.AssessLethalityAsync(threatId, answers);
//   // score >= 7 => "high danger" per Maryland Model protocol

using TheWatch.Shared.Enums;

namespace TheWatch.Shared.Domain.Models;

/// <summary>
/// A tracked threat source with real-time position, classification, and armed status.
/// Central entity for the threat tracking subsystem — every sensor event, acoustic detection,
/// and user report creates or updates a ThreatSource.
/// </summary>
public class ThreatSource
{
    /// <summary>Unique identifier for this threat (UUID).</summary>
    public string ThreatId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Classification of the threat type (ActiveShooter, DomesticViolence, Intruder, etc.).</summary>
    public ThreatType Type { get; set; } = ThreatType.Unknown;

    /// <summary>Mobility classification — stationary, slow, fast, or vehicular.</summary>
    public ThreatMobility Mobility { get; set; } = ThreatMobility.Unknown;

    /// <summary>Armed status — what type of weapon (if any) the threat possesses.</summary>
    public ThreatArmedStatus ArmedStatus { get; set; } = ThreatArmedStatus.Unknown;

    /// <summary>How the threat was initially detected (sensor, visual, user report, etc.).</summary>
    public ThreatDetectionMethod DetectionMethod { get; set; } = ThreatDetectionMethod.UserReported;

    /// <summary>
    /// Confidence score for this threat classification (0.0 = no confidence, 1.0 = certain).
    /// Sensor fusion increases confidence; single-sensor detections start lower.
    /// </summary>
    public float Confidence { get; set; }

    /// <summary>Last known latitude of the threat (WGS 84).</summary>
    public double Latitude { get; set; }

    /// <summary>Last known longitude of the threat (WGS 84).</summary>
    public double Longitude { get; set; }

    /// <summary>Floor level within a structure (null if outdoors or unknown). 0 = ground floor.</summary>
    public int? FloorLevel { get; set; }

    /// <summary>Last known heading in degrees (0-360, null if unknown). 0 = north, 90 = east.</summary>
    public double? LastKnownHeading { get; set; }

    /// <summary>Speed in meters per second (null if stationary or unknown).</summary>
    public double? SpeedMps { get; set; }

    /// <summary>Human-readable description of the threat (e.g., "Male, dark clothing, handgun visible").</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Relationship of the threat to the household — critical for DV situations.
    /// Determines safe harbor eligibility (harbors known to the threat are excluded).
    /// </summary>
    public ThreatRelationToHousehold RelationToHousehold { get; set; } = ThreatRelationToHousehold.Unknown;

    /// <summary>Whether the threat is currently actively violent (shooting, hitting, breaking in).</summary>
    public bool IsActivelyViolent { get; set; }

    /// <summary>UTC timestamp when this threat was first detected.</summary>
    public DateTime FirstDetectedAt { get; set; } = DateTime.UtcNow;

    /// <summary>UTC timestamp of the most recent update to this threat record.</summary>
    public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Correlation ID linking this threat to the broader emergency response chain (same as ResponseRequest.RequestId).</summary>
    public string CorrelationId { get; set; } = Guid.NewGuid().ToString();
}

/// <summary>
/// Complete history of a tracked threat, including position trail, events, and LAP assessment.
/// </summary>
public class ThreatHistory
{
    /// <summary>Unique identifier for this history record.</summary>
    public string HistoryId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>The threat ID this history belongs to.</summary>
    public string ThreatId { get; set; } = string.Empty;

    /// <summary>Chronological list of positions the threat has occupied.</summary>
    public List<ThreatPosition> Positions { get; set; } = new();

    /// <summary>Chronological list of events associated with this threat (weapon discharge, entry forced, etc.).</summary>
    public List<ThreatEvent> Events { get; set; } = new();

    /// <summary>
    /// Lethality Assessment Protocol score (0-11). Null if LAP has not been administered.
    /// Score >= 7 is "high danger" per the DOJ/Johns Hopkins Maryland Model.
    /// </summary>
    public int? LAPScore { get; set; }

    /// <summary>Individual answers to the 11-question LAP (null if not administered).</summary>
    public List<LAPAnswer> LAPAnswers { get; set; } = new();

    /// <summary>UTC timestamp when tracking began for this threat.</summary>
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    /// <summary>UTC timestamp of the most recent update.</summary>
    public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// A single position reading in a threat's tracked path.
/// </summary>
public class ThreatPosition
{
    /// <summary>Latitude (WGS 84).</summary>
    public double Latitude { get; set; }

    /// <summary>Longitude (WGS 84).</summary>
    public double Longitude { get; set; }

    /// <summary>Floor level within a structure (null if outdoors).</summary>
    public int? FloorLevel { get; set; }

    /// <summary>Heading in degrees (0-360, null if unknown).</summary>
    public double? Heading { get; set; }

    /// <summary>Speed in meters per second (null if stationary).</summary>
    public double? SpeedMps { get; set; }

    /// <summary>UTC timestamp of this position reading.</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>Detection method that produced this position reading.</summary>
    public ThreatDetectionMethod Source { get; set; } = ThreatDetectionMethod.SensorFusion;
}

/// <summary>
/// A discrete event in a threat's timeline (weapon discharged, hostage taken, threat fled, etc.).
/// </summary>
public class ThreatEvent
{
    /// <summary>Unique event identifier.</summary>
    public string EventId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>The threat this event belongs to.</summary>
    public string ThreatId { get; set; } = string.Empty;

    /// <summary>
    /// Event type string. Known values:
    /// "WeaponDischarged", "EntryForced", "HostageTaken", "VictimInjured",
    /// "ThreatNeutralized", "ThreatFled".
    /// </summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>UTC timestamp when the event occurred.</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>Human-readable description of the event.</summary>
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// An answer to one of the 11 questions in the DOJ/Johns Hopkins Lethality Assessment Protocol.
/// The LAP is a validated screening tool for domestic violence situations.
/// A total score >= 7 (out of 11) indicates "high danger" per the Maryland Model.
///
/// Example:
///   new LAPAnswer { QuestionNumber = 1, QuestionText = LAPQuestions.Q1, Answer = true, Weight = 4 }
/// </summary>
public class LAPAnswer
{
    /// <summary>Question number (1-11) per the standard LAP instrument.</summary>
    public int QuestionNumber { get; set; }

    /// <summary>Full text of the LAP question.</summary>
    public string QuestionText { get; set; } = string.Empty;

    /// <summary>The answer (true = yes, false = no, null = refused/unable to answer).</summary>
    public bool? Answer { get; set; }

    /// <summary>
    /// Weight of this question in the LAP scoring.
    /// Question 1 (attempted to kill) has weight 4; all others have weight 1.
    /// Total possible score: 4 + 10 = 14, but protocol scores on 0-11 scale
    /// (Q1 "yes" automatically scores "high danger" regardless of total).
    /// </summary>
    public int Weight { get; set; } = 1;
}

/// <summary>
/// An edge in the egress (escape route) graph that is blocked by a threat.
/// The egress computation builds a graph of all exits and corridors, then marks
/// edges as blocked based on threat position, line of sight, and acoustic proximity.
/// </summary>
public class BlockedEgressEdge
{
    /// <summary>Identifier for the egress path/edge that is blocked.</summary>
    public string PathId { get; set; } = string.Empty;

    /// <summary>The threat that is blocking this egress path.</summary>
    public string ThreatId { get; set; } = string.Empty;

    /// <summary>Reason this path is blocked (direct presence, line of sight, acoustic proximity, area denial).</summary>
    public BlocksEgressReason Reason { get; set; }

    /// <summary>UTC timestamp when the blockage was detected.</summary>
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Estimated UTC time when the path may clear (null if indefinite or unknown).</summary>
    public DateTime? EstimatedClearAt { get; set; }
}

/// <summary>
/// A safe harbor location where a person can shelter during a threat event.
/// For DV situations, harbors known to the threat perpetrator are excluded.
/// </summary>
public class SafeHarbor
{
    /// <summary>Unique identifier for this safe harbor.</summary>
    public string HarborId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Display name (e.g., "Neighbor's House - Johnson", "Community Center Safe Room").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Room identifier within a structure (null if the harbor is an entire building).</summary>
    public string? RoomId { get; set; }

    /// <summary>Latitude of the safe harbor (WGS 84).</summary>
    public double Latitude { get; set; }

    /// <summary>Longitude of the safe harbor (WGS 84).</summary>
    public double Longitude { get; set; }

    /// <summary>
    /// Whether this harbor location is known to the threat perpetrator.
    /// Critical for DV — if the perpetrator knows the location, it is NOT safe.
    /// </summary>
    public bool IsKnownToThreat { get; set; }

    /// <summary>Whether this harbor has a dedicated safe room (reinforced door, communications, etc.).</summary>
    public bool HasSafeRoom { get; set; }

    /// <summary>Whether this harbor is currently available (occupied, unlocked, reachable).</summary>
    public bool AvailableNow { get; set; }

    /// <summary>Maximum number of people this harbor can shelter.</summary>
    public int Capacity { get; set; }

    /// <summary>Distance in meters from the requesting user's current position.</summary>
    public double DistanceMeters { get; set; }

    /// <summary>Contact phone number for the harbor (safe room operator, neighbor, etc.).</summary>
    public string? ContactPhone { get; set; }
}

/// <summary>
/// An acoustic event detected by a microphone array or acoustic sensor.
/// Classification follows MIL-STD-1474E impulse noise methodology for gunshots/explosions
/// and UL 639 frequency analysis for glass break events.
/// </summary>
public class AcousticEvent
{
    /// <summary>Unique event identifier.</summary>
    public string EventId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Classified type of acoustic event.</summary>
    public AcousticEventType Type { get; set; }

    /// <summary>Confidence of the classification (0.0 = uncertain, 1.0 = certain).</summary>
    public float Confidence { get; set; }

    /// <summary>Peak decibel level measured at the sensor (dB SPL).</summary>
    public double DecibelLevel { get; set; }

    /// <summary>Estimated latitude of the sound source (WGS 84, triangulated if multiple sensors).</summary>
    public double Latitude { get; set; }

    /// <summary>Estimated longitude of the sound source (WGS 84).</summary>
    public double Longitude { get; set; }

    /// <summary>UTC timestamp when the acoustic event was detected.</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>Identifier of the sensor that detected the event.</summary>
    public string SensorId { get; set; } = string.Empty;

    /// <summary>
    /// SHA-256 hash of the raw audio signature for evidence chain-of-custody.
    /// Per MIL-STD-1474E, the raw waveform is preserved; this hash ensures integrity.
    /// </summary>
    public string RawSignatureHash { get; set; } = string.Empty;
}

/// <summary>
/// A reading from a door sensor (Z-Wave, Zigbee, WiFi, or BLE).
/// Forced entry events automatically create a ThreatSource via ProcessDoorSensorAsync.
/// </summary>
public class DoorSensorReading
{
    /// <summary>Identifier of the door sensor device.</summary>
    public string SensorId { get; set; } = string.Empty;

    /// <summary>Identifier of the door being monitored.</summary>
    public string DoorId { get; set; } = string.Empty;

    /// <summary>Identifier of the room the door belongs to.</summary>
    public string RoomId { get; set; } = string.Empty;

    /// <summary>The sensor event type (normal open, forced entry, tampered, etc.).</summary>
    public DoorSensorEvent Event { get; set; }

    /// <summary>
    /// Communication protocol of the sensor.
    /// Known values: "ZWave", "Zigbee", "WiFi", "BLE".
    /// </summary>
    public string Protocol { get; set; } = "ZWave";

    /// <summary>UTC timestamp of the sensor reading.</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>Battery percentage of the sensor (null if wired or unknown).</summary>
    public int? BatteryPercent { get; set; }
}

/// <summary>
/// A reading from a glass break sensor, classified per UL 639.
/// UL 639 defines two signature components: the initial "shock" (thud) and the subsequent
/// "flex" (tinkling) — both must be present for a valid glass break classification.
/// </summary>
public class GlassBreakReading
{
    /// <summary>Identifier of the glass break sensor device.</summary>
    public string SensorId { get; set; } = string.Empty;

    /// <summary>Identifier of the window being monitored.</summary>
    public string WindowId { get; set; } = string.Empty;

    /// <summary>Identifier of the room the window belongs to.</summary>
    public string RoomId { get; set; } = string.Empty;

    /// <summary>Confidence of the glass break classification (0.0-1.0).</summary>
    public float Confidence { get; set; }

    /// <summary>Dominant frequency of the break event in Hz (glass typically 3-6 kHz).</summary>
    public double FrequencyHz { get; set; }

    /// <summary>Peak decibel level measured at the sensor (dB SPL).</summary>
    public double DecibelLevel { get; set; }

    /// <summary>
    /// Break pattern type per UL 639 classification.
    /// Known values: "Impact" (object thrown), "Thermal" (heat-induced), "Forced" (prying/cutting).
    /// </summary>
    public string BreakPatternType { get; set; } = "Impact";

    /// <summary>UTC timestamp of the glass break event.</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Configuration for stealth mode — designed for domestic violence victims who must
/// use the app without the perpetrator knowing.
/// When enabled: no audible alerts, screen dims, notifications are silent,
/// and a duress code can fake a "safe" response while silently alerting responders.
/// </summary>
public class StealthModeConfig
{
    /// <summary>Whether stealth mode is currently enabled.</summary>
    public bool IsEnabled { get; set; }

    /// <summary>Push notifications are silent (no sound, no vibration).</summary>
    public bool SilentNotifications { get; set; }

    /// <summary>Screen brightness is reduced to minimum to avoid drawing attention.</summary>
    public bool ScreenDimmed { get; set; }

    /// <summary>No audible alerts of any kind (alarms, rings, notification sounds).</summary>
    public bool NoAudibleAlerts { get; set; }

    /// <summary>Prefer text-only communication — no voice calls, no video.</summary>
    public bool PreferTextOnly { get; set; }

    /// <summary>
    /// A secret code that, when entered, appears to dismiss the alert but silently
    /// continues recording and notifies responders that the user is under duress.
    /// Example: entering "1234" appears to cancel, but actually triggers silent SOS.
    /// </summary>
    public string? DuressCode { get; set; }

    /// <summary>
    /// A safe word or phrase that can be spoken aloud to trigger silent emergency response.
    /// Designed for situations where the victim cannot physically interact with the phone.
    /// Example: "I need to call my sister" triggers silent SOS.
    /// </summary>
    public string? SafeWordPhrase { get; set; }

    /// <summary>User IDs of trusted contacts who are notified during stealth mode events.</summary>
    public List<string> TrustedContactIds { get; set; } = new();

    /// <summary>
    /// When true, the system prioritizes locating and protecting children who may be hiding
    /// in the dwelling during a DV event. Responders receive child location information.
    /// </summary>
    public bool ChildInHidingPriority { get; set; }
}

/// <summary>
/// Static class containing the 11 standard questions of the DOJ/Johns Hopkins
/// Lethality Assessment Protocol (LAP), also known as the Maryland Model.
///
/// The LAP is a validated screening tool used by law enforcement and victim advocates
/// to assess the danger level in domestic violence situations. A "yes" to Question 1
/// (has the person tried to kill you) automatically qualifies as "high danger."
/// A total weighted score >= 7 also qualifies as "high danger."
///
/// Source: Maryland Network Against Domestic Violence / Johns Hopkins School of Nursing.
/// Validated in: Campbell, J.C. et al. (2009). "The Lethality Screen."
///
/// Example usage:
///   var answers = LAPQuestions.AllQuestions.Select((q, i) => new LAPAnswer
///   {
///       QuestionNumber = i + 1,
///       QuestionText = q,
///       Weight = i == 0 ? 4 : 1
///   }).ToList();
/// </summary>
public static class LAPQuestions
{
    /// <summary>Q1: Has he/she ever used a weapon against you or threatened you with a weapon? (Weight: 4 — "yes" alone = high danger)</summary>
    public const string Q1 = "Has he/she ever used a weapon against you or threatened you with a weapon?";

    /// <summary>Q2: Has he/she threatened to kill you or your children?</summary>
    public const string Q2 = "Has he/she threatened to kill you or your children?";

    /// <summary>Q3: Do you think he/she might try to kill you?</summary>
    public const string Q3 = "Do you think he/she might try to kill you?";

    /// <summary>Q4: Does he/she have a gun or can he/she get one easily?</summary>
    public const string Q4 = "Does he/she have a gun or can he/she get one easily?";

    /// <summary>Q5: Has he/she ever tried to choke (strangle) you?</summary>
    public const string Q5 = "Has he/she ever tried to choke (strangle) you?";

    /// <summary>Q6: Is he/she violently or constantly jealous or does he/she control most of your daily activities?</summary>
    public const string Q6 = "Is he/she violently or constantly jealous or does he/she control most of your daily activities?";

    /// <summary>Q7: Have you left or separated from him/her after living together or being married?</summary>
    public const string Q7 = "Have you left or separated from him/her after living together or being married?";

    /// <summary>Q8: Is he/she unemployed?</summary>
    public const string Q8 = "Is he/she unemployed?";

    /// <summary>Q9: Has he/she ever tried to kill himself/herself?</summary>
    public const string Q9 = "Has he/she ever tried to kill himself/herself?";

    /// <summary>Q10: Do you have a child that he/she knows is not his/hers?</summary>
    public const string Q10 = "Do you have a child that he/she knows is not his/hers?";

    /// <summary>Q11: Does he/she follow or spy on you or leave threatening messages?</summary>
    public const string Q11 = "Does he/she follow or spy on you or leave threatening messages?";

    /// <summary>All 11 LAP questions in order, for iteration.</summary>
    public static readonly IReadOnlyList<string> AllQuestions = new[]
    {
        Q1, Q2, Q3, Q4, Q5, Q6, Q7, Q8, Q9, Q10, Q11
    };

    /// <summary>
    /// Weights for each question. Q1 = 4 (automatic high danger if yes), all others = 1.
    /// Index 0 = Q1, Index 10 = Q11.
    /// </summary>
    public static readonly IReadOnlyList<int> Weights = new[]
    {
        4, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1
    };

    /// <summary>
    /// The score threshold at or above which the LAP result is "high danger."
    /// Per the DOJ/Johns Hopkins Maryland Model, a score >= 7 = high danger.
    /// Note: a "yes" to Q1 alone (weight 4) does NOT meet this threshold by itself,
    /// but per protocol, Q1 "yes" is independently classified as high danger.
    /// </summary>
    public const int HighDangerThreshold = 7;
}
