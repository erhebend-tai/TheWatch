// IThreatTrackingPort — domain port for real-time threat tracking, violence detection,
// acoustic classification, sensor integration, safe harbor routing, stealth mode,
// and DOJ/Johns Hopkins Lethality Assessment Protocol scoring.
//
// Architecture:
//   Sensor events (door, glass break, acoustic, CCTV) flow through this port.
//   Each sensor reading is classified and may create or update a ThreatSource.
//   The threat's position is tracked over time, egress routes are recomputed,
//   and safe harbors are recommended based on threat location and DV context.
//
//   ┌──────────────┐     ┌─────────────────────┐     ┌───────────────────────┐
//   │ Sensors/CCTV │────▶│ IThreatTrackingPort  │────▶│ Adapter               │
//   │ User Reports │     │ .ReportThreatAsync() │     │ (Mock, Azure, Cosmos) │
//   └──────────────┘     └─────────────────────┘     └───────────────────────┘
//                                 │
//                        Threat position trail + egress graph + safe harbors
//                        LAP scoring for DV → stealth mode for victim safety
//
// Standards referenced:
//   MIL-STD-1474E — acoustic gunshot classification
//   UL 639        — glass break sensor standards
//   Z-Wave/Zigbee — door sensor protocols
//   DOJ/Johns Hopkins LAP — 11-question domestic violence lethality screening
//
// Example — full threat lifecycle:
//   var threat = await port.ReportThreatAsync(new ThreatSource { Type = ThreatType.Intruder });
//   var updated = await port.UpdateThreatPositionAsync(threat.ThreatId, newPosition);
//   var blocked = await port.ComputeBlockedEgressAsync(threat.ThreatId, "structure-001");
//   var harbors = await port.GetSafeHarborsAsync(lat, lng, 500, excludeKnownToThreat: true);
//
// Example — DV with stealth mode:
//   await port.EnableStealthModeAsync(userId, new StealthModeConfig
//   {
//       IsEnabled = true, SilentNotifications = true, ScreenDimmed = true,
//       DuressCode = "1234", SafeWordPhrase = "I need to call my sister"
//   });
//   int lapScore = await port.AssessLethalityAsync(threatId, answers);

using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Enums;

namespace TheWatch.Shared.Domain.Ports;

/// <summary>
/// Domain port for threat tracking, violence detection, sensor integration,
/// egress computation, safe harbor routing, and stealth mode management.
/// </summary>
public interface IThreatTrackingPort
{
    // ── Threat Lifecycle ─────────────────────────────────────────

    /// <summary>
    /// Report a new threat source. Creates the threat record and begins tracking.
    /// Returns the created ThreatSource with assigned ThreatId and timestamps.
    /// </summary>
    /// <param name="source">The threat source to report, with type, position, and armed status.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The persisted ThreatSource with server-assigned fields populated.</returns>
    Task<ThreatSource> ReportThreatAsync(ThreatSource source, CancellationToken ct = default);

    /// <summary>
    /// Update a tracked threat's position. Appends to the position trail and
    /// recomputes mobility classification and heading.
    /// </summary>
    /// <param name="threatId">The threat to update.</param>
    /// <param name="position">The new position reading.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated ThreatSource with new position, heading, and speed.</returns>
    Task<ThreatSource> UpdateThreatPositionAsync(string threatId, ThreatPosition position, CancellationToken ct = default);

    /// <summary>
    /// Get a single active threat by ID. Returns null if the threat is not found or no longer active.
    /// </summary>
    /// <param name="threatId">The threat identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The active ThreatSource, or null if not found/inactive.</returns>
    Task<ThreatSource?> GetActiveThreatAsync(string threatId, CancellationToken ct = default);

    /// <summary>
    /// Get all active threats within a geographic radius.
    /// Uses geospatial indexing (H3/geohash) for efficient area queries.
    /// </summary>
    /// <param name="latitude">Center latitude (WGS 84).</param>
    /// <param name="longitude">Center longitude (WGS 84).</param>
    /// <param name="radiusMeters">Search radius in meters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>All active threats within the specified radius, ordered by distance.</returns>
    Task<IReadOnlyList<ThreatSource>> GetActiveThreatsInAreaAsync(double latitude, double longitude, double radiusMeters, CancellationToken ct = default);

    /// <summary>
    /// Record a discrete threat event (weapon discharged, entry forced, hostage taken, etc.).
    /// Events are appended to the threat's history timeline.
    /// </summary>
    /// <param name="threatEvent">The event to record.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The persisted ThreatEvent with server-assigned EventId and timestamp.</returns>
    Task<ThreatEvent> RecordThreatEventAsync(ThreatEvent threatEvent, CancellationToken ct = default);

    /// <summary>
    /// Get the complete history of a threat, including position trail, events, and LAP score.
    /// Returns null if no history exists for the given threat ID.
    /// </summary>
    /// <param name="threatId">The threat identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The complete ThreatHistory, or null if not found.</returns>
    Task<ThreatHistory?> GetThreatHistoryAsync(string threatId, CancellationToken ct = default);

    // ── Lethality Assessment Protocol (LAP) ──────────────────────

    /// <summary>
    /// Assess domestic violence lethality using the 11-question DOJ/Johns Hopkins LAP.
    /// Computes a weighted score (0-11 scale). Score >= 7 or Q1 "yes" = "high danger."
    /// The score is persisted in the threat's ThreatHistory.LAPScore.
    /// </summary>
    /// <param name="threatId">The DV threat being assessed.</param>
    /// <param name="answers">The 11 LAP answers with question numbers, responses, and weights.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The computed LAP score (0-11). Check against LAPQuestions.HighDangerThreshold.</returns>
    Task<int> AssessLethalityAsync(string threatId, LAPAnswer[] answers, CancellationToken ct = default);

    // ── Egress & Safe Harbor ─────────────────────────────────────

    /// <summary>
    /// Compute which egress (escape) routes in a structure are blocked by a threat.
    /// Uses the structure's floor plan graph, the threat's position, line of sight,
    /// and acoustic proximity to determine impassable edges.
    /// </summary>
    /// <param name="threatId">The threat blocking egress routes.</param>
    /// <param name="structureId">The structure (building/dwelling) to compute egress for.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of blocked egress edges with reasons and estimated clear times.</returns>
    Task<IReadOnlyList<BlockedEgressEdge>> ComputeBlockedEgressAsync(string threatId, string structureId, CancellationToken ct = default);

    /// <summary>
    /// Get available safe harbors within a radius of the user's position.
    /// For DV situations, set excludeKnownToThreat = true to filter out locations
    /// the perpetrator knows about.
    /// </summary>
    /// <param name="latitude">User's current latitude (WGS 84).</param>
    /// <param name="longitude">User's current longitude (WGS 84).</param>
    /// <param name="radiusMeters">Search radius in meters.</param>
    /// <param name="excludeKnownToThreat">If true, exclude harbors where IsKnownToThreat = true.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Available safe harbors ordered by distance, filtered by DV context.</returns>
    Task<IReadOnlyList<SafeHarbor>> GetSafeHarborsAsync(double latitude, double longitude, double radiusMeters, bool excludeKnownToThreat, CancellationToken ct = default);

    // ── Sensor Processing ────────────────────────────────────────

    /// <summary>
    /// Classify an acoustic event using signal processing and ML models.
    /// Returns the event with updated Type and Confidence fields.
    /// Gunshot classification follows MIL-STD-1474E impulse noise methodology.
    /// Glass break classification follows UL 639 shock+flex pattern analysis.
    /// </summary>
    /// <param name="acousticEvent">The raw acoustic event with sensor data.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The acoustic event with classified Type and updated Confidence.</returns>
    Task<AcousticEvent> ClassifyAcousticEventAsync(AcousticEvent acousticEvent, CancellationToken ct = default);

    /// <summary>
    /// Process a door sensor reading. If the event is ForcedEntryWhileLocked or Tampered,
    /// automatically creates a ThreatSource (Type = Intruder) and returns it.
    /// Returns null for normal door events.
    /// </summary>
    /// <param name="reading">The door sensor reading to process.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A new ThreatSource if forced entry detected; null otherwise.</returns>
    Task<ThreatSource?> ProcessDoorSensorAsync(DoorSensorReading reading, CancellationToken ct = default);

    /// <summary>
    /// Process a glass break sensor reading. If confidence exceeds threshold,
    /// automatically creates a ThreatSource (Type = Intruder) and returns it.
    /// Classification follows UL 639 standards for shock+flex pattern validation.
    /// Returns null if the break event does not meet threat thresholds.
    /// </summary>
    /// <param name="reading">The glass break sensor reading to process.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A new ThreatSource if glass break threat detected; null otherwise.</returns>
    Task<ThreatSource?> ProcessGlassBreakAsync(GlassBreakReading reading, CancellationToken ct = default);

    // ── Stealth Mode (DV Victim Safety) ──────────────────────────

    /// <summary>
    /// Enable stealth mode for a user. Configures silent notifications, screen dimming,
    /// duress codes, and safe word phrases. Designed for DV victims who must use the app
    /// without the perpetrator's knowledge.
    /// </summary>
    /// <param name="userId">The user to enable stealth mode for.</param>
    /// <param name="config">Stealth mode configuration.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if stealth mode was successfully enabled.</returns>
    Task<bool> EnableStealthModeAsync(string userId, StealthModeConfig config, CancellationToken ct = default);

    /// <summary>
    /// Disable stealth mode for a user. Restores normal notification and alert behavior.
    /// </summary>
    /// <param name="userId">The user to disable stealth mode for.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if stealth mode was successfully disabled.</returns>
    Task<bool> DisableStealthModeAsync(string userId, CancellationToken ct = default);

    // ── Children in Hiding ───────────────────────────────────────

    /// <summary>
    /// Get user IDs of children flagged as hiding within a structure during a threat event.
    /// Used by responders to locate children during DV or intruder situations.
    /// The system tracks children's last known positions within the dwelling.
    /// </summary>
    /// <param name="structureId">The structure (building/dwelling) to search.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>User IDs of children flagged as hiding in the structure.</returns>
    Task<IReadOnlyList<string>> GetChildrenInHidingAsync(string structureId, CancellationToken ct = default);
}
