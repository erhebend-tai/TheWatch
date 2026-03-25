// IStructurePort.cs — Domain port for building/structure data, hazard conditions,
// weather alerts, hazmat lookup, and seismic alerts.
//
// Architecture:
//   This port abstracts all structure and hazard operations behind async methods.
//   Adapters implement this interface for each backing store:
//     - CosmosDB (Azure production)
//     - SQL Server / PostgreSQL (on-prem / hybrid)
//     - Mock (unit tests, local development)
//
//   ┌──────────────────┐     ┌──────────────────────┐     ┌─────────────────────┐
//   │ Dashboard.Api     │────▶│ IStructurePort        │────▶│ Adapter             │
//   │ Controllers/Hubs  │     │ (this interface)      │     │ (Cosmos, SQL, Mock) │
//   └──────────────────┘     └──────────────────────┘     └─────────────────────┘
//                                      │
//                              ┌───────┴───────┐
//                              │ External APIs  │
//                              │ NWS, USGS,     │
//                              │ ShakeAlert,    │
//                              │ ERG 2024 DB    │
//                              └───────────────┘
//
// Standards referenced:
//   IBC — International Building Code (occupancy, construction type)
//   NFPA 13/13R/13D/72 — Fire protection system classifications
//   NFIRS 5.0 — Fire cause and area-of-origin coding
//   CAP 1.2 — Common Alerting Protocol (NWS weather alerts)
//   ERG 2024 — Emergency Response Guidebook (hazmat evacuation tables)
//   ShakeAlert — USGS Earthquake Early Warning
//   HAZUS-MH — FEMA multi-hazard loss estimation
//
// Example — full building query flow:
//   var port = serviceProvider.GetRequiredService<IStructurePort>();
//   var building = await port.GetBuildingAsync("bld-001");
//   var rooms = await port.GetRoomsAsync("bld-001", floorLevel: 1);
//   var firePro = await port.GetFireProtectionAsync("bld-001");
//   var hazards = await port.GetActiveHazardsAsync("bld-001");
//
// Example — explosion risk check:
//   var (isAtRisk, fuel, rec) = await port.ComputeExplosionRiskAsync("bld-001");
//   // isAtRisk=true, fuel="Propane", rec="Install gas leak detector; propane pools in basements"
//
// Example — hazmat ERG lookup:
//   var chlorine = await port.LookupHazmatAsync("1017");
//   // chlorine.ERGGuideNumber = 124, chlorine.IsToxicByInhalation = true
//
// Example — weather alerts near a location:
//   var alerts = await port.GetWeatherAlertsAsync(39.78, -89.65, radiusKm: 50);
//   // Returns active NWS alerts within 50 km

using TheWatch.Shared.Domain.Models;

namespace TheWatch.Shared.Domain.Ports;

/// <summary>
/// Domain port for building/structure data, fire protection systems, hazard conditions,
/// weather alerts, hazmat information, and seismic alerts. All methods are async with
/// optional CancellationToken support.
/// </summary>
public interface IStructurePort
{
    // ── Building CRUD ─────────────────────────────────────────────

    /// <summary>
    /// Retrieve a building record by its unique identifier.
    /// Returns null if not found.
    /// </summary>
    /// <param name="buildingId">Unique building identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The building data, or null if not found.</returns>
    Task<BuildingData?> GetBuildingAsync(string buildingId, CancellationToken ct = default);

    /// <summary>
    /// Retrieve a building record by Assessor Parcel Number (APN).
    /// APN is a county-assigned identifier for the land parcel (e.g., "123-456-789").
    /// Returns null if not found.
    /// </summary>
    /// <param name="apn">Assessor Parcel Number.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The building data, or null if no match.</returns>
    Task<BuildingData?> GetBuildingByAPNAsync(string apn, CancellationToken ct = default);

    /// <summary>
    /// Find all buildings within a radius of a geographic point.
    /// Uses geospatial index (H3/geohash) for efficient area queries.
    /// </summary>
    /// <param name="lat">WGS84 latitude of the search center.</param>
    /// <param name="lng">WGS84 longitude of the search center.</param>
    /// <param name="radiusMeters">Search radius in meters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Buildings within the specified radius, ordered by distance.</returns>
    Task<IReadOnlyList<BuildingData>> GetBuildingsInAreaAsync(double lat, double lng, double radiusMeters, CancellationToken ct = default);

    /// <summary>
    /// Create or update a building record. Upsert semantics — if BuildingId exists, update; otherwise insert.
    /// </summary>
    /// <param name="building">Building data to persist.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The persisted building data (with any server-generated fields populated).</returns>
    Task<BuildingData> UpdateBuildingAsync(BuildingData building, CancellationToken ct = default);

    // ── Room Inventory ────────────────────────────────────────────

    /// <summary>
    /// Retrieve rooms for a building, optionally filtered by floor level.
    /// </summary>
    /// <param name="buildingId">Building identifier.</param>
    /// <param name="floorLevel">Optional floor level filter. Null returns all floors.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Rooms matching the criteria.</returns>
    Task<IReadOnlyList<RoomData>> GetRoomsAsync(string buildingId, int? floorLevel = null, CancellationToken ct = default);

    // ── Fire Protection ───────────────────────────────────────────

    /// <summary>
    /// Retrieve the fire protection system summary for a building.
    /// Returns null if no fire protection data has been recorded.
    /// </summary>
    /// <param name="buildingId">Building identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Fire protection system data, or null.</returns>
    Task<FireProtectionSystem?> GetFireProtectionAsync(string buildingId, CancellationToken ct = default);

    // ── Hazard Conditions ─────────────────────────────────────────

    /// <summary>
    /// Report a new hazard condition at a building. Persists the record and returns
    /// the created hazard with any server-generated fields (e.g., timestamps).
    /// </summary>
    /// <param name="hazard">Hazard condition to report.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The persisted hazard condition.</returns>
    Task<HazardCondition> ReportHazardAsync(HazardCondition hazard, CancellationToken ct = default);

    /// <summary>
    /// Retrieve all currently active (unresolved) hazard conditions at a building.
    /// </summary>
    /// <param name="buildingId">Building identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Active hazard conditions, ordered by DetectedAt descending.</returns>
    Task<IReadOnlyList<HazardCondition>> GetActiveHazardsAsync(string buildingId, CancellationToken ct = default);

    /// <summary>
    /// Resolve (close) a hazard condition. Sets IsActive=false, populates ResolvedAt,
    /// and records the resolution description.
    /// </summary>
    /// <param name="hazardId">Hazard identifier to resolve.</param>
    /// <param name="resolution">Free-text description of how the hazard was resolved.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated hazard condition.</returns>
    Task<HazardCondition> ResolveHazardAsync(string hazardId, string resolution, CancellationToken ct = default);

    // ── Weather Alerts (CAP 1.2 / NWS) ───────────────────────────

    /// <summary>
    /// Retrieve active weather alerts near a geographic point.
    /// Queries NWS API / IPAWS feed for CAP 1.2 alerts within the radius.
    /// </summary>
    /// <param name="lat">WGS84 latitude.</param>
    /// <param name="lng">WGS84 longitude.</param>
    /// <param name="radiusKm">Search radius in kilometers.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Active weather alerts within the area.</returns>
    Task<IReadOnlyList<WeatherAlert>> GetWeatherAlertsAsync(double lat, double lng, double radiusKm, CancellationToken ct = default);

    // ── Hazmat (ERG 2024) ─────────────────────────────────────────

    /// <summary>
    /// Look up hazardous material information by UN Number using ERG 2024 data.
    /// Returns evacuation distances, guide number, and TIH/water-reactive flags.
    /// </summary>
    /// <param name="unNumber">4-digit UN Number (e.g., "1017" for Chlorine).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Hazmat info, or null if UN Number not found.</returns>
    Task<HazmatInfo?> LookupHazmatAsync(string unNumber, CancellationToken ct = default);

    // ── Seismic Alerts (ShakeAlert / USGS) ────────────────────────

    /// <summary>
    /// Retrieve active seismic alerts near a geographic point.
    /// Integrates ShakeAlert EEW (early warning) and USGS ShakeMap (post-event).
    /// </summary>
    /// <param name="lat">WGS84 latitude.</param>
    /// <param name="lng">WGS84 longitude.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Seismic alerts relevant to the location.</returns>
    Task<IReadOnlyList<SeismicAlert>> GetSeismicAlertsAsync(double lat, double lng, CancellationToken ct = default);

    // ── Computed Risk Assessments ─────────────────────────────────

    /// <summary>
    /// Compute explosion risk for a building based on its heating fuel type.
    /// Propane and natural gas have highest risk; electric/solar have lowest.
    /// </summary>
    /// <param name="buildingId">Building identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// Tuple: IsAtRisk (bool), FuelType (string name of the fuel), Recommendation (actionable guidance).
    /// Example: (true, "Propane", "Install combustible gas detector in basement; propane is heavier than air and pools in low areas.")
    /// </returns>
    Task<(bool IsAtRisk, string FuelType, string Recommendation)> ComputeExplosionRiskAsync(string buildingId, CancellationToken ct = default);

    /// <summary>
    /// Compute flood vulnerability for a building based on its foundation type.
    /// Basements are most vulnerable; piers/posts are most resilient.
    /// </summary>
    /// <param name="buildingId">Building identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// Tuple: IsVulnerable (bool), FoundationType (string name), Recommendation (actionable guidance).
    /// Example: (true, "Basement", "Install sump pump with battery backup; elevate utilities above BFE.")
    /// </returns>
    Task<(bool IsVulnerable, string FoundationType, string Recommendation)> ComputeFloodVulnerabilityAsync(string buildingId, CancellationToken ct = default);
}
