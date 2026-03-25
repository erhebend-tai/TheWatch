// ThreatModels.kt — Android domain models for threat tracking, violence detection,
// acoustic classification, sensor integration, safe harbor routing, stealth mode,
// and DOJ/Johns Hopkins Lethality Assessment Protocol (LAP).
//
// Mirrors the C# models in TheWatch.Shared.Domain.Models.ThreatModels
// and enums in TheWatch.Shared.Enums.ThreatType.
//
// Standards referenced:
//   MIL-STD-1474E — acoustic gunshot classification
//   UL 639        — glass break sensor standards
//   Z-Wave/Zigbee — door sensor protocols
//   DOJ/Johns Hopkins LAP — 11-question DV lethality screening
//
// Example — create a threat report:
//   val threat = ThreatSource(
//       type = ThreatType.Intruder,
//       armedStatus = ThreatArmedStatus.Edged,
//       detectionMethod = ThreatDetectionMethod.DoorSensor,
//       confidence = 0.85f,
//       latitude = 38.8977,
//       longitude = -77.0365
//   )
//
// Example — stealth mode for DV victim:
//   val config = StealthModeConfig(
//       isEnabled = true,
//       silentNotifications = true,
//       screenDimmed = true,
//       duressCode = "1234",
//       safeWordPhrase = "I need to call my sister"
//   )

package com.thewatch.app.data.model

import java.util.UUID

// ═══════════════════════════════════════════════════════════════
// Enums
// ═══════════════════════════════════════════════════════════════

/**
 * Classification of the threat type observed or reported.
 * Maps to federal incident classification codes (DHS/FBI UCR).
 */
enum class ThreatType {
    /** Active shooter — one or more individuals actively engaged in killing people in a populated area. */
    ActiveShooter,
    /** Domestic violence — violence or abuse between intimate partners or household members. Triggers LAP. */
    DomesticViolence,
    /** Unauthorized intruder detected on premises. */
    Intruder,
    /** Armed robbery in progress — threat demanding property under threat of violence. */
    ArmedRobbery,
    /** Kidnapping or abduction in progress. */
    Kidnapping,
    /** Stalking behavior — repeated unwanted contact, following, or surveillance. */
    Stalking,
    /** Hate crime — violence motivated by bias (18 U.S.C. 249). */
    HateCrime,
    /** Terrorism — premeditated, politically motivated violence (18 U.S.C. 2331). */
    Terrorism,
    /** Threat type could not be determined. */
    Unknown
}

/**
 * Mobility classification of a tracked threat source.
 * Used by egress computation to determine how quickly escape routes become blocked.
 */
enum class ThreatMobility {
    /** Threat is stationary. */
    Stationary,
    /** Threat moving slowly — walking pace, ~1-2 m/s. */
    SlowMoving,
    /** Threat moving quickly — running pace, ~3-8 m/s. */
    FastMoving,
    /** Threat is in a vehicle. */
    Vehicular,
    /** Mobility could not be determined. */
    Unknown
}

/**
 * Armed status of a tracked threat.
 * Used for responder safety briefing and 911 dispatch priority.
 */
enum class ThreatArmedStatus {
    /** No weapon detected or reported. */
    Unarmed,
    /** Firearm — handgun, rifle, shotgun. */
    Firearm,
    /** Blunt weapon — bat, pipe, club. */
    Blunt,
    /** Edged weapon — knife, machete, box cutter. */
    Edged,
    /** Explosive — IED, grenade. */
    Explosive,
    /** Chemical agent — pepper spray, tear gas, acid. */
    Chemical,
    /** Armed status could not be determined. */
    Unknown
}

/**
 * Method by which the threat was initially detected.
 * Determines confidence weighting and corroboration requirements.
 */
enum class ThreatDetectionMethod {
    /** Visually confirmed by a human observer (highest confidence). */
    VisualConfirmed,
    /** Acoustic signature analysis — gunshot, glass break, forced entry (MIL-STD-1474E). */
    AcousticSignature,
    /** Multiple sensor inputs fused — acoustic + visual + motion. */
    SensorFusion,
    /** User reported via the app — manual SOS or threat report. */
    UserReported,
    /** Door sensor — forced entry, tamper, anomalous pattern (Z-Wave/Zigbee). */
    DoorSensor,
    /** Glass break sensor — window or glass panel shattered (UL 639). */
    GlassBreakSensor,
    /** Motion sensor — PIR, microwave, or dual-technology. */
    MotionSensor,
    /** CCTV/camera analysis — AI object detection, weapon recognition. */
    CCTVAnalysis
}

/**
 * Reason a threat blocks an egress (escape) route.
 */
enum class BlocksEgressReason {
    /** Threat physically present on the egress path. */
    DirectPresence,
    /** Threat has line of sight to the egress path. */
    LineOfSight,
    /** Threat is acoustically proximate — escaping person would be heard. */
    AcousticProximity,
    /** Area denied — explosive, chemical, or fire blocks passage. */
    AreaDenial
}

/**
 * Classification of acoustic events detected by sensors.
 * Referenced: MIL-STD-1474E (gunshots/explosions), UL 639 (glass break).
 */
enum class AcousticEventType {
    /** Single gunshot — isolated impulse noise (MIL-STD-1474E). */
    GunshotSingle,
    /** Multiple gunshots — semi-automatic fire pattern. */
    GunshotMultiple,
    /** Automatic gunfire — sustained rapid-fire impulse train. */
    GunshotAutomatic,
    /** Raised voices — elevated amplitude suggesting argument or distress. */
    RaisedVoices,
    /** Light impact — slap, punch, thrown object. */
    ImpactSoundLight,
    /** Heavy impact — body hitting wall, furniture overturned. */
    ImpactSoundHeavy,
    /** Small glass break — drinking glass, picture frame (UL 639 flex). */
    GlassBreakSmall,
    /** Large glass break — full window, sliding door (UL 639 shock + flex). */
    GlassBreakLarge,
    /** Forced entry — door kicked in, lock drilled, barrier breached. */
    ForcedEntry,
    /** Door slam — rapid high-amplitude closure. */
    DoorSlam,
    /** Scream — high-pitched sustained vocalization indicating distress. */
    Scream,
    /** Sudden silence — abrupt cessation of ambient sound. */
    Silence,
    /** Explosion — high-energy broadband impulse (MIL-STD-1474E). */
    Explosion
}

/**
 * Events reported by door sensors (Z-Wave, Zigbee, WiFi, BLE).
 * Referenced: Z-Wave (ITU-T G.9959), Zigbee (IEEE 802.15.4).
 */
enum class DoorSensorEvent {
    /** Normal door open event. */
    NormalOpen,
    /** Normal door close event. */
    NormalClose,
    /** Forced entry while locked — breach without unlock command (high threat). */
    ForcedEntryWhileLocked,
    /** Rapid open/close sequence — panic, search, or distraction. */
    RapidOpenCloseSequence,
    /** Door held open beyond threshold. */
    HeldOpen,
    /** Sensor tampered — physical tampering, removal, or jamming. */
    Tampered
}

/**
 * Relationship of the threat to the household or victim.
 * Critical for DV — determines safe harbor eligibility and LAP scoring.
 */
enum class ThreatRelationToHousehold {
    /** No known relationship. */
    None,
    /** Current intimate partner (highest DV risk). */
    CurrentPartner,
    /** Former intimate partner (high DV risk, may know dwelling layout). */
    ExPartner,
    /** Family member (may have keys/access codes). */
    FamilyMember,
    /** Known acquaintance — neighbor, coworker, friend. */
    Acquaintance,
    /** Complete stranger. */
    Stranger,
    /** Relationship could not be determined. */
    Unknown
}

// ═══════════════════════════════════════════════════════════════
// Models
// ═══════════════════════════════════════════════════════════════

/**
 * A tracked threat source with real-time position, classification, and armed status.
 * Central entity for the threat tracking subsystem.
 */
data class ThreatSource(
    val threatId: String = UUID.randomUUID().toString(),
    val type: ThreatType = ThreatType.Unknown,
    val mobility: ThreatMobility = ThreatMobility.Unknown,
    val armedStatus: ThreatArmedStatus = ThreatArmedStatus.Unknown,
    val detectionMethod: ThreatDetectionMethod = ThreatDetectionMethod.UserReported,
    val confidence: Float = 0f,
    val latitude: Double = 0.0,
    val longitude: Double = 0.0,
    val floorLevel: Int? = null,
    val lastKnownHeading: Double? = null,
    val speedMps: Double? = null,
    val description: String = "",
    val relationToHousehold: ThreatRelationToHousehold = ThreatRelationToHousehold.Unknown,
    val isActivelyViolent: Boolean = false,
    val firstDetectedAt: String = "",
    val lastUpdatedAt: String = "",
    val correlationId: String = UUID.randomUUID().toString()
)

/**
 * Complete history of a tracked threat including position trail, events, and LAP assessment.
 */
data class ThreatHistory(
    val historyId: String = UUID.randomUUID().toString(),
    val threatId: String = "",
    val positions: List<ThreatPosition> = emptyList(),
    val events: List<ThreatEvent> = emptyList(),
    val lapScore: Int? = null,
    val lapAnswers: List<LAPAnswer> = emptyList(),
    val startedAt: String = "",
    val lastUpdatedAt: String = ""
)

/**
 * A single position reading in a threat's tracked path.
 */
data class ThreatPosition(
    val latitude: Double = 0.0,
    val longitude: Double = 0.0,
    val floorLevel: Int? = null,
    val heading: Double? = null,
    val speedMps: Double? = null,
    val timestamp: String = "",
    val source: ThreatDetectionMethod = ThreatDetectionMethod.SensorFusion
)

/**
 * A discrete event in a threat's timeline.
 * EventType known values: "WeaponDischarged", "EntryForced", "HostageTaken",
 * "VictimInjured", "ThreatNeutralized", "ThreatFled".
 */
data class ThreatEvent(
    val eventId: String = UUID.randomUUID().toString(),
    val threatId: String = "",
    val eventType: String = "",
    val timestamp: String = "",
    val description: String = ""
)

/**
 * An answer to one of the 11 DOJ/Johns Hopkins Lethality Assessment Protocol questions.
 * Score >= 7 or Q1 "yes" = "high danger" per the Maryland Model.
 *
 * Example:
 *   val answer = LAPAnswer(
 *       questionNumber = 1,
 *       questionText = LAPQuestions.Q1,
 *       answer = true,
 *       weight = 4
 *   )
 */
data class LAPAnswer(
    val questionNumber: Int = 0,
    val questionText: String = "",
    val answer: Boolean? = null,
    val weight: Int = 1
)

/**
 * An edge in the egress graph blocked by a threat.
 */
data class BlockedEgressEdge(
    val pathId: String = "",
    val threatId: String = "",
    val reason: BlocksEgressReason = BlocksEgressReason.DirectPresence,
    val detectedAt: String = "",
    val estimatedClearAt: String? = null
)

/**
 * A safe harbor location where a person can shelter during a threat event.
 * For DV situations, harbors known to the perpetrator are excluded.
 */
data class SafeHarbor(
    val harborId: String = UUID.randomUUID().toString(),
    val name: String = "",
    val roomId: String? = null,
    val latitude: Double = 0.0,
    val longitude: Double = 0.0,
    val isKnownToThreat: Boolean = false,
    val hasSafeRoom: Boolean = false,
    val availableNow: Boolean = true,
    val capacity: Int = 1,
    val distanceMeters: Double = 0.0,
    val contactPhone: String? = null
) {
    val distanceDisplay: String
        get() = if (distanceMeters < 1000) {
            String.format("%.0f m", distanceMeters)
        } else {
            String.format("%.1f km", distanceMeters / 1000)
        }
}

/**
 * An acoustic event detected by a microphone array or acoustic sensor.
 * Classification follows MIL-STD-1474E (gunshots) and UL 639 (glass break).
 */
data class AcousticEvent(
    val eventId: String = UUID.randomUUID().toString(),
    val type: AcousticEventType = AcousticEventType.Silence,
    val confidence: Float = 0f,
    val decibelLevel: Double = 0.0,
    val latitude: Double = 0.0,
    val longitude: Double = 0.0,
    val timestamp: String = "",
    val sensorId: String = "",
    val rawSignatureHash: String = ""
)

/**
 * A reading from a door sensor (Z-Wave, Zigbee, WiFi, or BLE).
 * Forced entry events automatically create a ThreatSource via ProcessDoorSensor.
 */
data class DoorSensorReading(
    val sensorId: String = "",
    val doorId: String = "",
    val roomId: String = "",
    val event: DoorSensorEvent = DoorSensorEvent.NormalClose,
    val protocol: String = "ZWave",
    val timestamp: String = "",
    val batteryPercent: Int? = null
)

/**
 * A reading from a glass break sensor, classified per UL 639.
 * BreakPatternType known values: "Impact", "Thermal", "Forced".
 */
data class GlassBreakReading(
    val sensorId: String = "",
    val windowId: String = "",
    val roomId: String = "",
    val confidence: Float = 0f,
    val frequencyHz: Double = 0.0,
    val decibelLevel: Double = 0.0,
    val breakPatternType: String = "Impact",
    val timestamp: String = ""
)

/**
 * Configuration for stealth mode — designed for DV victims.
 * When enabled: no audible alerts, screen dims, notifications are silent,
 * and a duress code can fake a "safe" response while silently alerting responders.
 *
 * Example:
 *   val config = StealthModeConfig(
 *       isEnabled = true,
 *       silentNotifications = true,
 *       screenDimmed = true,
 *       noAudibleAlerts = true,
 *       duressCode = "1234",
 *       safeWordPhrase = "I need to call my sister",
 *       childInHidingPriority = true
 *   )
 */
data class StealthModeConfig(
    val isEnabled: Boolean = false,
    val silentNotifications: Boolean = false,
    val screenDimmed: Boolean = false,
    val noAudibleAlerts: Boolean = false,
    val preferTextOnly: Boolean = false,
    val duressCode: String? = null,
    val safeWordPhrase: String? = null,
    val trustedContactIds: List<String> = emptyList(),
    val childInHidingPriority: Boolean = false
)

// ═══════════════════════════════════════════════════════════════
// LAP Questions
// ═══════════════════════════════════════════════════════════════

/**
 * The 11 standard questions of the DOJ/Johns Hopkins Lethality Assessment Protocol.
 * A "yes" to Q1 automatically qualifies as "high danger."
 * Total weighted score >= 7 also qualifies as "high danger."
 *
 * Source: Maryland Network Against Domestic Violence / Johns Hopkins School of Nursing.
 * Validated in: Campbell, J.C. et al. (2009). "The Lethality Screen."
 *
 * Example:
 *   val answers = LAPQuestions.allQuestions.mapIndexed { index, question ->
 *       LAPAnswer(
 *           questionNumber = index + 1,
 *           questionText = question,
 *           weight = LAPQuestions.weights[index]
 *       )
 *   }
 */
object LAPQuestions {
    const val Q1 = "Has he/she ever used a weapon against you or threatened you with a weapon?"
    const val Q2 = "Has he/she threatened to kill you or your children?"
    const val Q3 = "Do you think he/she might try to kill you?"
    const val Q4 = "Does he/she have a gun or can he/she get one easily?"
    const val Q5 = "Has he/she ever tried to choke (strangle) you?"
    const val Q6 = "Is he/she violently or constantly jealous or does he/she control most of your daily activities?"
    const val Q7 = "Have you left or separated from him/her after living together or being married?"
    const val Q8 = "Is he/she unemployed?"
    const val Q9 = "Has he/she ever tried to kill himself/herself?"
    const val Q10 = "Do you have a child that he/she knows is not his/hers?"
    const val Q11 = "Does he/she follow or spy on you or leave threatening messages?"

    val allQuestions: List<String> = listOf(
        Q1, Q2, Q3, Q4, Q5, Q6, Q7, Q8, Q9, Q10, Q11
    )

    /** Weights: Q1 = 4 (automatic high danger if yes), all others = 1. */
    val weights: List<Int> = listOf(
        4, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1
    )

    /** Score threshold at or above which the result is "high danger" per the Maryland Model. */
    const val HIGH_DANGER_THRESHOLD = 7
}
