/**
 * ┌─────────────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                            │
 * │ File:    MapServicePort.kt                                                 │
 * │ Purpose: Hexagonal port (domain contract) for all geospatial map data.     │
 * │          Provides nearby responders, community alerts, evacuation routes,   │
 * │          and shelter information. This is the INBOUND port — adapters       │
 * │          (Mock, Firebase, REST) implement it. UI and ViewModels depend      │
 * │          ONLY on this interface, never on concrete data sources.            │
 * │ Date:    2026-03-24                                                        │
 * │ Author:  Claude (Anthropic)                                                │
 * │ Deps:    com.thewatch.app.data.model.Responder                             │
 * │          com.thewatch.app.data.model.CommunityAlert                        │
 * │          com.google.android.gms.maps.model.LatLng                          │
 * │ License: Proprietary — TheWatch Safety Platform                            │
 * │                                                                            │
 * │ Usage Example:                                                             │
 * │   // In a ViewModel:                                                       │
 * │   @HiltViewModel                                                           │
 * │   class MapViewModel @Inject constructor(                                  │
 * │       private val mapService: MapServicePort                               │
 * │   ) : ViewModel() {                                                        │
 * │       fun loadNearby(center: LatLng, radiusMeters: Double) {               │
 * │           viewModelScope.launch {                                          │
 * │               val responders = mapService.getNearbyResponders(             │
 * │                   center, radiusMeters                                     │
 * │               )                                                            │
 * │               val alerts = mapService.getCommunityAlerts(                  │
 * │                   center, radiusMeters                                     │
 * │               )                                                            │
 * │               val routes = mapService.getEvacuationRoutes(center)          │
 * │               val shelters = mapService.getShelters(center, radiusMeters)  │
 * │           }                                                                │
 * │       }                                                                    │
 * │   }                                                                        │
 * │                                                                            │
 * │ Possible Future Adapters:                                                  │
 * │   - FirebaseMapServiceAdapter (Firestore + GeoFire)                        │
 * │   - RestMapServiceAdapter (backend REST API)                               │
 * │   - GraphQLMapServiceAdapter (Apollo GraphQL)                              │
 * │   - GrpcMapServiceAdapter (protobuf streaming)                             │
 * └─────────────────────────────────────────────────────────────────────────────┘
 */
package com.thewatch.app.data.maps

import com.google.android.gms.maps.model.LatLng

/**
 * Domain model for an evacuation route displayed on the map.
 *
 * @property id Unique identifier for the route
 * @property name Human-readable route name (e.g., "Route 1: Direct Path North")
 * @property waypoints Ordered list of LatLng points forming the polyline
 * @property safeZone Whether this route is currently confirmed safe
 * @property distanceMeters Total route distance in meters
 * @property estimatedWalkMinutes Walking time estimate in minutes
 * @property shelterDestinationId ID of the destination shelter, if any
 */
data class EvacuationRouteGeo(
    val id: String,
    val name: String,
    val waypoints: List<LatLng>,
    val safeZone: Boolean = true,
    val distanceMeters: Double = 0.0,
    val estimatedWalkMinutes: Int = 0,
    val shelterDestinationId: String? = null
)

/**
 * Domain model for a hazard zone polygon displayed on the map.
 *
 * @property id Unique identifier
 * @property name Human-readable description (e.g., "Chemical Spill Zone")
 * @property boundary Ordered polygon boundary points (closed loop)
 * @property hazardType Classification: "fire", "flood", "chemical", "structural", "other"
 * @property severity 1 (low) to 5 (critical)
 */
data class HazardZone(
    val id: String,
    val name: String,
    val boundary: List<LatLng>,
    val hazardType: String = "other",
    val severity: Int = 3
)

/**
 * Domain model for an emergency shelter displayed on the map.
 *
 * @property id Unique identifier
 * @property name Shelter name
 * @property location Geographic position
 * @property address Street address
 * @property capacity Maximum occupancy
 * @property currentOccupancy Current number of occupants
 * @property hasAccessibility ADA / wheelchair accessible
 * @property hasMedical Medical station available on site
 * @property hasPetArea Designated pet-friendly area
 * @property contactPhone Emergency contact phone number
 */
data class Shelter(
    val id: String,
    val name: String,
    val location: LatLng,
    val address: String = "",
    val capacity: Int = 0,
    val currentOccupancy: Int = 0,
    val hasAccessibility: Boolean = false,
    val hasMedical: Boolean = false,
    val hasPetArea: Boolean = false,
    val contactPhone: String = ""
) {
    /** Occupancy as a percentage (0.0 to 1.0). */
    val occupancyPercent: Float
        get() = if (capacity > 0) currentOccupancy.toFloat() / capacity else 0f

    /** true when occupancy >= 80% capacity. */
    val isNearCapacity: Boolean get() = occupancyPercent >= 0.8f

    /** true when occupancy >= 50% capacity. */
    val isModerate: Boolean get() = occupancyPercent >= 0.5f
}

/**
 * Hexagonal port interface for all map-related geospatial data.
 *
 * Three-tier implementation strategy:
 * - **Mock**: In-memory static data for development / UI previews.
 * - **Native**: Firebase Firestore + GeoFire for real-time geospatial queries.
 * - **Live**: Backend REST/gRPC with server-side H3 indexing.
 *
 * All methods are suspend — callers should invoke from a coroutine scope.
 * All methods are safe to call from any dispatcher.
 */
interface MapServicePort {

    /**
     * Retrieve nearby volunteer responders within [radiusMeters] of [center].
     *
     * Results are sorted by distance ascending (closest first).
     * Each Responder includes computed ETA based on distance and assumed walking/driving speed.
     *
     * @param center The user's current position
     * @param radiusMeters Search radius in meters (default 2000m = 2km, max ring)
     * @return List of responders within the radius, sorted by distance
     */
    suspend fun getNearbyResponders(
        center: LatLng,
        radiusMeters: Double = 2000.0
    ): List<com.thewatch.app.data.model.Responder>

    /**
     * Retrieve community-sourced alerts within [radiusMeters] of [center].
     *
     * Community alerts include user reports (suspicious activity, accidents, etc.)
     * with confidence percentages based on corroborating reports.
     *
     * @param center The user's current position
     * @param radiusMeters Search radius in meters (default 2000m)
     * @return List of community alerts, sorted by timestamp descending
     */
    suspend fun getCommunityAlerts(
        center: LatLng,
        radiusMeters: Double = 2000.0
    ): List<com.thewatch.app.data.model.CommunityAlert>

    /**
     * Retrieve evacuation routes originating near [origin].
     *
     * Routes include ordered waypoints for polyline drawing, distance, and
     * estimated walking time. Hazard zones are returned separately via [getHazardZones].
     *
     * @param origin Starting position for route search
     * @return List of evacuation routes, sorted by distance ascending
     */
    suspend fun getEvacuationRoutes(
        origin: LatLng
    ): List<EvacuationRouteGeo>

    /**
     * Retrieve known hazard zones near [center].
     *
     * Hazard zones are closed polygons representing dangerous areas
     * (fire, flood, chemical spill, structural collapse).
     *
     * @param center Reference position
     * @param radiusMeters Search radius in meters (default 5000m)
     * @return List of hazard zone polygons
     */
    suspend fun getHazardZones(
        center: LatLng,
        radiusMeters: Double = 5000.0
    ): List<HazardZone>

    /**
     * Retrieve emergency shelters within [radiusMeters] of [center].
     *
     * Shelters include real-time occupancy data when available.
     * Results are sorted by distance ascending.
     *
     * @param center The user's current position
     * @param radiusMeters Search radius in meters (default 5000m)
     * @return List of shelters with occupancy info
     */
    suspend fun getShelters(
        center: LatLng,
        radiusMeters: Double = 5000.0
    ): List<Shelter>
}
