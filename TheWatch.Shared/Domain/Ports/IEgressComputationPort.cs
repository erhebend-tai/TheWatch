// IEgressComputationPort — port interface for Egress & Escape Routing (Run-Hide-Fight).
//
// This port defines the contract for all egress computation operations:
//   1. Shortest safe egress route (Dijkstra on the structure graph)
//   2. Adversarial A* route (threat-avoidance potential field)
//   3. Hide-location scoring (0–19 point rubric)
//   4. Run-Hide-Fight decision engine
//   5. Steiner tree group evacuation
//   6. Hazard edge pruning and restoration
//   7. Safe harbor discovery
//
// Architecture:
//   IEgressComputationPort is implemented by adapters that maintain the structure
//   graph in memory (or query it from a backing store). The graph is a weighted
//   directed graph where rooms are nodes and egress paths are edges.
//
//   Mock adapter: in-memory graph with canned structures for testing.
//   Real adapter: loads structure data from CosmosDB/PostgreSQL, builds graph,
//                 runs pathfinding algorithms, caches results in Redis.
//
// Thread safety:
//   All methods are async and accept CancellationToken for cooperative cancellation.
//   Implementations must be thread-safe — multiple concurrent SOS events may
//   trigger egress computation on the same structure simultaneously.
//
// Example:
//   // Compute safest egress route away from an active threat
//   var threatParams = new AdversarialAStarParams
//   {
//       ThreatLocations = new List<ThreatLocation>
//       {
//           new() { Latitude = 30.27, Longitude = -97.74, RadiusMeters = 10 }
//       },
//       ThreatAvoidanceK = 200.0
//   };
//   var route = await egressPort.ComputeAdversarialRouteAsync("s-001", "r-office", threatParams);
//   // route = [ corridor-to-stairwell, stairwell-down, corridor-to-exit, exit-door ]
//
//   // Then make a Run-Hide-Fight decision
//   var decision = await egressPort.DecideRunHideFightAsync(
//       "s-001", "r-office", threatParams.ThreatLocations);
//   // decision.Action = RunHideFightAction.Run, decision.RunRoute = route

using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Enums;

namespace TheWatch.Shared.Domain.Ports;

/// <summary>
/// Port for computing egress routes, scoring hide locations, and making
/// Run-Hide-Fight decisions within a structure's egress graph.
/// </summary>
public interface IEgressComputationPort
{
    // ── Route Computation ───────────────────────────────────────

    /// <summary>
    /// Compute the shortest safe egress route from a room to the nearest exit.
    /// Uses Dijkstra's algorithm on the structure graph, filtering edges by
    /// hazard status (only Clear edges) and accessibility (if personCapabilities provided).
    /// </summary>
    /// <param name="structureId">The structure to route within.</param>
    /// <param name="fromRoomId">The room the person is currently in.</param>
    /// <param name="personCapabilities">
    /// Optional accessibility filter. If provided, routes are restricted to edges
    /// matching the person's mobility level (e.g., FullyAccessible, WheelchairWithAssistance).
    /// Null = no accessibility filtering (assume full mobility).
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Ordered list of egress path segments from current room to exit. Empty if no route exists.</returns>
    Task<IReadOnlyList<EgressPath>> ComputeEgressRouteAsync(
        string structureId,
        string fromRoomId,
        EgressAccessibility? personCapabilities = null,
        CancellationToken ct = default);

    /// <summary>
    /// Compute an egress route that avoids known threat locations using adversarial A*.
    /// The cost function adds a repulsive potential field: cost += K / dist(edge, threat)^2
    /// for each threat, pushing the route away from danger zones.
    /// </summary>
    /// <param name="structureId">The structure to route within.</param>
    /// <param name="fromRoomId">The room the person is currently in.</param>
    /// <param name="parameters">
    /// Adversarial A* parameters including threat locations, avoidance strength (K),
    /// mobility weight, and lighting preference.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Ordered list of egress path segments avoiding threats. Empty if no safe route exists.</returns>
    Task<IReadOnlyList<EgressPath>> ComputeAdversarialRouteAsync(
        string structureId,
        string fromRoomId,
        AdversarialAStarParams parameters,
        CancellationToken ct = default);

    // ── Hide-Location Scoring ───────────────────────────────────

    /// <summary>
    /// Score a room as a potential hide location using the 0–19 point rubric.
    /// Evaluates door lockability, wall construction, concealment options,
    /// alternate egress, distance from threats, and phone availability.
    /// </summary>
    /// <param name="roomId">The room to evaluate.</param>
    /// <param name="threatLocations">Current known threat locations for distance scoring.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Detailed hide score with component breakdown and recommendation text.</returns>
    Task<HideScoreResult> ScoreHideLocationAsync(
        string roomId,
        IReadOnlyList<ThreatLocation> threatLocations,
        CancellationToken ct = default);

    // ── Run-Hide-Fight Decision ─────────────────────────────────

    /// <summary>
    /// Make a Run-Hide-Fight decision for a person in a specific room.
    /// The decision engine:
    ///   1. Attempts to compute an adversarial egress route (RUN).
    ///   2. If no safe route, scores all reachable rooms as hide locations (HIDE).
    ///   3. If no adequate hide location (score &lt; threshold), recommends FIGHT.
    /// </summary>
    /// <param name="structureId">The structure the person is in.</param>
    /// <param name="personRoomId">The room the person is currently in.</param>
    /// <param name="threatLocations">Current known threat locations.</param>
    /// <param name="personCapabilities">
    /// Optional accessibility level. Null = full mobility.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// Decision containing the recommended action, supporting route/hide data,
    /// confidence level, and human-readable reasoning.
    /// </returns>
    Task<RunHideFightDecision> DecideRunHideFightAsync(
        string structureId,
        string personRoomId,
        IReadOnlyList<ThreatLocation> threatLocations,
        EgressAccessibility? personCapabilities = null,
        CancellationToken ct = default);

    // ── Group Evacuation ────────────────────────────────────────

    /// <summary>
    /// Compute a group evacuation plan using a Steiner tree algorithm.
    /// Finds the minimum-cost tree connecting all participant rooms to a
    /// common assembly point, minimizing total group travel distance.
    /// </summary>
    /// <param name="structureId">The structure to evacuate.</param>
    /// <param name="participantRoomIds">Room IDs where each participant is located.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// Evacuation plan containing Steiner tree paths, assembly point,
    /// and estimated total evacuation time.
    /// </returns>
    Task<GroupEvacuationPlan> ComputeGroupEvacuationAsync(
        string structureId,
        IReadOnlyList<string> participantRoomIds,
        CancellationToken ct = default);

    // ── Hazard Edge Management ──────────────────────────────────

    /// <summary>
    /// Prune (block) egress path edges near hazard locations.
    /// Sets Status = Blocked on all edges within the hazard radius.
    /// Called when new hazards are detected (fire, structural collapse, active threat).
    /// </summary>
    /// <param name="structureId">The structure to update.</param>
    /// <param name="hazardLocations">Hazard locations with influence radii.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Number of edges that were blocked.</returns>
    Task<int> PruneHazardEdgesAsync(
        string structureId,
        IReadOnlyList<ThreatLocation> hazardLocations,
        CancellationToken ct = default);

    /// <summary>
    /// Restore all pruned/blocked edges in a structure back to Clear status.
    /// Called when hazards are resolved or during system reset.
    /// </summary>
    /// <param name="structureId">The structure to restore.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Number of edges that were restored.</returns>
    Task<int> RestoreEdgesAsync(
        string structureId,
        CancellationToken ct = default);

    // ── Query ───────────────────────────────────────────────────

    /// <summary>
    /// Get all rooms in a structure that qualify as safe harbors
    /// (SafeRoom type, exterior assembly points, reinforced rooms).
    /// </summary>
    /// <param name="structureId">The structure to query.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of rooms designated as safe harbors.</returns>
    Task<IReadOnlyList<Room>> GetSafeHarborsAsync(
        string structureId,
        CancellationToken ct = default);

    /// <summary>
    /// Get all egress paths in a structure, optionally filtered by floor level.
    /// </summary>
    /// <param name="structureId">The structure to query.</param>
    /// <param name="floorLevel">Optional floor level filter. Null = all floors.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of egress paths.</returns>
    Task<IReadOnlyList<EgressPath>> GetEgressPathsAsync(
        string structureId,
        int? floorLevel = null,
        CancellationToken ct = default);
}
