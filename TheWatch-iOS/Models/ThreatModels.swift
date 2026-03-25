// ThreatModels.swift — iOS domain models for threat tracking, violence detection,
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
//   let threat = ThreatSource(
//       type: .intruder,
//       armedStatus: .edged,
//       detectionMethod: .doorSensor,
//       confidence: 0.85,
//       latitude: 38.8977,
//       longitude: -77.0365
//   )
//
// Example — stealth mode for DV victim:
//   let config = StealthModeConfig(
//       isEnabled: true,
//       silentNotifications: true,
//       screenDimmed: true,
//       duressCode: "1234",
//       safeWordPhrase: "I need to call my sister"
//   )

import Foundation

// MARK: - Enums

/// Classification of the threat type observed or reported.
/// Maps to federal incident classification codes (DHS/FBI UCR).
enum ThreatType: String, Codable, CaseIterable {
    case activeShooter = "ActiveShooter"
    case domesticViolence = "DomesticViolence"
    case intruder = "Intruder"
    case armedRobbery = "ArmedRobbery"
    case kidnapping = "Kidnapping"
    case stalking = "Stalking"
    case hateCrime = "HateCrime"
    case terrorism = "Terrorism"
    case unknown = "Unknown"
}

/// Mobility classification of a tracked threat source.
/// Used by egress computation to determine how quickly escape routes become blocked.
enum ThreatMobility: String, Codable, CaseIterable {
    case stationary = "Stationary"
    case slowMoving = "SlowMoving"
    case fastMoving = "FastMoving"
    case vehicular = "Vehicular"
    case unknown = "Unknown"
}

/// Armed status of a tracked threat.
/// Used for responder safety briefing and 911 dispatch priority.
enum ThreatArmedStatus: String, Codable, CaseIterable {
    case unarmed = "Unarmed"
    case firearm = "Firearm"
    case blunt = "Blunt"
    case edged = "Edged"
    case explosive = "Explosive"
    case chemical = "Chemical"
    case unknown = "Unknown"
}

/// Method by which the threat was initially detected.
/// Determines confidence weighting and corroboration requirements.
enum ThreatDetectionMethod: String, Codable, CaseIterable {
    case visualConfirmed = "VisualConfirmed"
    case acousticSignature = "AcousticSignature"
    case sensorFusion = "SensorFusion"
    case userReported = "UserReported"
    case doorSensor = "DoorSensor"
    case glassBreakSensor = "GlassBreakSensor"
    case motionSensor = "MotionSensor"
    case cctvAnalysis = "CCTVAnalysis"
}

/// Reason a threat blocks an egress (escape) route.
enum BlocksEgressReason: String, Codable, CaseIterable {
    case directPresence = "DirectPresence"
    case lineOfSight = "LineOfSight"
    case acousticProximity = "AcousticProximity"
    case areaDenial = "AreaDenial"
}

/// Classification of acoustic events detected by microphone arrays or acoustic sensors.
/// Referenced: MIL-STD-1474E (gunshots/explosions), UL 639 (glass break).
enum AcousticEventType: String, Codable, CaseIterable {
    case gunshotSingle = "GunshotSingle"
    case gunshotMultiple = "GunshotMultiple"
    case gunshotAutomatic = "GunshotAutomatic"
    case raisedVoices = "RaisedVoices"
    case impactSoundLight = "ImpactSoundLight"
    case impactSoundHeavy = "ImpactSoundHeavy"
    case glassBreakSmall = "GlassBreakSmall"
    case glassBreakLarge = "GlassBreakLarge"
    case forcedEntry = "ForcedEntry"
    case doorSlam = "DoorSlam"
    case scream = "Scream"
    case silence = "Silence"
    case explosion = "Explosion"
}

/// Events reported by door sensors (Z-Wave, Zigbee, WiFi, or BLE).
enum DoorSensorEvent: String, Codable, CaseIterable {
    case normalOpen = "NormalOpen"
    case normalClose = "NormalClose"
    case forcedEntryWhileLocked = "ForcedEntryWhileLocked"
    case rapidOpenCloseSequence = "RapidOpenCloseSequence"
    case heldOpen = "HeldOpen"
    case tampered = "Tampered"
}

/// Relationship of the threat to the household or victim.
/// Critical for DV — determines safe harbor eligibility and LAP scoring.
enum ThreatRelationToHousehold: String, Codable, CaseIterable {
    case none = "None"
    case currentPartner = "CurrentPartner"
    case exPartner = "ExPartner"
    case familyMember = "FamilyMember"
    case acquaintance = "Acquaintance"
    case stranger = "Stranger"
    case unknown = "Unknown"
}

// MARK: - Models

/// A tracked threat source with real-time position, classification, and armed status.
struct ThreatSource: Codable, Hashable, Identifiable {
    var id: String { threatId }
    let threatId: String
    var type: ThreatType
    var mobility: ThreatMobility
    var armedStatus: ThreatArmedStatus
    var detectionMethod: ThreatDetectionMethod
    var confidence: Float
    var latitude: Double
    var longitude: Double
    var floorLevel: Int?
    var lastKnownHeading: Double?
    var speedMps: Double?
    var description: String
    var relationToHousehold: ThreatRelationToHousehold
    var isActivelyViolent: Bool
    var firstDetectedAt: Date
    var lastUpdatedAt: Date
    var correlationId: String

    init(
        threatId: String = UUID().uuidString,
        type: ThreatType = .unknown,
        mobility: ThreatMobility = .unknown,
        armedStatus: ThreatArmedStatus = .unknown,
        detectionMethod: ThreatDetectionMethod = .userReported,
        confidence: Float = 0,
        latitude: Double = 0,
        longitude: Double = 0,
        floorLevel: Int? = nil,
        lastKnownHeading: Double? = nil,
        speedMps: Double? = nil,
        description: String = "",
        relationToHousehold: ThreatRelationToHousehold = .unknown,
        isActivelyViolent: Bool = false,
        firstDetectedAt: Date = Date(),
        lastUpdatedAt: Date = Date(),
        correlationId: String = UUID().uuidString
    ) {
        self.threatId = threatId
        self.type = type
        self.mobility = mobility
        self.armedStatus = armedStatus
        self.detectionMethod = detectionMethod
        self.confidence = confidence
        self.latitude = latitude
        self.longitude = longitude
        self.floorLevel = floorLevel
        self.lastKnownHeading = lastKnownHeading
        self.speedMps = speedMps
        self.description = description
        self.relationToHousehold = relationToHousehold
        self.isActivelyViolent = isActivelyViolent
        self.firstDetectedAt = firstDetectedAt
        self.lastUpdatedAt = lastUpdatedAt
        self.correlationId = correlationId
    }
}

/// Complete history of a tracked threat including position trail, events, and LAP assessment.
struct ThreatHistory: Codable, Hashable, Identifiable {
    var id: String { historyId }
    let historyId: String
    var threatId: String
    var positions: [ThreatPosition]
    var events: [ThreatEvent]
    var lapScore: Int?
    var lapAnswers: [LAPAnswer]
    var startedAt: Date
    var lastUpdatedAt: Date

    init(
        historyId: String = UUID().uuidString,
        threatId: String = "",
        positions: [ThreatPosition] = [],
        events: [ThreatEvent] = [],
        lapScore: Int? = nil,
        lapAnswers: [LAPAnswer] = [],
        startedAt: Date = Date(),
        lastUpdatedAt: Date = Date()
    ) {
        self.historyId = historyId
        self.threatId = threatId
        self.positions = positions
        self.events = events
        self.lapScore = lapScore
        self.lapAnswers = lapAnswers
        self.startedAt = startedAt
        self.lastUpdatedAt = lastUpdatedAt
    }
}

/// A single position reading in a threat's tracked path.
struct ThreatPosition: Codable, Hashable {
    var latitude: Double
    var longitude: Double
    var floorLevel: Int?
    var heading: Double?
    var speedMps: Double?
    var timestamp: Date
    var source: ThreatDetectionMethod

    init(
        latitude: Double = 0,
        longitude: Double = 0,
        floorLevel: Int? = nil,
        heading: Double? = nil,
        speedMps: Double? = nil,
        timestamp: Date = Date(),
        source: ThreatDetectionMethod = .sensorFusion
    ) {
        self.latitude = latitude
        self.longitude = longitude
        self.floorLevel = floorLevel
        self.heading = heading
        self.speedMps = speedMps
        self.timestamp = timestamp
        self.source = source
    }
}

/// A discrete event in a threat's timeline.
/// EventType known values: "WeaponDischarged", "EntryForced", "HostageTaken",
/// "VictimInjured", "ThreatNeutralized", "ThreatFled".
struct ThreatEvent: Codable, Hashable, Identifiable {
    var id: String { eventId }
    let eventId: String
    var threatId: String
    var eventType: String
    var timestamp: Date
    var description: String

    init(
        eventId: String = UUID().uuidString,
        threatId: String = "",
        eventType: String = "",
        timestamp: Date = Date(),
        description: String = ""
    ) {
        self.eventId = eventId
        self.threatId = threatId
        self.eventType = eventType
        self.timestamp = timestamp
        self.description = description
    }
}

/// An answer to one of the 11 DOJ/Johns Hopkins Lethality Assessment Protocol questions.
/// Score >= 7 or Q1 "yes" = "high danger" per the Maryland Model.
struct LAPAnswer: Codable, Hashable {
    var questionNumber: Int
    var questionText: String
    var answer: Bool?
    var weight: Int

    init(
        questionNumber: Int = 0,
        questionText: String = "",
        answer: Bool? = nil,
        weight: Int = 1
    ) {
        self.questionNumber = questionNumber
        self.questionText = questionText
        self.answer = answer
        self.weight = weight
    }
}

/// An edge in the egress graph blocked by a threat.
struct BlockedEgressEdge: Codable, Hashable, Identifiable {
    var id: String { pathId }
    var pathId: String
    var threatId: String
    var reason: BlocksEgressReason
    var detectedAt: Date
    var estimatedClearAt: Date?

    init(
        pathId: String = "",
        threatId: String = "",
        reason: BlocksEgressReason = .directPresence,
        detectedAt: Date = Date(),
        estimatedClearAt: Date? = nil
    ) {
        self.pathId = pathId
        self.threatId = threatId
        self.reason = reason
        self.detectedAt = detectedAt
        self.estimatedClearAt = estimatedClearAt
    }
}

/// A safe harbor location where a person can shelter during a threat event.
/// For DV situations, harbors known to the perpetrator are excluded.
struct SafeHarbor: Codable, Hashable, Identifiable {
    var id: String { harborId }
    let harborId: String
    var name: String
    var roomId: String?
    var latitude: Double
    var longitude: Double
    var isKnownToThreat: Bool
    var hasSafeRoom: Bool
    var availableNow: Bool
    var capacity: Int
    var distanceMeters: Double
    var contactPhone: String?

    init(
        harborId: String = UUID().uuidString,
        name: String = "",
        roomId: String? = nil,
        latitude: Double = 0,
        longitude: Double = 0,
        isKnownToThreat: Bool = false,
        hasSafeRoom: Bool = false,
        availableNow: Bool = true,
        capacity: Int = 1,
        distanceMeters: Double = 0,
        contactPhone: String? = nil
    ) {
        self.harborId = harborId
        self.name = name
        self.roomId = roomId
        self.latitude = latitude
        self.longitude = longitude
        self.isKnownToThreat = isKnownToThreat
        self.hasSafeRoom = hasSafeRoom
        self.availableNow = availableNow
        self.capacity = capacity
        self.distanceMeters = distanceMeters
        self.contactPhone = contactPhone
    }

    var distanceDisplay: String {
        if distanceMeters < 1000 {
            return String(format: "%.0f m", distanceMeters)
        } else {
            return String(format: "%.1f km", distanceMeters / 1000)
        }
    }
}

/// An acoustic event detected by a microphone array or acoustic sensor.
/// Classification follows MIL-STD-1474E (gunshots) and UL 639 (glass break).
struct AcousticEvent: Codable, Hashable, Identifiable {
    var id: String { eventId }
    let eventId: String
    var type: AcousticEventType
    var confidence: Float
    var decibelLevel: Double
    var latitude: Double
    var longitude: Double
    var timestamp: Date
    var sensorId: String
    var rawSignatureHash: String

    init(
        eventId: String = UUID().uuidString,
        type: AcousticEventType = .silence,
        confidence: Float = 0,
        decibelLevel: Double = 0,
        latitude: Double = 0,
        longitude: Double = 0,
        timestamp: Date = Date(),
        sensorId: String = "",
        rawSignatureHash: String = ""
    ) {
        self.eventId = eventId
        self.type = type
        self.confidence = confidence
        self.decibelLevel = decibelLevel
        self.latitude = latitude
        self.longitude = longitude
        self.timestamp = timestamp
        self.sensorId = sensorId
        self.rawSignatureHash = rawSignatureHash
    }
}

/// A reading from a door sensor (Z-Wave, Zigbee, WiFi, or BLE).
struct DoorSensorReading: Codable, Hashable {
    var sensorId: String
    var doorId: String
    var roomId: String
    var event: DoorSensorEvent
    var protocolType: String
    var timestamp: Date
    var batteryPercent: Int?

    init(
        sensorId: String = "",
        doorId: String = "",
        roomId: String = "",
        event: DoorSensorEvent = .normalClose,
        protocolType: String = "ZWave",
        timestamp: Date = Date(),
        batteryPercent: Int? = nil
    ) {
        self.sensorId = sensorId
        self.doorId = doorId
        self.roomId = roomId
        self.event = event
        self.protocolType = protocolType
        self.timestamp = timestamp
        self.batteryPercent = batteryPercent
    }

    enum CodingKeys: String, CodingKey {
        case sensorId, doorId, roomId, event
        case protocolType = "protocol"
        case timestamp, batteryPercent
    }
}

/// A reading from a glass break sensor, classified per UL 639.
struct GlassBreakReading: Codable, Hashable {
    var sensorId: String
    var windowId: String
    var roomId: String
    var confidence: Float
    var frequencyHz: Double
    var decibelLevel: Double
    var breakPatternType: String
    var timestamp: Date

    init(
        sensorId: String = "",
        windowId: String = "",
        roomId: String = "",
        confidence: Float = 0,
        frequencyHz: Double = 0,
        decibelLevel: Double = 0,
        breakPatternType: String = "Impact",
        timestamp: Date = Date()
    ) {
        self.sensorId = sensorId
        self.windowId = windowId
        self.roomId = roomId
        self.confidence = confidence
        self.frequencyHz = frequencyHz
        self.decibelLevel = decibelLevel
        self.breakPatternType = breakPatternType
        self.timestamp = timestamp
    }
}

/// Configuration for stealth mode — designed for DV victims.
/// When enabled: no audible alerts, screen dims, notifications are silent,
/// and a duress code can fake a "safe" response while silently alerting responders.
struct StealthModeConfig: Codable, Hashable {
    var isEnabled: Bool
    var silentNotifications: Bool
    var screenDimmed: Bool
    var noAudibleAlerts: Bool
    var preferTextOnly: Bool
    var duressCode: String?
    var safeWordPhrase: String?
    var trustedContactIds: [String]
    var childInHidingPriority: Bool

    init(
        isEnabled: Bool = false,
        silentNotifications: Bool = false,
        screenDimmed: Bool = false,
        noAudibleAlerts: Bool = false,
        preferTextOnly: Bool = false,
        duressCode: String? = nil,
        safeWordPhrase: String? = nil,
        trustedContactIds: [String] = [],
        childInHidingPriority: Bool = false
    ) {
        self.isEnabled = isEnabled
        self.silentNotifications = silentNotifications
        self.screenDimmed = screenDimmed
        self.noAudibleAlerts = noAudibleAlerts
        self.preferTextOnly = preferTextOnly
        self.duressCode = duressCode
        self.safeWordPhrase = safeWordPhrase
        self.trustedContactIds = trustedContactIds
        self.childInHidingPriority = childInHidingPriority
    }
}

// MARK: - LAP Questions

/// The 11 standard questions of the DOJ/Johns Hopkins Lethality Assessment Protocol.
/// A "yes" to Q1 automatically qualifies as "high danger."
/// Total weighted score >= 7 also qualifies as "high danger."
///
/// Source: Maryland Network Against Domestic Violence / Johns Hopkins School of Nursing.
/// Validated in: Campbell, J.C. et al. (2009). "The Lethality Screen."
///
/// Example:
///   let answers = LAPQuestions.allQuestions.enumerated().map { index, question in
///       LAPAnswer(questionNumber: index + 1, questionText: question, weight: LAPQuestions.weights[index])
///   }
enum LAPQuestions {
    static let q1 = "Has he/she ever used a weapon against you or threatened you with a weapon?"
    static let q2 = "Has he/she threatened to kill you or your children?"
    static let q3 = "Do you think he/she might try to kill you?"
    static let q4 = "Does he/she have a gun or can he/she get one easily?"
    static let q5 = "Has he/she ever tried to choke (strangle) you?"
    static let q6 = "Is he/she violently or constantly jealous or does he/she control most of your daily activities?"
    static let q7 = "Have you left or separated from him/her after living together or being married?"
    static let q8 = "Is he/she unemployed?"
    static let q9 = "Has he/she ever tried to kill himself/herself?"
    static let q10 = "Do you have a child that he/she knows is not his/hers?"
    static let q11 = "Does he/she follow or spy on you or leave threatening messages?"

    static let allQuestions: [String] = [
        q1, q2, q3, q4, q5, q6, q7, q8, q9, q10, q11
    ]

    /// Weights: Q1 = 4 (automatic high danger if yes), all others = 1.
    static let weights: [Int] = [
        4, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1
    ]

    /// Score threshold at or above which the result is "high danger" per the Maryland Model.
    static let highDangerThreshold = 7
}
