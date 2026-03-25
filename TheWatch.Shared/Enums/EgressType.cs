// EgressType — enumerates all types related to Egress & Escape Routing (Run-Hide-Fight).
//
// This file covers the graph-edge and node classification for indoor egress computation,
// adversarial pathfinding (threat-avoidance A*), and the Run-Hide-Fight decision model.
//
// Standards referenced:
//   - NFPA 101 (Life Safety Code) — corridor widths, dead-end limits, panic hardware
//   - IBC (International Building Code) — travel distance limits, occupant load factors
//   - IRC R310 — emergency escape and rescue openings (residential)
//   - ADA — accessible means of egress (door widths, ramp requirements)
//   - FEMA Run-Hide-Fight guidance (DHS Active Shooter preparedness)
//
// Architecture:
//   Structure → FloorPlan[] → Room[] + EgressPath[] (weighted directed graph)
//   Each EgressPath is an edge with weight = f(distance, threat proximity, accessibility).
//   Rooms are nodes. Openings are edge attributes (door type, width, fire rating).
//   The graph supports:
//     1. Dijkstra/A* for shortest safe egress route
//     2. Adversarial A* with threat-repulsion potential field: cost += K / dist(threat)^2
//     3. Steiner tree for multi-person group evacuation to a common assembly point
//     4. Hide-location scoring (lockability, concealment, alternate egress, wall rating)
//     5. Run-Hide-Fight decision engine combining route availability + hide scores
//
// Enum value range: 500–599 (reserved block for egress domain).
//
// Example:
//   var path = new EgressPath { PathType = EgressPathType.Corridor, Status = HazardEdgeStatus.Clear };
//   var decision = new RunHideFightDecision { Action = RunHideFightAction.Run };

namespace TheWatch.Shared.Enums;

/// <summary>
/// Classification of an egress path segment in the structure graph.
/// Each edge in the egress graph has a path type that determines traversal cost,
/// accessibility filtering, and hazard-pruning behavior.
/// </summary>
public enum EgressPathType
{
    /// <summary>Standard hallway or corridor. Min width 44" per NFPA 101 7.3.4.1.</summary>
    Corridor = 500,

    /// <summary>Enclosed stairwell. Primary vertical egress for ambulatory occupants.</summary>
    Stairwell = 501,

    /// <summary>
    /// Elevator shaft. NOT a valid egress path during fire events (NFPA 101 7.2.13).
    /// May be used for non-fire emergencies (active shooter, medical) if power is stable.
    /// Automatically pruned from the graph when fire hazard edges are present.
    /// </summary>
    Elevator = 502,

    /// <summary>ADA-compliant ramp. Required for wheelchair-accessible egress routes.</summary>
    Ramp = 503,

    /// <summary>Standard door transition between rooms. See EgressOpening for door specifics.</summary>
    Door = 504,

    /// <summary>
    /// Window egress. Valid for residential emergency escape per IRC R310
    /// (min 20" wide, 24" high, 5.7 sq ft net clear opening, max 44" sill height).
    /// </summary>
    Window = 505,

    /// <summary>Exterior fire escape (ladder or stairway). Common in older multi-story buildings.</summary>
    FireEscape = 506,

    /// <summary>Roof access path. Used when ground-level egress is fully blocked.</summary>
    RoofAccess = 507,

    /// <summary>Dedicated emergency exit path with illuminated signage and panic hardware.</summary>
    EmergencyExit = 508,

    /// <summary>Underground tunnel or utility passage. Common in campus and hospital settings.</summary>
    TunnelPassage = 509,

    /// <summary>
    /// Shelter-in-place location. Not a traversal path but a terminal node
    /// used when all egress routes are blocked (e.g., tornado, chemical spill).
    /// </summary>
    ShelterInPlace = 510
}

/// <summary>
/// Classification of a physical opening (door, window, hatch) that an egress path traverses.
/// Determines whether the opening is passable, fire-rated, ADA-compliant, or blocked during emergency.
/// </summary>
public enum EgressOpeningType
{
    /// <summary>Standard hinged door. Min 32" clear width per ADA 404.2.3.</summary>
    StandardDoor = 520,

    /// <summary>Fire-rated door assembly (20-min, 45-min, 60-min, 90-min ratings per NFPA 80).</summary>
    FireRatedDoor = 521,

    /// <summary>Emergency exit door with panic hardware per NFPA 101 7.2.1.7.</summary>
    EmergencyExitDoor = 522,

    /// <summary>Operable window meeting emergency escape requirements (IRC R310).</summary>
    Window = 523,

    /// <summary>Roll-up door (garage, warehouse). May require manual or powered operation.</summary>
    RollupDoor = 524,

    /// <summary>Sliding door (patio, pocket). ADA-compliant if min 32" clear opening.</summary>
    SlidingDoor = 525,

    /// <summary>
    /// Revolving door. BLOCKED during emergency per NFPA 101 7.2.1.10 —
    /// must have adjacent swing door or be collapsible to book-fold configuration.
    /// Automatically pruned from egress graph during active emergencies.
    /// </summary>
    RevolvingDoor = 526,

    /// <summary>Floor or ceiling hatch. Used for roof access or crawl-space egress.</summary>
    Hatch = 527,

    /// <summary>
    /// Break-glass emergency panel (fire alarm pull station, emergency tool cabinet).
    /// Requires physical breaking action to operate; adds traversal time penalty.
    /// </summary>
    BreakGlass = 528
}

/// <summary>
/// The three actions in the DHS/FEMA Active Shooter response model.
/// The decision engine evaluates egress availability, hide quality, and threat proximity
/// to recommend one of these actions.
///
/// Decision priority (per DHS guidance):
///   1. RUN — if a safe egress route exists away from the threat
///   2. HIDE — if running is not possible, find a secure hiding location
///   3. FIGHT — last resort when life is in imminent danger
/// </summary>
public enum RunHideFightAction
{
    /// <summary>
    /// RUN: Evacuate via the safest available egress route.
    /// Recommended when at least one egress path exists that avoids the threat
    /// and the person is physically capable of traversing it.
    /// </summary>
    Run = 540,

    /// <summary>
    /// HIDE: Shelter in the best available concealment/barricade location.
    /// Recommended when no safe egress route exists but a lockable/concealable
    /// room is available with a hide score above threshold.
    /// </summary>
    Hide = 541,

    /// <summary>
    /// FIGHT: Confront the threat as a last resort.
    /// Recommended only when both Run and Hide are infeasible —
    /// all egress routes are blocked and no adequate hide location exists.
    /// </summary>
    Fight = 542
}

/// <summary>
/// Real-time status of an egress path edge. Updated by sensor feeds, manual reports,
/// or hazard-pruning algorithms. Blocked edges are removed from the traversable graph.
/// </summary>
public enum HazardEdgeStatus
{
    /// <summary>Path is clear and traversable. Normal weight applies.</summary>
    Clear = 550,

    /// <summary>
    /// Path is blocked (fire, debris, locked gate, structural collapse).
    /// Edge is pruned from the egress graph — no routing through this path.
    /// </summary>
    Blocked = 551,

    /// <summary>
    /// Path is compromised (smoke detected, partial obstruction, unreliable lighting).
    /// Edge remains in the graph but with a heavy penalty weight.
    /// </summary>
    Compromised = 552,

    /// <summary>
    /// Path status is unknown (sensor offline, no recent report).
    /// Edge remains in the graph with a moderate penalty weight.
    /// </summary>
    Unknown = 553
}

/// <summary>
/// Classification of a safe harbor / assembly point where evacuees gather
/// after exiting the structure. Used as the destination node in egress routing.
/// </summary>
public enum SafeHarborType
{
    /// <summary>Purpose-built safe room (reinforced walls, communications, supplies). Per FEMA P-320/P-361.</summary>
    DesignatedSafeRoom = 560,

    /// <summary>Existing room reinforced for shelter (interior bathroom, closet with solid walls).</summary>
    ReinforcedRoom = 561,

    /// <summary>Outdoor assembly point at a safe distance from the structure. Per OSHA/fire marshal.</summary>
    ExteriorAssemblyPoint = 562,

    /// <summary>Neighboring dwelling used as temporary refuge (pre-arranged with neighbor).</summary>
    NeighborDwelling = 563,

    /// <summary>Public shelter (community center, school gymnasium, government facility).</summary>
    PublicShelter = 564,

    /// <summary>Vehicle escape — evacuee reaches their vehicle and drives to safety.</summary>
    VehicleEscape = 565
}

/// <summary>
/// Accessibility classification of an egress path segment.
/// Used to filter routes based on person's mobility capabilities.
/// Aligns with ADA accessible means of egress requirements (IBC 1009).
/// </summary>
public enum EgressAccessibility
{
    /// <summary>Fully accessible: level path, wide doors, no steps. Wheelchair-independent.</summary>
    FullyAccessible = 570,

    /// <summary>Wheelchair-passable but may require assistance (heavy door, slight grade).</summary>
    WheelchairWithAssistance = 571,

    /// <summary>Ambulatory only: narrow passage, low clearance, or minor step. No wheelchair.</summary>
    AmbulatoryOnly = 572,

    /// <summary>Stairs required: no elevator or ramp alternative on this path segment.</summary>
    StairsRequired = 573,

    /// <summary>Not accessible: ladder, hatch, window egress, or severely obstructed path.</summary>
    NotAccessible = 574
}
