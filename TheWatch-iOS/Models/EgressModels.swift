import Foundation

// ═══════════════════════════════════════════════════════════════
// EgressModels.swift — Egress & Escape Routing (Run-Hide-Fight) for iOS
//
// Mirrors the C# domain models in TheWatch.Shared for mobile client use.
// Standards: NFPA 101, IBC, IRC R310, ADA, FEMA Run-Hide-Fight.
//
// Example:
//   let decision = RunHideFightDecision(
//       action: .run,
//       confidence: 0.92,
//       runRoute: [corridorPath, stairwellPath, exitPath],
//       reasoning: "Clear egress via west stairwell. 45s to assembly point."
//   )
// ═══════════════════════════════════════════════════════════════

// MARK: - Enums

/// Classification of an egress path segment in the structure graph.
enum EgressPathType: String, Codable, CaseIterable {
    case corridor = "Corridor"
    case stairwell = "Stairwell"
    case elevator = "Elevator"           // NOT valid during fire events (NFPA 101 7.2.13)
    case ramp = "Ramp"
    case door = "Door"
    case window = "Window"
    case fireEscape = "FireEscape"
    case roofAccess = "RoofAccess"
    case emergencyExit = "EmergencyExit"
    case tunnelPassage = "TunnelPassage"
    case shelterInPlace = "ShelterInPlace"
}

/// Classification of a physical opening (door, window, hatch) on an egress path.
enum EgressOpeningType: String, Codable, CaseIterable {
    case standardDoor = "StandardDoor"
    case fireRatedDoor = "FireRatedDoor"
    case emergencyExitDoor = "EmergencyExitDoor"
    case window = "Window"
    case rollupDoor = "RollupDoor"
    case slidingDoor = "SlidingDoor"
    case revolvingDoor = "RevolvingDoor"   // BLOCKED during emergency (NFPA 101 7.2.1.10)
    case hatch = "Hatch"
    case breakGlass = "BreakGlass"
}

/// DHS/FEMA Active Shooter response actions. Priority: Run > Hide > Fight.
enum RunHideFightAction: String, Codable, CaseIterable {
    case run = "Run"
    case hide = "Hide"
    case fight = "Fight"
}

/// Real-time traversability status of an egress path edge.
enum HazardEdgeStatus: String, Codable, CaseIterable {
    case clear = "Clear"
    case blocked = "Blocked"
    case compromised = "Compromised"
    case unknown = "Unknown"
}

/// Classification of safe harbor / assembly point types.
enum SafeHarborType: String, Codable, CaseIterable {
    case designatedSafeRoom = "DesignatedSafeRoom"
    case reinforcedRoom = "ReinforcedRoom"
    case exteriorAssemblyPoint = "ExteriorAssemblyPoint"
    case neighborDwelling = "NeighborDwelling"
    case publicShelter = "PublicShelter"
    case vehicleEscape = "VehicleEscape"
}

/// Accessibility classification of an egress path segment (ADA / IBC 1009).
enum EgressAccessibility: String, Codable, CaseIterable {
    case fullyAccessible = "FullyAccessible"
    case wheelchairWithAssistance = "WheelchairWithAssistance"
    case ambulatoryOnly = "AmbulatoryOnly"
    case stairsRequired = "StairsRequired"
    case notAccessible = "NotAccessible"
}

// MARK: - Constants

/// Building-code constants for egress computation.
/// Values are statutory minimums from NFPA 101, IBC, IRC, and ADA.
struct EgressConstants {
    /// Min corridor width in inches (NFPA 101 7.3.4.1)
    static let minCorridorWidthInches: Double = 44.0
    /// Min door clear width in inches (ADA 404.2.3)
    static let minDoorWidthInches: Double = 32.0
    /// Min emergency window width in inches (IRC R310.2.1)
    static let minEmergencyWindowInches: Double = 20.0
    /// Min emergency window area in sq ft (IRC R310.1)
    static let minEmergencyWindowAreaSqFt: Double = 5.7
    /// Max dead-end corridor length in feet (NFPA 101)
    static let maxDeadEndFeet: Double = 20.0
    /// Max travel distance to exit in feet (IBC 1017.1, unsprinklered)
    static let maxTravelDistanceFeet: Double = 200.0
    /// Max window sill height in inches (IRC R310.2.2)
    static let maxWindowSillHeightInches: Double = 44.0
    /// Min emergency window height in inches (IRC R310.2.1)
    static let minEmergencyWindowHeightInches: Double = 24.0
    /// Default hide score threshold for HIDE recommendation
    static let defaultHideScoreThreshold: Double = 10.0
    /// Default threat avoidance K for adversarial A*
    static let defaultThreatAvoidanceK: Double = 100.0
}

// MARK: - Structure Graph Models

/// Top-level structure (building, dwelling) containing floors.
/// Root of the egress graph.
struct Structure: Codable, Hashable, Identifiable {
    var id: String { structureId }
    let structureId: String
    var name: String
    var address: String
    var apn: String?
    var floors: [FloorPlan]
    var latitude: Double
    var longitude: Double
    var lastUpdated: Date

    init(
        structureId: String = UUID().uuidString,
        name: String = "",
        address: String = "",
        apn: String? = nil,
        floors: [FloorPlan] = [],
        latitude: Double = 0,
        longitude: Double = 0,
        lastUpdated: Date = Date()
    ) {
        self.structureId = structureId
        self.name = name
        self.address = address
        self.apn = apn
        self.floors = floors
        self.latitude = latitude
        self.longitude = longitude
        self.lastUpdated = lastUpdated
    }
}

/// A single floor within a structure. Contains rooms (nodes) and egress paths (edges).
struct FloorPlan: Codable, Hashable {
    /// 0 = ground floor, 1 = second floor, -1 = first basement
    var floorLevel: Int
    var rooms: [Room]
    var egressPaths: [EgressPath]

    init(
        floorLevel: Int = 0,
        rooms: [Room] = [],
        egressPaths: [EgressPath] = []
    ) {
        self.floorLevel = floorLevel
        self.rooms = rooms
        self.egressPaths = egressPaths
    }
}

/// A room within a structure floor. Graph node for egress computation.
struct Room: Codable, Hashable, Identifiable {
    var id: String { roomId }
    let roomId: String
    var floorLevel: Int
    /// "Bedroom", "Kitchen", "Office", "Bathroom", "Hallway", "Stairwell",
    /// "LivingRoom", "Garage", "Basement", "Attic", "Closet", "SafeRoom"
    var roomType: String
    var areaSqFt: Double
    /// Max occupant load per IBC Table 1004.5
    var occupantLoad: Int
    var hasWindow: Bool
    var hasPhone: Bool
    /// e.g., "UnderDesk", "BehindFurniture", "InCloset", "UnderBed"
    var concealmentOptions: [String]
    var openings: [EgressOpening]

    init(
        roomId: String = UUID().uuidString,
        floorLevel: Int = 0,
        roomType: String = "",
        areaSqFt: Double = 0,
        occupantLoad: Int = 0,
        hasWindow: Bool = false,
        hasPhone: Bool = false,
        concealmentOptions: [String] = [],
        openings: [EgressOpening] = []
    ) {
        self.roomId = roomId
        self.floorLevel = floorLevel
        self.roomType = roomType
        self.areaSqFt = areaSqFt
        self.occupantLoad = occupantLoad
        self.hasWindow = hasWindow
        self.hasPhone = hasPhone
        self.concealmentOptions = concealmentOptions
        self.openings = openings
    }
}

/// An egress path segment — directed edge in the structure graph.
struct EgressPath: Codable, Hashable, Identifiable {
    var id: String { pathId }
    let pathId: String
    var fromRoomId: String
    var toRoomId: String
    var pathType: EgressPathType
    var lengthMeters: Double
    /// Min 44" (1.12m) for corridors per NFPA 101 7.3.4.1
    var widthMeters: Double
    var accessibility: EgressAccessibility
    var travelTimeSeconds: Double
    var isLit: Bool
    var hasEmergencyLighting: Bool
    var status: HazardEdgeStatus
    var blockedReason: String?

    init(
        pathId: String = UUID().uuidString,
        fromRoomId: String = "",
        toRoomId: String = "",
        pathType: EgressPathType = .corridor,
        lengthMeters: Double = 0,
        widthMeters: Double = 1.12,
        accessibility: EgressAccessibility = .fullyAccessible,
        travelTimeSeconds: Double = 0,
        isLit: Bool = true,
        hasEmergencyLighting: Bool = false,
        status: HazardEdgeStatus = .clear,
        blockedReason: String? = nil
    ) {
        self.pathId = pathId
        self.fromRoomId = fromRoomId
        self.toRoomId = toRoomId
        self.pathType = pathType
        self.lengthMeters = lengthMeters
        self.widthMeters = widthMeters
        self.accessibility = accessibility
        self.travelTimeSeconds = travelTimeSeconds
        self.isLit = isLit
        self.hasEmergencyLighting = hasEmergencyLighting
        self.status = status
        self.blockedReason = blockedReason
    }
}

/// A physical opening (door, window, hatch) on an egress path.
struct EgressOpening: Codable, Hashable, Identifiable {
    var id: String { openingId }
    let openingId: String
    var type: EgressOpeningType
    /// Min 32" for ADA, 20" for emergency window (IRC R310.2.1)
    var widthInches: Double
    /// Min 24" for emergency window (IRC R310.2.1)
    var heightInches: Double
    var fireRatingMinutes: Int
    var isLocked: Bool
    /// Per NFPA 101 7.2.1.7 — required for occupant load > 100
    var hasPanicHardware: Bool
    var isAccessible: Bool

    init(
        openingId: String = UUID().uuidString,
        type: EgressOpeningType = .standardDoor,
        widthInches: Double = 36,
        heightInches: Double = 80,
        fireRatingMinutes: Int = 0,
        isLocked: Bool = false,
        hasPanicHardware: Bool = false,
        isAccessible: Bool = true
    ) {
        self.openingId = openingId
        self.type = type
        self.widthInches = widthInches
        self.heightInches = heightInches
        self.fireRatingMinutes = fireRatingMinutes
        self.isLocked = isLocked
        self.hasPanicHardware = hasPanicHardware
        self.isAccessible = isAccessible
    }
}

// MARK: - Adversarial A* Parameters

/// A known threat location with influence radius.
struct ThreatLocation: Codable, Hashable {
    var latitude: Double
    var longitude: Double
    /// Edges within this radius receive maximum repulsion penalty
    var radiusMeters: Double

    init(
        latitude: Double = 0,
        longitude: Double = 0,
        radiusMeters: Double = 10
    ) {
        self.latitude = latitude
        self.longitude = longitude
        self.radiusMeters = radiusMeters
    }
}

/// Parameters for adversarial A* pathfinding with threat-repulsion potential field.
/// Cost function: edge_cost += K / dist(edge_midpoint, threat)^2
struct AdversarialAStarParams: Codable, Hashable {
    var threatLocations: [ThreatLocation]
    /// K in K/dist^2. Higher = wider berth. Range: 50–500.
    var threatAvoidanceK: Double
    /// 1.0 = full mobility, 0.5 = reduced, 0.0 = immobile
    var personMobilityWeight: Double
    /// Prefer lit paths (adds penalty to unlit edges)
    var preferLitPaths: Bool

    init(
        threatLocations: [ThreatLocation] = [],
        threatAvoidanceK: Double = EgressConstants.defaultThreatAvoidanceK,
        personMobilityWeight: Double = 1.0,
        preferLitPaths: Bool = true
    ) {
        self.threatLocations = threatLocations
        self.threatAvoidanceK = threatAvoidanceK
        self.personMobilityWeight = personMobilityWeight
        self.preferLitPaths = preferLitPaths
    }
}

// MARK: - Hide-Location Scoring

/// Scoring result for a room evaluated as a potential hide location.
/// 0–19 point rubric with possible -10 line-of-sight penalty.
///
/// Rubric:
///   +5  Door lockable    +3  Solid door    +2  Wall rating
///   +4  Alternate egress +2  Distance      +3  Phone available
///   +2  Concealment     -10  Line-of-sight penalty
struct HideScoreResult: Codable, Hashable {
    var roomId: String
    var totalScore: Double
    var doorLockableScore: Double      // 0 or 5
    var doorSolidScore: Double         // 0 or 3
    var wallRatingScore: Double        // 0–2
    var altEgressScore: Double         // 0 or 4
    var distanceFromThreatScore: Double // 0–2
    var phoneAvailableScore: Double    // 0 or 3
    var concealmentScore: Double       // 0–2
    var lineOfSightPenalty: Double     // 0 or -10
    var recommendation: String

    init(
        roomId: String = "",
        totalScore: Double = 0,
        doorLockableScore: Double = 0,
        doorSolidScore: Double = 0,
        wallRatingScore: Double = 0,
        altEgressScore: Double = 0,
        distanceFromThreatScore: Double = 0,
        phoneAvailableScore: Double = 0,
        concealmentScore: Double = 0,
        lineOfSightPenalty: Double = 0,
        recommendation: String = ""
    ) {
        self.roomId = roomId
        self.totalScore = totalScore
        self.doorLockableScore = doorLockableScore
        self.doorSolidScore = doorSolidScore
        self.wallRatingScore = wallRatingScore
        self.altEgressScore = altEgressScore
        self.distanceFromThreatScore = distanceFromThreatScore
        self.phoneAvailableScore = phoneAvailableScore
        self.concealmentScore = concealmentScore
        self.lineOfSightPenalty = lineOfSightPenalty
        self.recommendation = recommendation
    }
}

// MARK: - Run-Hide-Fight Decision

/// Output of the Run-Hide-Fight decision engine.
struct RunHideFightDecision: Codable, Hashable {
    var action: RunHideFightAction
    /// 0.0–1.0. Low confidence (<0.5) = uncertain data.
    var confidence: Float
    /// Recommended egress route if action is .run. Nil otherwise.
    var runRoute: [EgressPath]?
    /// Recommended hide room if action is .hide. Nil otherwise.
    var hideRoom: Room?
    /// Hide score for recommended room if action is .hide. Nil otherwise.
    var hideScore: HideScoreResult?
    var reasoning: String
    var decidedAt: Date

    init(
        action: RunHideFightAction = .run,
        confidence: Float = 0,
        runRoute: [EgressPath]? = nil,
        hideRoom: Room? = nil,
        hideScore: HideScoreResult? = nil,
        reasoning: String = "",
        decidedAt: Date = Date()
    ) {
        self.action = action
        self.confidence = confidence
        self.runRoute = runRoute
        self.hideRoom = hideRoom
        self.hideScore = hideScore
        self.reasoning = reasoning
        self.decidedAt = decidedAt
    }
}

// MARK: - Group Evacuation

/// A participant in a group evacuation.
struct EvacuationParticipant: Codable, Hashable {
    var userId: String
    var roomId: String

    init(userId: String = "", roomId: String = "") {
        self.userId = userId
        self.roomId = roomId
    }
}

/// Group evacuation plan computed via Steiner tree algorithm.
/// Minimum-cost tree connecting all participant rooms to a common assembly point.
struct GroupEvacuationPlan: Codable, Hashable, Identifiable {
    var id: String { planId }
    let planId: String
    var structureId: String
    var participants: [EvacuationParticipant]
    var steinerTreePaths: [EgressPath]
    var assemblyPointId: String
    var estimatedTotalTimeSeconds: Double
    var createdAt: Date

    init(
        planId: String = UUID().uuidString,
        structureId: String = "",
        participants: [EvacuationParticipant] = [],
        steinerTreePaths: [EgressPath] = [],
        assemblyPointId: String = "",
        estimatedTotalTimeSeconds: Double = 0,
        createdAt: Date = Date()
    ) {
        self.planId = planId
        self.structureId = structureId
        self.participants = participants
        self.steinerTreePaths = steinerTreePaths
        self.assemblyPointId = assemblyPointId
        self.estimatedTotalTimeSeconds = estimatedTotalTimeSeconds
        self.createdAt = createdAt
    }
}
