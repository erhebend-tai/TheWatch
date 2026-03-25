package com.thewatch.app.data.model

// ═══════════════════════════════════════════════════════════════
// EgressModels.kt — Egress & Escape Routing (Run-Hide-Fight) for Android
//
// Mirrors the C# domain models in TheWatch.Shared for mobile client use.
// Standards: NFPA 101, IBC, IRC R310, ADA, FEMA Run-Hide-Fight.
//
// Example:
//   val decision = RunHideFightDecision(
//       action = RunHideFightAction.Run,
//       confidence = 0.92f,
//       runRoute = listOf(corridorPath, stairwellPath, exitPath),
//       reasoning = "Clear egress via west stairwell. 45s to assembly point."
//   )
// ═══════════════════════════════════════════════════════════════

// ── Enums ────────────────────────────────────────────────────

/**
 * Classification of an egress path segment in the structure graph.
 * Each edge in the egress graph has a path type that determines traversal cost,
 * accessibility filtering, and hazard-pruning behavior.
 */
enum class EgressPathType {
    Corridor,
    Stairwell,
    Elevator,        // NOT valid during fire events (NFPA 101 7.2.13)
    Ramp,
    Door,
    Window,
    FireEscape,
    RoofAccess,
    EmergencyExit,
    TunnelPassage,
    ShelterInPlace
}

/**
 * Classification of a physical opening (door, window, hatch) on an egress path.
 */
enum class EgressOpeningType {
    StandardDoor,
    FireRatedDoor,
    EmergencyExitDoor,
    Window,
    RollupDoor,
    SlidingDoor,
    RevolvingDoor,   // BLOCKED during emergency (NFPA 101 7.2.1.10)
    Hatch,
    BreakGlass
}

/**
 * DHS/FEMA Active Shooter response actions.
 * Decision priority: Run (if safe route exists) > Hide (if score >= threshold) > Fight (last resort).
 */
enum class RunHideFightAction {
    Run,
    Hide,
    Fight
}

/**
 * Real-time traversability status of an egress path edge.
 * Blocked edges are pruned from the traversable graph.
 */
enum class HazardEdgeStatus {
    Clear,
    Blocked,
    Compromised,
    Unknown
}

/**
 * Classification of safe harbor / assembly point types.
 */
enum class SafeHarborType {
    DesignatedSafeRoom,
    ReinforcedRoom,
    ExteriorAssemblyPoint,
    NeighborDwelling,
    PublicShelter,
    VehicleEscape
}

/**
 * Accessibility classification of an egress path segment (ADA / IBC 1009).
 */
enum class EgressAccessibility {
    FullyAccessible,
    WheelchairWithAssistance,
    AmbulatoryOnly,
    StairsRequired,
    NotAccessible
}

// ── Constants ────────────────────────────────────────────────

/**
 * Building-code constants for egress computation.
 * All values are statutory minimums from NFPA 101, IBC, IRC, and ADA.
 */
object EgressConstants {
    /** Min corridor width in inches (NFPA 101 7.3.4.1) */
    const val MIN_CORRIDOR_WIDTH_INCHES: Double = 44.0
    /** Min door clear width in inches (ADA 404.2.3) */
    const val MIN_DOOR_WIDTH_INCHES: Double = 32.0
    /** Min emergency window width in inches (IRC R310.2.1) */
    const val MIN_EMERGENCY_WINDOW_INCHES: Double = 20.0
    /** Min emergency window area in sq ft (IRC R310.1) */
    const val MIN_EMERGENCY_WINDOW_AREA_SQFT: Double = 5.7
    /** Max dead-end corridor length in feet (NFPA 101) */
    const val MAX_DEAD_END_FEET: Double = 20.0
    /** Max travel distance to exit in feet (IBC 1017.1, unsprinklered) */
    const val MAX_TRAVEL_DISTANCE_FEET: Double = 200.0
    /** Max window sill height in inches (IRC R310.2.2) */
    const val MAX_WINDOW_SILL_HEIGHT_INCHES: Double = 44.0
    /** Min emergency window height in inches (IRC R310.2.1) */
    const val MIN_EMERGENCY_WINDOW_HEIGHT_INCHES: Double = 24.0
    /** Default hide score threshold for HIDE recommendation */
    const val DEFAULT_HIDE_SCORE_THRESHOLD: Double = 10.0
    /** Default threat avoidance K for adversarial A* */
    const val DEFAULT_THREAT_AVOIDANCE_K: Double = 100.0
}

// ── Structure Graph Models ───────────────────────────────────

/**
 * Top-level structure (building, dwelling) containing floors.
 * Root of the egress graph.
 *
 * @property structureId Unique structure identifier
 * @property name Human-readable name
 * @property address Street address
 * @property apn Assessor Parcel Number — county-assigned parcel ID
 * @property floors All floors in the structure (0 = ground, negative = basement)
 * @property latitude Latitude (WGS 84)
 * @property longitude Longitude (WGS 84)
 * @property lastUpdated When structure data was last updated (ISO 8601)
 */
data class Structure(
    val structureId: String = "",
    val name: String = "",
    val address: String = "",
    val apn: String? = null,
    val floors: List<FloorPlan> = emptyList(),
    val latitude: Double = 0.0,
    val longitude: Double = 0.0,
    val lastUpdated: String = ""
)

/**
 * A single floor within a structure.
 * Contains rooms (graph nodes) and egress paths (graph edges).
 *
 * @property floorLevel 0 = ground, 1 = second floor, -1 = first basement
 */
data class FloorPlan(
    val floorLevel: Int = 0,
    val rooms: List<Room> = emptyList(),
    val egressPaths: List<EgressPath> = emptyList()
)

/**
 * A room within a structure floor. Graph node for egress computation.
 * Contains metadata for hide-location scoring.
 *
 * @property roomType Standard values: "Bedroom", "Kitchen", "Office", "Bathroom",
 *           "Hallway", "Stairwell", "LivingRoom", "Garage", "Basement", "Attic",
 *           "Closet", "SafeRoom"
 * @property occupantLoad Max occupant load per IBC Table 1004.5
 * @property concealmentOptions e.g., "UnderDesk", "BehindFurniture", "InCloset", "UnderBed"
 */
data class Room(
    val roomId: String = "",
    val floorLevel: Int = 0,
    val roomType: String = "",
    val areaSqFt: Double = 0.0,
    val occupantLoad: Int = 0,
    val hasWindow: Boolean = false,
    val hasPhone: Boolean = false,
    val concealmentOptions: List<String> = emptyList(),
    val openings: List<EgressOpening> = emptyList()
)

/**
 * An egress path segment — directed edge in the structure graph.
 *
 * @property widthMeters Min 44" (1.12m) for corridors per NFPA 101 7.3.4.1
 * @property travelTimeSeconds Estimated at 1.2 m/s walking speed (SFPE)
 * @property blockedReason Human-readable reason if status is Blocked or Compromised
 */
data class EgressPath(
    val pathId: String = "",
    val fromRoomId: String = "",
    val toRoomId: String = "",
    val pathType: EgressPathType = EgressPathType.Corridor,
    val lengthMeters: Double = 0.0,
    val widthMeters: Double = 1.12,
    val accessibility: EgressAccessibility = EgressAccessibility.FullyAccessible,
    val travelTimeSeconds: Double = 0.0,
    val isLit: Boolean = true,
    val hasEmergencyLighting: Boolean = false,
    val status: HazardEdgeStatus = HazardEdgeStatus.Clear,
    val blockedReason: String? = null
)

/**
 * A physical opening (door, window, hatch) on an egress path.
 *
 * @property widthInches Min 32" for ADA, 20" for emergency window (IRC R310.2.1)
 * @property heightInches Min 24" for emergency window (IRC R310.2.1)
 * @property fireRatingMinutes 0, 20, 45, 60, or 90 per NFPA 80
 * @property hasPanicHardware Per NFPA 101 7.2.1.7 — required for occupant load > 100
 */
data class EgressOpening(
    val openingId: String = "",
    val type: EgressOpeningType = EgressOpeningType.StandardDoor,
    val widthInches: Double = 36.0,
    val heightInches: Double = 80.0,
    val fireRatingMinutes: Int = 0,
    val isLocked: Boolean = false,
    val hasPanicHardware: Boolean = false,
    val isAccessible: Boolean = true
)

// ── Adversarial A* Parameters ────────────────────────────────

/**
 * A known threat location with influence radius.
 * Edges within [radiusMeters] receive maximum repulsion penalty.
 */
data class ThreatLocation(
    val latitude: Double = 0.0,
    val longitude: Double = 0.0,
    val radiusMeters: Double = 10.0
)

/**
 * Parameters for adversarial A* pathfinding with threat-repulsion potential field.
 * Cost function: edge_cost += K / dist(edge_midpoint, threat)^2
 *
 * @property threatAvoidanceK K in K/dist^2. Higher = wider berth. Range: 50–500.
 * @property personMobilityWeight 1.0 = full mobility, 0.5 = reduced, 0.0 = immobile
 * @property preferLitPaths Prefer lit paths (adds penalty to unlit edges)
 */
data class AdversarialAStarParams(
    val threatLocations: List<ThreatLocation> = emptyList(),
    val threatAvoidanceK: Double = EgressConstants.DEFAULT_THREAT_AVOIDANCE_K,
    val personMobilityWeight: Double = 1.0,
    val preferLitPaths: Boolean = true
)

// ── Hide-Location Scoring ────────────────────────────────────

/**
 * Scoring result for a room evaluated as a potential hide location.
 * 0–19 point rubric with possible -10 line-of-sight penalty.
 *
 * Rubric:
 *   +5  Door lockable    +3  Solid door    +2  Wall rating
 *   +4  Alternate egress +2  Distance      +3  Phone available
 *   +2  Concealment     -10  Line-of-sight penalty
 *
 * Example: Locked bathroom with solid door, concrete walls, phone:
 *   5 + 3 + 2 + 0 + 1 + 3 + 1 = 15 → Strong HIDE recommendation
 */
data class HideScoreResult(
    val roomId: String = "",
    val totalScore: Double = 0.0,
    val doorLockableScore: Double = 0.0,     // 0 or 5
    val doorSolidScore: Double = 0.0,        // 0 or 3
    val wallRatingScore: Double = 0.0,       // 0–2
    val altEgressScore: Double = 0.0,        // 0 or 4
    val distanceFromThreatScore: Double = 0.0, // 0–2
    val phoneAvailableScore: Double = 0.0,   // 0 or 3
    val concealmentScore: Double = 0.0,      // 0–2
    val lineOfSightPenalty: Double = 0.0,    // 0 or -10
    val recommendation: String = ""
)

// ── Run-Hide-Fight Decision ──────────────────────────────────

/**
 * Output of the Run-Hide-Fight decision engine.
 *
 * @property confidence 0.0–1.0. Low confidence (<0.5) = uncertain data
 * @property runRoute Recommended egress route if action is Run. Null otherwise.
 * @property hideRoom Recommended hide room if action is Hide. Null otherwise.
 * @property hideScore Hide score for recommended room if action is Hide. Null otherwise.
 * @property decidedAt ISO 8601 UTC timestamp
 */
data class RunHideFightDecision(
    val action: RunHideFightAction = RunHideFightAction.Run,
    val confidence: Float = 0f,
    val runRoute: List<EgressPath>? = null,
    val hideRoom: Room? = null,
    val hideScore: HideScoreResult? = null,
    val reasoning: String = "",
    val decidedAt: String = ""
)

// ── Group Evacuation ─────────────────────────────────────────

/**
 * A participant in a group evacuation — a person in a specific room.
 */
data class EvacuationParticipant(
    val userId: String = "",
    val roomId: String = ""
)

/**
 * Group evacuation plan computed via Steiner tree algorithm.
 * Minimum-cost tree connecting all participant rooms to a common assembly point.
 *
 * Example:
 *   // Family of 4 in different rooms during a fire
 *   val plan = egressRepository.computeGroupEvacuation(
 *       structureId = "s-001",
 *       participantRoomIds = listOf("r-bedroom-01", "r-kitchen", "r-office", "r-bedroom-02")
 *   )
 *   // plan.steinerTreePaths = merged evacuation routes
 *   // plan.assemblyPointId = "r-assembly-front-yard"
 *   // plan.estimatedTotalTimeSeconds = 52.0
 */
data class GroupEvacuationPlan(
    val planId: String = "",
    val structureId: String = "",
    val participants: List<EvacuationParticipant> = emptyList(),
    val steinerTreePaths: List<EgressPath> = emptyList(),
    val assemblyPointId: String = "",
    val estimatedTotalTimeSeconds: Double = 0.0,
    val createdAt: String = ""
)
