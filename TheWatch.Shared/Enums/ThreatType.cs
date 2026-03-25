// ThreatType.cs — enumerates threat tracking, violence detection, acoustic classification,
// sensor event types, and related enums for the TheWatch safety platform.
//
// This file covers the complete threat domain: from threat classification and mobility
// to acoustic gunshot detection (MIL-STD-1474E), door/window sensor events (Z-Wave/Zigbee),
// glass break detection (UL 639), and domestic violence lethality assessment support.
//
// Enum value range: 600-699 (reserved for threat domain).
//
// Standards referenced:
//   MIL-STD-1474E — Department of Defense Design Criteria Standard: Noise Limits
//   UL 639       — Standard for Intrusion-Detection Units (glass break sensors)
//   Z-Wave/Zigbee — Smart home sensor protocols for door/window sensors
//   DOJ/Johns Hopkins Lethality Assessment Protocol (LAP) — 11-question DV screening
//
// Example: ThreatType.ActiveShooter when CCTV analysis detects a firearm discharge event.
// Example: AcousticEventType.GunshotSingle when a single gunshot signature is classified.
// Example: DoorSensorEvent.ForcedEntryWhileLocked when a Z-Wave sensor detects forced entry.

namespace TheWatch.Shared.Enums;

/// <summary>
/// Classification of the threat type observed or reported.
/// Maps to federal incident classification codes used by DHS and FBI UCR.
/// </summary>
public enum ThreatType
{
    /// <summary>Active shooter situation — one or more individuals actively engaged in killing or attempting to kill people in a populated area (FBI definition).</summary>
    ActiveShooter = 600,

    /// <summary>Domestic violence incident — violence or abuse between intimate partners or household members. Triggers LAP assessment.</summary>
    DomesticViolence = 601,

    /// <summary>Unauthorized intruder detected on premises — may be armed or unarmed.</summary>
    Intruder = 602,

    /// <summary>Armed robbery in progress — threat is demanding property under threat of violence.</summary>
    ArmedRobbery = 603,

    /// <summary>Kidnapping or abduction in progress — victim is being taken against their will.</summary>
    Kidnapping = 604,

    /// <summary>Stalking behavior detected — repeated unwanted contact, following, or surveillance of a victim.</summary>
    Stalking = 605,

    /// <summary>Hate crime — violence motivated by bias against race, religion, sexual orientation, gender identity, or disability (18 U.S.C. § 249).</summary>
    HateCrime = 606,

    /// <summary>Terrorism — premeditated, politically motivated violence against noncombatant targets (18 U.S.C. § 2331).</summary>
    Terrorism = 607,

    /// <summary>Threat type could not be determined from available sensor data or reports.</summary>
    Unknown = 608
}

/// <summary>
/// Mobility classification of a tracked threat source.
/// Used by egress computation to determine how quickly escape routes become blocked.
/// </summary>
public enum ThreatMobility
{
    /// <summary>Threat is stationary — fixed position, not moving.</summary>
    Stationary = 610,

    /// <summary>Threat is moving slowly — walking pace, approximately 1-2 m/s.</summary>
    SlowMoving = 611,

    /// <summary>Threat is moving quickly — running pace, approximately 3-8 m/s.</summary>
    FastMoving = 612,

    /// <summary>Threat is in a vehicle — speed exceeds typical foot travel, requires road-based tracking.</summary>
    Vehicular = 613,

    /// <summary>Mobility could not be determined from available data.</summary>
    Unknown = 614
}

/// <summary>
/// Armed status of a tracked threat, used for responder safety briefing and 911 dispatch priority.
/// </summary>
public enum ThreatArmedStatus
{
    /// <summary>Threat appears unarmed — no weapon detected or reported.</summary>
    Unarmed = 620,

    /// <summary>Threat possesses a firearm — handgun, rifle, shotgun, or other projectile weapon.</summary>
    Firearm = 621,

    /// <summary>Threat possesses a blunt weapon — bat, pipe, club, or similar impact weapon.</summary>
    Blunt = 622,

    /// <summary>Threat possesses an edged weapon — knife, machete, box cutter, or similar cutting instrument.</summary>
    Edged = 623,

    /// <summary>Threat possesses explosive materials — IED, grenade, or other detonation device.</summary>
    Explosive = 624,

    /// <summary>Threat possesses chemical agent — pepper spray, tear gas, acid, or other chemical weapon.</summary>
    Chemical = 625,

    /// <summary>Armed status could not be determined from available data.</summary>
    Unknown = 626
}

/// <summary>
/// Method by which the threat was initially detected.
/// Determines confidence weighting and corroboration requirements.
/// </summary>
public enum ThreatDetectionMethod
{
    /// <summary>Threat visually confirmed by a human observer (highest confidence).</summary>
    VisualConfirmed = 630,

    /// <summary>Threat detected via acoustic signature analysis — gunshot, glass break, forced entry (per MIL-STD-1474E).</summary>
    AcousticSignature = 631,

    /// <summary>Threat detected by fusing multiple sensor inputs — acoustic + visual + motion correlation.</summary>
    SensorFusion = 632,

    /// <summary>Threat reported by a user via the app — manual SOS or threat report submission.</summary>
    UserReported = 633,

    /// <summary>Threat detected by a door sensor — forced entry, tamper, or anomalous open/close pattern (Z-Wave/Zigbee).</summary>
    DoorSensor = 634,

    /// <summary>Threat detected by a glass break sensor — window or glass panel shattered (per UL 639).</summary>
    GlassBreakSensor = 635,

    /// <summary>Threat detected by a motion sensor — PIR, microwave, or dual-technology motion detector.</summary>
    MotionSensor = 636,

    /// <summary>Threat detected by CCTV/camera analysis — AI-based object detection, weapon recognition, or behavioral analysis.</summary>
    CCTVAnalysis = 637
}

/// <summary>
/// Reason a threat blocks an egress (escape) route.
/// Used by the egress graph computation to mark edges as impassable.
/// </summary>
public enum BlocksEgressReason
{
    /// <summary>Threat is physically present on the egress path — direct obstruction.</summary>
    DirectPresence = 640,

    /// <summary>Threat has line of sight to the egress path — passage would expose the escaping person.</summary>
    LineOfSight = 641,

    /// <summary>Threat is acoustically proximate — escaping person would be heard (footsteps, door opening).</summary>
    AcousticProximity = 642,

    /// <summary>Threat has denied the area — explosive, chemical, or fire blocks passage even if threat is not physically present.</summary>
    AreaDenial = 643
}

/// <summary>
/// Classification of acoustic events detected by microphone arrays or acoustic sensors.
/// Referenced standards:
///   MIL-STD-1474E — impulse noise measurement and classification methodology
///   UL 639        — glass break detection frequency and pattern standards
/// </summary>
public enum AcousticEventType
{
    /// <summary>Single gunshot detected — isolated impulse noise signature matching firearm discharge (MIL-STD-1474E).</summary>
    GunshotSingle = 650,

    /// <summary>Multiple gunshots detected — semi-automatic fire pattern, distinct individual impulses.</summary>
    GunshotMultiple = 651,

    /// <summary>Automatic gunfire detected — sustained rapid-fire impulse train characteristic of automatic weapons.</summary>
    GunshotAutomatic = 652,

    /// <summary>Raised voices detected — elevated vocal amplitude suggesting argument, confrontation, or distress.</summary>
    RaisedVoices = 653,

    /// <summary>Light impact sound detected — slap, punch, thrown object, or minor collision.</summary>
    ImpactSoundLight = 654,

    /// <summary>Heavy impact sound detected — body hitting wall/floor, furniture overturned, heavy object thrown.</summary>
    ImpactSoundHeavy = 655,

    /// <summary>Small glass break detected — drinking glass, picture frame, or small window pane (per UL 639 flex pattern).</summary>
    GlassBreakSmall = 656,

    /// <summary>Large glass break detected — full window, sliding door, or plate glass panel (per UL 639 shock + flex pattern).</summary>
    GlassBreakLarge = 657,

    /// <summary>Forced entry sound detected — door being kicked in, lock being drilled, or barrier being breached.</summary>
    ForcedEntry = 658,

    /// <summary>Door slam detected — rapid high-amplitude door closure, may indicate aggression or flight.</summary>
    DoorSlam = 659,

    /// <summary>Scream detected — high-pitched sustained vocalization indicating fear, pain, or distress.</summary>
    Scream = 660,

    /// <summary>Sudden silence detected — abrupt cessation of expected ambient sound, may indicate suppression or incapacitation.</summary>
    Silence = 661,

    /// <summary>Explosion detected — high-energy broadband impulse exceeding gunshot amplitude and duration (MIL-STD-1474E).</summary>
    Explosion = 662
}

/// <summary>
/// Events reported by door sensors (Z-Wave, Zigbee, WiFi, or BLE protocol).
/// Referenced protocols: Z-Wave (ITU-T G.9959), Zigbee (IEEE 802.15.4), BLE (Bluetooth 5.x).
/// </summary>
public enum DoorSensorEvent
{
    /// <summary>Normal door open event — door opened during expected hours/pattern.</summary>
    NormalOpen = 670,

    /// <summary>Normal door close event — door closed following a normal open.</summary>
    NormalClose = 671,

    /// <summary>Forced entry while locked — sensor detected door breach without prior unlock command (high threat indicator).</summary>
    ForcedEntryWhileLocked = 672,

    /// <summary>Rapid open/close sequence — door opened and closed multiple times in rapid succession (may indicate panic, search, or distraction).</summary>
    RapidOpenCloseSequence = 673,

    /// <summary>Door held open — door has remained open beyond the configured threshold (ventilation, prop, or breach).</summary>
    HeldOpen = 674,

    /// <summary>Sensor tampered — device reports physical tampering, removal, or signal jamming attempt.</summary>
    Tampered = 675
}

/// <summary>
/// Relationship of the threat to the household or victim.
/// Critical for domestic violence situations — determines safe harbor eligibility and LAP scoring.
/// </summary>
public enum ThreatRelationToHousehold
{
    /// <summary>No known relationship to the household — stranger threat.</summary>
    None = 680,

    /// <summary>Current intimate partner of a household member — highest DV risk indicator.</summary>
    CurrentPartner = 681,

    /// <summary>Former intimate partner of a household member — high DV risk, may know dwelling layout.</summary>
    ExPartner = 682,

    /// <summary>Family member of the household — blood or legal relation, may have keys/access codes.</summary>
    FamilyMember = 683,

    /// <summary>Known acquaintance of the household — neighbor, coworker, friend, or associate.</summary>
    Acquaintance = 684,

    /// <summary>Complete stranger — no prior relationship to anyone in the household.</summary>
    Stranger = 685,

    /// <summary>Relationship to household could not be determined.</summary>
    Unknown = 686
}
