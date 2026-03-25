// EgressModels — domain models for Egress & Escape Routing (Run-Hide-Fight).
//
// These models represent the indoor structure graph used for egress computation:
//   Structure → FloorPlan[] → Room[] (nodes) + EgressPath[] (edges)
//
// The graph supports five core algorithms:
//   1. Dijkstra shortest-path egress (ComputeEgressRouteAsync)
//   2. Adversarial A* with threat-repulsion field (ComputeAdversarialRouteAsync)
//      - Cost function: g(n) + h(n) + sum_over_threats(K / dist(n, threat_i)^2)
//      - K is the ThreatAvoidanceK parameter; higher K = wider threat berth
//   3. Hide-location scoring (ScoreHideLocationAsync)
//      - 0–19 point rubric: door lock (5), solid door (3), wall rating (2),
//        alternate egress (4), distance from threat (2), phone (3),
//        concealment (2), line-of-sight penalty (-10)
//   4. Run-Hide-Fight decision engine (DecideRunHideFightAsync)
//      - Priority: Run (if safe route exists) > Hide (if score >= threshold) > Fight
//   5. Steiner tree group evacuation (ComputeGroupEvacuationAsync)
//      - Minimum spanning tree over participant locations to a common assembly point
//
// Standards referenced:
//   - NFPA 101 Life Safety Code (corridor width 44", dead-end 20', panic hardware 7.2.1)
//   - IBC 1004.5 (occupant load factors), IBC 1017.1 (travel distance 200')
//   - IRC R310 (emergency window: 20" min width, 5.7 sq ft min area, 44" max sill)
//   - ADA 404.2.3 (door clear width 32")
//   - FEMA P-320/P-361 (safe room design), DHS Run-Hide-Fight model
//
// Example:
//   var structure = new Structure
//   {
//       StructureId = "s-001",
//       Name = "Main Residence",
//       Floors = new List<FloorPlan>
//       {
//           new FloorPlan
//           {
//               FloorLevel = 0,
//               Rooms = new List<Room> { /* ... */ },
//               EgressPaths = new List<EgressPath> { /* ... */ }
//           }
//       }
//   };
//
//   // Score a hide location
//   var hideScore = new HideScoreResult
//   {
//       RoomId = "r-closet-01",
//       TotalScore = 14.0,
//       DoorLockableScore = 5,
//       DoorSolidScore = 3,
//       ConcealmentScore = 2,
//       AltEgressScore = 4,
//       Recommendation = "Good hiding location. Lock door, silence phone, stay low."
//   };

using TheWatch.Shared.Enums;

namespace TheWatch.Shared.Domain.Models;

// ═══════════════════════════════════════════════════════════════
// Constants — code-referenced building code minimums
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Building-code constants used by the egress computation engine.
/// All values are statutory minimums from NFPA 101, IBC, IRC, and ADA.
/// </summary>
public static class EgressConstants
{
    /// <summary>Minimum corridor width in inches per NFPA 101 7.3.4.1 (44" for new construction).</summary>
    public const double MinCorridorWidthInches = 44.0;

    /// <summary>Minimum door clear width in inches per ADA 404.2.3 (32" clear opening).</summary>
    public const double MinDoorWidthInches = 32.0;

    /// <summary>Minimum emergency escape window width in inches per IRC R310.2.1 (20").</summary>
    public const double MinEmergencyWindowInches = 20.0;

    /// <summary>
    /// Minimum emergency escape window net clear opening area in square feet per IRC R310.1.
    /// Grade-floor windows may be 5.0 sq ft; upper floors require 5.7 sq ft.
    /// We use the more restrictive value.
    /// </summary>
    public const double MinEmergencyWindowAreaSqFt = 5.7;

    /// <summary>
    /// Maximum dead-end corridor length in feet per NFPA 101 (varies by occupancy;
    /// 20' is the most restrictive for assembly/educational). IBC 1020.4 allows 20'.
    /// </summary>
    public const double MaxDeadEndFeet = 20.0;

    /// <summary>
    /// Maximum travel distance to an exit in feet per IBC 1017.1.
    /// 200' for unsprinklered; 250' for sprinklered. We use the unsprinklered value.
    /// </summary>
    public const double MaxTravelDistanceFeet = 200.0;

    /// <summary>Maximum sill height for emergency escape windows in inches per IRC R310.2.2 (44").</summary>
    public const double MaxWindowSillHeightInches = 44.0;

    /// <summary>Minimum emergency escape window height in inches per IRC R310.2.1 (24").</summary>
    public const double MinEmergencyWindowHeightInches = 24.0;

    /// <summary>Default threshold hide score (out of 19) above which HIDE is recommended over FIGHT.</summary>
    public const double DefaultHideScoreThreshold = 10.0;

    /// <summary>Default threat avoidance K value for adversarial A* potential field.</summary>
    public const double DefaultThreatAvoidanceK = 100.0;
}

// ═══════════════════════════════════════════════════════════════
// Structure Graph — nodes and edges
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Top-level structure (building, dwelling, campus building) containing floors.
/// This is the root of the egress graph. Each structure has a unique ID,
/// a geolocation, and one or more floor plans.
/// </summary>
public class Structure
{
    // ── Identity ────────────────────────────────────────────────

    /// <summary>Unique structure identifier (e.g., "s-001").</summary>
    public string StructureId { get; set; } = string.Empty;

    /// <summary>Human-readable name (e.g., "Main Residence", "Building A").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Street address of the structure.</summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>
    /// Assessor Parcel Number — the county-assigned parcel ID for the property.
    /// Used for cross-referencing with county GIS records for floor plan verification.
    /// Example: "0142-21-1234" (Travis County, TX format).
    /// </summary>
    public string? APN { get; set; }

    // ── Floor Plans ─────────────────────────────────────────────

    /// <summary>All floors in the structure. Index by FloorLevel (0 = ground, negative = basement).</summary>
    public List<FloorPlan> Floors { get; set; } = new();

    // ── Geolocation ─────────────────────────────────────────────

    /// <summary>Latitude of the structure centroid (WGS 84).</summary>
    public double Latitude { get; set; }

    /// <summary>Longitude of the structure centroid (WGS 84).</summary>
    public double Longitude { get; set; }

    // ── Metadata ────────────────────────────────────────────────

    /// <summary>When the structure data was last updated (UTC).</summary>
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// A single floor within a structure. Contains rooms (graph nodes) and
/// egress paths (graph edges). FloorLevel 0 is ground; negative values are basements.
/// </summary>
public class FloorPlan
{
    /// <summary>
    /// Floor level: 0 = ground floor, 1 = second floor, -1 = first basement, etc.
    /// </summary>
    public int FloorLevel { get; set; }

    /// <summary>All rooms on this floor (graph nodes).</summary>
    public List<Room> Rooms { get; set; } = new();

    /// <summary>All egress paths on this floor and transitions to adjacent floors (graph edges).</summary>
    public List<EgressPath> EgressPaths { get; set; } = new();
}

/// <summary>
/// A room within a structure floor. Serves as a node in the egress graph.
/// Contains metadata for hide-location scoring (lockability, concealment,
/// occupant load, phone availability, window presence).
/// </summary>
public class Room
{
    // ── Identity ────────────────────────────────────────────────

    /// <summary>Unique room identifier within the structure (e.g., "r-bedroom-01").</summary>
    public string RoomId { get; set; } = string.Empty;

    /// <summary>Floor level this room is on (matches FloorPlan.FloorLevel).</summary>
    public int FloorLevel { get; set; }

    /// <summary>
    /// Room type classification. Standard values:
    /// "Bedroom", "Kitchen", "Office", "Bathroom", "Hallway", "Stairwell",
    /// "LivingRoom", "Garage", "Basement", "Attic", "Closet", "SafeRoom".
    /// </summary>
    public string RoomType { get; set; } = string.Empty;

    // ── Dimensions & Occupancy ──────────────────────────────────

    /// <summary>Room area in square feet.</summary>
    public double AreaSqFt { get; set; }

    /// <summary>
    /// Maximum occupant load per IBC Table 1004.5.
    /// Calculated as AreaSqFt / OccupantLoadFactor for the room's use.
    /// Example: Office = 150 sq ft/person, Assembly = 15 sq ft/person.
    /// </summary>
    public int OccupantLoad { get; set; }

    // ── Hide-Location Scoring Attributes ────────────────────────

    /// <summary>Whether the room has at least one operable window (affects alternate egress score).</summary>
    public bool HasWindow { get; set; }

    /// <summary>Whether a phone (landline or reliable cell signal) is available in this room.</summary>
    public bool HasPhone { get; set; }

    /// <summary>
    /// Concealment options available in the room.
    /// Examples: "UnderDesk", "BehindFurniture", "InCloset", "BehindDoor", "UnderBed", "InCabinet".
    /// Used in hide-score concealment calculation (0–2 points).
    /// </summary>
    public List<string> ConcealmentOptions { get; set; } = new();

    // ── Openings ────────────────────────────────────────────────

    /// <summary>All openings (doors, windows, hatches) in this room.</summary>
    public List<EgressOpening> Openings { get; set; } = new();
}

/// <summary>
/// An egress path segment — a directed edge in the structure graph.
/// Connects two rooms with a traversal cost based on distance, hazard status,
/// accessibility, and threat proximity (when adversarial routing is active).
/// </summary>
public class EgressPath
{
    // ── Identity ────────────────────────────────────────────────

    /// <summary>Unique path identifier (e.g., "p-001").</summary>
    public string PathId { get; set; } = string.Empty;

    /// <summary>Source room ID (edge origin).</summary>
    public string FromRoomId { get; set; } = string.Empty;

    /// <summary>Destination room ID (edge target).</summary>
    public string ToRoomId { get; set; } = string.Empty;

    // ── Path Characteristics ────────────────────────────────────

    /// <summary>Type of egress path (corridor, stairwell, window, etc.).</summary>
    public EgressPathType PathType { get; set; }

    /// <summary>Path length in meters.</summary>
    public double LengthMeters { get; set; }

    /// <summary>
    /// Path width in meters. Minimum 44" (1.12m) for corridors per NFPA 101 7.3.4.1.
    /// Narrower paths receive a traversal penalty in the routing algorithm.
    /// </summary>
    public double WidthMeters { get; set; }

    /// <summary>Accessibility classification of this path segment.</summary>
    public EgressAccessibility Accessibility { get; set; } = EgressAccessibility.FullyAccessible;

    /// <summary>Estimated traversal time in seconds at normal walking speed (1.2 m/s per SFPE).</summary>
    public double TravelTimeSeconds { get; set; }

    // ── Lighting ────────────────────────────────────────────────

    /// <summary>Whether the path has normal lighting.</summary>
    public bool IsLit { get; set; } = true;

    /// <summary>Whether the path has emergency/battery-backed lighting per NFPA 101 7.9.</summary>
    public bool HasEmergencyLighting { get; set; }

    // ── Hazard Status ───────────────────────────────────────────

    /// <summary>Current traversability status of this path edge.</summary>
    public HazardEdgeStatus Status { get; set; } = HazardEdgeStatus.Clear;

    /// <summary>
    /// Human-readable reason if Status is Blocked or Compromised.
    /// Examples: "Fire detected by smoke sensor Z-3", "Debris from ceiling collapse",
    /// "Door jammed — key required", "Flooding from broken pipe".
    /// Null when Status is Clear.
    /// </summary>
    public string? BlockedReason { get; set; }
}

/// <summary>
/// A physical opening (door, window, hatch) on an egress path.
/// Contains building-code-relevant dimensions and hardware attributes
/// used for accessibility filtering and fire-safety validation.
/// </summary>
public class EgressOpening
{
    // ── Identity ────────────────────────────────────────────────

    /// <summary>Unique opening identifier (e.g., "o-door-01").</summary>
    public string OpeningId { get; set; } = string.Empty;

    /// <summary>Type of opening (standard door, fire-rated, window, etc.).</summary>
    public EgressOpeningType Type { get; set; }

    // ── Dimensions ──────────────────────────────────────────────

    /// <summary>
    /// Clear width of the opening in inches.
    /// Minimum 32" for ADA compliance (ADA 404.2.3).
    /// Minimum 20" for emergency escape windows (IRC R310.2.1).
    /// </summary>
    public double WidthInches { get; set; }

    /// <summary>
    /// Height of the opening in inches.
    /// Minimum 24" for emergency escape windows (IRC R310.2.1).
    /// </summary>
    public double HeightInches { get; set; }

    // ── Fire Rating ─────────────────────────────────────────────

    /// <summary>
    /// Fire resistance rating in minutes (0, 20, 45, 60, 90 per NFPA 80).
    /// 0 means no fire rating. Higher ratings allow the door to remain in the
    /// egress graph longer during fire events before being pruned.
    /// </summary>
    public int FireRatingMinutes { get; set; }

    // ── Hardware ─────────────────────────────────────────────────

    /// <summary>Whether the opening is currently locked (affects egress traversal and hide scoring).</summary>
    public bool IsLocked { get; set; }

    /// <summary>
    /// Whether the door has panic hardware (push bar) per NFPA 101 7.2.1.7.
    /// Required on doors serving areas with occupant load > 100.
    /// Panic hardware allows single-motion egress without keys.
    /// </summary>
    public bool HasPanicHardware { get; set; }

    /// <summary>Whether this opening meets ADA accessibility requirements.</summary>
    public bool IsAccessible { get; set; } = true;
}

// ═══════════════════════════════════════════════════════════════
// Adversarial A* Parameters
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Parameters for the adversarial A* pathfinding algorithm.
/// The algorithm adds a repulsive potential field around each threat location:
///   edge_cost += K / dist(edge_midpoint, threat)^2
/// This pushes the computed route away from known threats.
///
/// Example:
///   var threatParams = new AdversarialAStarParams
///   {
///       ThreatLocations = new List&lt;ThreatLocation&gt;
///       {
///           new ThreatLocation { Latitude = 30.27, Longitude = -97.74, RadiusMeters = 15.0 }
///       },
///       ThreatAvoidanceK = 150.0,
///       PreferLitPaths = true
///   };
/// </summary>
public class AdversarialAStarParams
{
    /// <summary>Known threat locations with influence radii.</summary>
    public List<ThreatLocation> ThreatLocations { get; set; } = new();

    /// <summary>
    /// The K constant in the threat-repulsion potential field: cost += K / dist^2.
    /// Higher K = wider berth around threats. Default: 100.0.
    /// Typical range: 50 (mild avoidance) to 500 (maximum avoidance).
    /// </summary>
    public double ThreatAvoidanceK { get; set; } = EgressConstants.DefaultThreatAvoidanceK;

    /// <summary>
    /// Mobility weight multiplier for the person being routed.
    /// 1.0 = full mobility, 0.5 = reduced mobility (injury, disability), 0.0 = immobile.
    /// Affects route selection: lower values penalize stairs/ladders more heavily.
    /// </summary>
    public double PersonMobilityWeight { get; set; } = 1.0;

    /// <summary>
    /// Whether to prefer lit paths (adds penalty to unlit/dark path edges).
    /// Recommended during power outages or nighttime evacuations.
    /// </summary>
    public bool PreferLitPaths { get; set; } = true;
}

/// <summary>
/// A known threat location with an influence radius.
/// Used by the adversarial A* algorithm and hide-location scoring.
/// </summary>
public class ThreatLocation
{
    /// <summary>Latitude of the threat (WGS 84).</summary>
    public double Latitude { get; set; }

    /// <summary>Longitude of the threat (WGS 84).</summary>
    public double Longitude { get; set; }

    /// <summary>
    /// Radius of influence in meters. Edges within this radius receive the
    /// maximum repulsion penalty. Edges beyond this radius receive diminishing penalty.
    /// </summary>
    public double RadiusMeters { get; set; }
}

// ═══════════════════════════════════════════════════════════════
// Hide-Location Scoring
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Scoring result for a room evaluated as a potential hide location.
/// Uses a 0–19 point rubric (with possible -10 penalty for line-of-sight exposure).
///
/// Rubric breakdown:
///   +5  Door lockable from inside
///   +3  Door is solid-core (not hollow)
///   +2  Wall rating (concrete/brick = 2, drywall = 1, glass = 0)
///   +4  Alternate egress exists (can escape if threat breaches)
///   +2  Distance from threat (far = 2, medium = 1, near = 0)
///   +3  Phone available (can call 911)
///   +2  Concealment options (2+ = 2, 1 = 1, 0 = 0)
///  -10  Direct line-of-sight to threat location (fatal penalty)
///  ───
///   19  Maximum score (without penalty)
///    9  Minimum recommended threshold for HIDE recommendation
///
/// Example:
///   Locked bathroom with solid door, concrete walls, no window, phone available:
///   5 + 3 + 2 + 0 + 1 + 3 + 1 = 15 → Strong HIDE recommendation
/// </summary>
public class HideScoreResult
{
    /// <summary>Room ID that was scored.</summary>
    public string RoomId { get; set; } = string.Empty;

    /// <summary>Total hide score (sum of all component scores).</summary>
    public double TotalScore { get; set; }

    /// <summary>Door lockable from inside: 0 (no) or 5 (yes).</summary>
    public double DoorLockableScore { get; set; }

    /// <summary>Door is solid-core: 0 (hollow/none) or 3 (solid).</summary>
    public double DoorSolidScore { get; set; }

    /// <summary>Wall construction rating: 0 (glass/curtain), 1 (drywall), 2 (concrete/brick/CMU).</summary>
    public double WallRatingScore { get; set; }

    /// <summary>Alternate egress from hide location: 0 (no) or 4 (yes, e.g., window or second door).</summary>
    public double AltEgressScore { get; set; }

    /// <summary>Distance from nearest threat: 0 (near, &lt;10m), 1 (medium, 10–30m), 2 (far, &gt;30m).</summary>
    public double DistanceFromThreatScore { get; set; }

    /// <summary>Phone availability: 0 (no phone) or 3 (phone/cell signal available).</summary>
    public double PhoneAvailableScore { get; set; }

    /// <summary>Concealment options: 0 (none), 1 (one option), 2 (two or more options).</summary>
    public double ConcealmentScore { get; set; }

    /// <summary>Line-of-sight penalty: 0 (no LOS to threat) or -10 (direct LOS — critically exposed).</summary>
    public double LineOfSightPenalty { get; set; }

    /// <summary>
    /// Human-readable recommendation based on the score.
    /// Examples:
    ///   "Strong hiding location. Lock door, barricade, silence phone, call 911."
    ///   "Weak hiding location. Consider relocating if possible."
    ///   "Not recommended. Direct line-of-sight to threat."
    /// </summary>
    public string Recommendation { get; set; } = string.Empty;
}

// ═══════════════════════════════════════════════════════════════
// Run-Hide-Fight Decision
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// The output of the Run-Hide-Fight decision engine.
/// Contains the recommended action, the supporting data (route or hide score),
/// and the confidence level of the recommendation.
///
/// Example:
///   var decision = new RunHideFightDecision
///   {
///       Action = RunHideFightAction.Run,
///       Confidence = 0.92f,
///       RunRoute = new[] { corridorPath, stairwellPath, exitPath },
///       Reasoning = "Clear egress route via west stairwell. 45 seconds to exterior assembly point."
///   };
/// </summary>
public class RunHideFightDecision
{
    /// <summary>The recommended action: Run, Hide, or Fight.</summary>
    public RunHideFightAction Action { get; set; }

    /// <summary>
    /// Confidence in the recommendation (0.0 – 1.0).
    /// Low confidence (&lt;0.5) indicates uncertain data (unknown edge statuses,
    /// imprecise threat location). Client should display caveats.
    /// </summary>
    public float Confidence { get; set; }

    /// <summary>
    /// The recommended egress route if Action is Run. Null otherwise.
    /// Ordered from current room to exit/assembly point.
    /// </summary>
    public EgressPath[]? RunRoute { get; set; }

    /// <summary>The recommended hide room if Action is Hide. Null otherwise.</summary>
    public Room? HideRoom { get; set; }

    /// <summary>The hide score for the recommended room if Action is Hide. Null otherwise.</summary>
    public HideScoreResult? HideScore { get; set; }

    /// <summary>
    /// Human-readable reasoning for the decision.
    /// Examples:
    ///   "Clear route to east exit via corridor C-3 and stairwell S-1. ETA 35s."
    ///   "All egress routes blocked by fire. Best hide: bathroom B-2 (score 16/19). Lock door, call 911."
    ///   "No safe egress or adequate hide location. Last resort: confront threat."
    /// </summary>
    public string Reasoning { get; set; } = string.Empty;

    /// <summary>UTC timestamp when the decision was computed.</summary>
    public DateTime DecidedAt { get; set; } = DateTime.UtcNow;
}

// ═══════════════════════════════════════════════════════════════
// Group Evacuation (Steiner Tree)
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// A participant in a group evacuation — a person in a specific room.
/// </summary>
public class EvacuationParticipant
{
    /// <summary>User ID of the participant.</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>Room ID where the participant is currently located.</summary>
    public string RoomId { get; set; } = string.Empty;
}

/// <summary>
/// Group evacuation plan computed via Steiner tree algorithm.
/// Finds the minimum-cost tree in the egress graph that connects all participant
/// locations to a common assembly point, minimizing total travel distance/time
/// while avoiding hazards and threats.
///
/// Example:
///   // Family of 4 in different rooms during a fire
///   var plan = await egressPort.ComputeGroupEvacuationAsync(
///       structureId: "s-001",
///       participantRoomIds: new[] { "r-bedroom-01", "r-kitchen", "r-office", "r-bedroom-02" }
///   );
///   // plan.SteinerTreePaths contains the merged evacuation routes
///   // plan.AssemblyPointId = "r-assembly-front-yard"
///   // plan.EstimatedTotalTimeSeconds = 52
/// </summary>
public class GroupEvacuationPlan
{
    /// <summary>Unique plan identifier.</summary>
    public string PlanId { get; set; } = string.Empty;

    /// <summary>Structure ID this plan is for.</summary>
    public string StructureId { get; set; } = string.Empty;

    /// <summary>Participants and their starting rooms.</summary>
    public List<EvacuationParticipant> Participants { get; set; } = new();

    /// <summary>
    /// The Steiner tree paths — the merged set of egress paths that connect
    /// all participant rooms to the assembly point with minimum total cost.
    /// </summary>
    public List<EgressPath> SteinerTreePaths { get; set; } = new();

    /// <summary>Room ID of the designated assembly point (destination node).</summary>
    public string AssemblyPointId { get; set; } = string.Empty;

    /// <summary>Estimated total evacuation time in seconds (longest individual path).</summary>
    public double EstimatedTotalTimeSeconds { get; set; }

    /// <summary>UTC timestamp when the plan was created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
