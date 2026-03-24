/**
 * ┌─────────────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                            │
 * │ File:    MockMapServiceAdapter.kt                                          │
 * │ Purpose: Mock (Tier 1) adapter for MapServicePort. Provides realistic      │
 * │          static geospatial data around NYC (40.7128, -74.0060) for         │
 * │          development and UI preview. No network, no disk, no Firebase.     │
 * │          All data is deterministic and repeatable for screenshot testing.   │
 * │ Date:    2026-03-24                                                        │
 * │ Author:  Claude (Anthropic)                                                │
 * │ Deps:    com.thewatch.app.data.maps.MapServicePort                         │
 * │          com.thewatch.app.data.model.Responder                             │
 * │          com.thewatch.app.data.model.CommunityAlert                        │
 * │          com.google.android.gms.maps.model.LatLng                          │
 * │          javax.inject (Hilt DI)                                            │
 * │ License: Proprietary — TheWatch Safety Platform                            │
 * │                                                                            │
 * │ Usage Example:                                                             │
 * │   // Wired via Hilt in AppModule:                                          │
 * │   @Singleton @Provides                                                     │
 * │   fun provideMapServicePort(): MapServicePort = MockMapServiceAdapter()    │
 * │                                                                            │
 * │   // Direct usage in tests:                                                │
 * │   val adapter = MockMapServiceAdapter()                                    │
 * │   val responders = runBlocking {                                           │
 * │       adapter.getNearbyResponders(LatLng(40.7128, -74.0060), 2000.0)      │
 * │   }                                                                        │
 * │   assert(responders.isNotEmpty())                                          │
 * │                                                                            │
 * │ Notes:                                                                     │
 * │   - Mock center is Lower Manhattan (40.7128, -74.0060).                   │
 * │   - Responders placed at realistic offsets within 500m, 1km, 2km rings.   │
 * │   - Community alerts include varied types and confidence levels.           │
 * │   - Evacuation routes follow real-ish NYC street grid orientations.        │
 * │   - Shelters have varied occupancy levels for UI testing.                  │
 * └─────────────────────────────────────────────────────────────────────────────┘
 */
package com.thewatch.app.data.maps.mock

import android.util.Log
import com.google.android.gms.maps.model.LatLng
import com.thewatch.app.data.maps.EvacuationRouteGeo
import com.thewatch.app.data.maps.HazardZone
import com.thewatch.app.data.maps.MapServicePort
import com.thewatch.app.data.maps.Shelter
import com.thewatch.app.data.model.CommunityAlert
import com.thewatch.app.data.model.Responder
import java.time.LocalDateTime
import javax.inject.Inject
import javax.inject.Singleton

@Singleton
class MockMapServiceAdapter @Inject constructor() : MapServicePort {

    companion object {
        private const val TAG = "TheWatch.MockMap"

        // ── Mock center: Lower Manhattan ──
        private const val CENTER_LAT = 40.7128
        private const val CENTER_LNG = -74.0060
    }

    // ─────────────────────────────────────────────────────────────────────────
    // getNearbyResponders
    // ─────────────────────────────────────────────────────────────────────────
    override suspend fun getNearbyResponders(
        center: LatLng,
        radiusMeters: Double
    ): List<Responder> {
        Log.d(TAG, "getNearbyResponders(center=$center, radius=$radiusMeters)")

        return listOf(
            // ── Critical ring (< 500m) ──
            Responder(
                id = "resp-001",
                name = "Michael Chen",
                type = "EMT",
                distance = 250.0,
                latitude = center.latitude + 0.0020,
                longitude = center.longitude + 0.0010,
                eta = 3, // minutes
                certifications = listOf("EMT-B", "CPR", "First Aid")
            ),
            Responder(
                id = "resp-002",
                name = "Sarah Anderson",
                type = "Nurse",
                distance = 450.0,
                latitude = center.latitude - 0.0030,
                longitude = center.longitude - 0.0020,
                eta = 5,
                certifications = listOf("RN", "ACLS", "PALS")
            ),
            // ── Primary ring (500m - 1km) ──
            Responder(
                id = "resp-003",
                name = "James Walker",
                type = "Volunteer",
                distance = 720.0,
                latitude = center.latitude + 0.0050,
                longitude = center.longitude - 0.0035,
                eta = 8,
                certifications = listOf("CPR", "Stop the Bleed")
            ),
            Responder(
                id = "resp-004",
                name = "Elena Rodriguez",
                type = "Paramedic",
                distance = 880.0,
                latitude = center.latitude - 0.0060,
                longitude = center.longitude + 0.0045,
                eta = 6, // driving
                certifications = listOf("Paramedic", "ACLS", "PHTLS")
            ),
            // ── Secondary ring (1km - 2km) ──
            Responder(
                id = "resp-005",
                name = "David Kim",
                type = "Off-Duty Officer",
                distance = 1350.0,
                latitude = center.latitude + 0.0100,
                longitude = center.longitude + 0.0060,
                eta = 10,
                certifications = listOf("Law Enforcement", "Tactical Medic")
            ),
            Responder(
                id = "resp-006",
                name = "Rachel Torres",
                type = "Volunteer",
                distance = 1800.0,
                latitude = center.latitude - 0.0120,
                longitude = center.longitude - 0.0090,
                eta = 14,
                certifications = listOf("CPR", "Wilderness First Aid")
            ),
            Responder(
                id = "resp-007",
                name = "Marcus Johnson",
                type = "Firefighter",
                distance = 1950.0,
                latitude = center.latitude + 0.0140,
                longitude = center.longitude - 0.0070,
                eta = 7, // driving
                certifications = listOf("Firefighter II", "HazMat Ops", "EMT-B")
            )
        ).filter { it.distance <= radiusMeters }
            .sortedBy { it.distance }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // getCommunityAlerts
    // ─────────────────────────────────────────────────────────────────────────
    override suspend fun getCommunityAlerts(
        center: LatLng,
        radiusMeters: Double
    ): List<CommunityAlert> {
        Log.d(TAG, "getCommunityAlerts(center=$center, radius=$radiusMeters)")

        return listOf(
            CommunityAlert(
                id = "alert-001",
                type = "Suspicious Activity",
                latitude = center.latitude + 0.0015,
                longitude = center.longitude - 0.0025,
                description = "Individual loitering near school entrance for 30+ minutes. Multiple reports from parents.",
                timestamp = LocalDateTime.now().minusMinutes(12),
                severity = "High",
                respondersCount = 3
            ),
            CommunityAlert(
                id = "alert-002",
                type = "Traffic Accident",
                latitude = center.latitude - 0.0040,
                longitude = center.longitude + 0.0030,
                description = "Two-vehicle collision at intersection. Minor injuries reported. Traffic blocked.",
                timestamp = LocalDateTime.now().minusMinutes(25),
                severity = "Medium",
                respondersCount = 5
            ),
            CommunityAlert(
                id = "alert-003",
                type = "Power Outage",
                latitude = center.latitude + 0.0080,
                longitude = center.longitude + 0.0050,
                description = "Block-wide power outage affecting 6 buildings. Utility company notified.",
                timestamp = LocalDateTime.now().minusMinutes(45),
                severity = "Low",
                respondersCount = 0
            ),
            CommunityAlert(
                id = "alert-004",
                type = "Medical Emergency",
                latitude = center.latitude - 0.0020,
                longitude = center.longitude - 0.0010,
                description = "Person collapsed on sidewalk. Bystanders providing CPR. EMS dispatched.",
                timestamp = LocalDateTime.now().minusMinutes(3),
                severity = "Critical",
                respondersCount = 2
            ),
            CommunityAlert(
                id = "alert-005",
                type = "Flooding",
                latitude = center.latitude + 0.0060,
                longitude = center.longitude - 0.0080,
                description = "Storm drain backup causing street-level flooding. Water rising to curb height.",
                timestamp = LocalDateTime.now().minusMinutes(60),
                severity = "Medium",
                respondersCount = 1
            )
        )
    }

    // ─────────────────────────────────────────────────────────────────────────
    // getEvacuationRoutes
    // ─────────────────────────────────────────────────────────────────────────
    override suspend fun getEvacuationRoutes(
        origin: LatLng
    ): List<EvacuationRouteGeo> {
        Log.d(TAG, "getEvacuationRoutes(origin=$origin)")

        return listOf(
            // Route 1: North toward Central Park
            EvacuationRouteGeo(
                id = "route-001",
                name = "Route 1: Direct Path North",
                waypoints = listOf(
                    origin,
                    LatLng(origin.latitude + 0.005, origin.longitude),
                    LatLng(origin.latitude + 0.010, origin.longitude + 0.002),
                    LatLng(origin.latitude + 0.015, origin.longitude + 0.002),
                    LatLng(origin.latitude + 0.020, origin.longitude + 0.003) // shelter
                ),
                safeZone = true,
                distanceMeters = 2500.0,
                estimatedWalkMinutes = 12,
                shelterDestinationId = "shelter-001"
            ),
            // Route 2: West toward Hudson River Park
            EvacuationRouteGeo(
                id = "route-002",
                name = "Route 2: Riverside Alternative",
                waypoints = listOf(
                    origin,
                    LatLng(origin.latitude + 0.002, origin.longitude - 0.005),
                    LatLng(origin.latitude + 0.005, origin.longitude - 0.010),
                    LatLng(origin.latitude + 0.008, origin.longitude - 0.014),
                    LatLng(origin.latitude + 0.010, origin.longitude - 0.016) // shelter
                ),
                safeZone = true,
                distanceMeters = 3200.0,
                estimatedWalkMinutes = 16,
                shelterDestinationId = "shelter-002"
            ),
            // Route 3: East toward subway junction
            EvacuationRouteGeo(
                id = "route-003",
                name = "Route 3: Subway Junction",
                waypoints = listOf(
                    origin,
                    LatLng(origin.latitude + 0.003, origin.longitude + 0.004),
                    LatLng(origin.latitude + 0.007, origin.longitude + 0.007),
                    LatLng(origin.latitude + 0.012, origin.longitude + 0.008) // shelter
                ),
                safeZone = true,
                distanceMeters = 1800.0,
                estimatedWalkMinutes = 9,
                shelterDestinationId = "shelter-003"
            )
        )
    }

    // ─────────────────────────────────────────────────────────────────────────
    // getHazardZones
    // ─────────────────────────────────────────────────────────────────────────
    override suspend fun getHazardZones(
        center: LatLng,
        radiusMeters: Double
    ): List<HazardZone> {
        Log.d(TAG, "getHazardZones(center=$center, radius=$radiusMeters)")

        return listOf(
            HazardZone(
                id = "hazard-001",
                name = "Chemical Spill Zone",
                boundary = listOf(
                    LatLng(center.latitude - 0.003, center.longitude + 0.006),
                    LatLng(center.latitude - 0.001, center.longitude + 0.008),
                    LatLng(center.latitude + 0.001, center.longitude + 0.007),
                    LatLng(center.latitude - 0.001, center.longitude + 0.005),
                    LatLng(center.latitude - 0.003, center.longitude + 0.006) // closed
                ),
                hazardType = "chemical",
                severity = 4
            ),
            HazardZone(
                id = "hazard-002",
                name = "Structural Collapse Risk",
                boundary = listOf(
                    LatLng(center.latitude + 0.004, center.longitude - 0.003),
                    LatLng(center.latitude + 0.006, center.longitude - 0.002),
                    LatLng(center.latitude + 0.006, center.longitude - 0.005),
                    LatLng(center.latitude + 0.004, center.longitude - 0.005),
                    LatLng(center.latitude + 0.004, center.longitude - 0.003) // closed
                ),
                hazardType = "structural",
                severity = 3
            ),
            HazardZone(
                id = "hazard-003",
                name = "Flood Zone — Storm Drain Overflow",
                boundary = listOf(
                    LatLng(center.latitude + 0.007, center.longitude - 0.008),
                    LatLng(center.latitude + 0.009, center.longitude - 0.006),
                    LatLng(center.latitude + 0.010, center.longitude - 0.009),
                    LatLng(center.latitude + 0.008, center.longitude - 0.011),
                    LatLng(center.latitude + 0.007, center.longitude - 0.008) // closed
                ),
                hazardType = "flood",
                severity = 2
            )
        )
    }

    // ─────────────────────────────────────────────────────────────────────────
    // getShelters
    // ─────────────────────────────────────────────────────────────────────────
    override suspend fun getShelters(
        center: LatLng,
        radiusMeters: Double
    ): List<Shelter> {
        Log.d(TAG, "getShelters(center=$center, radius=$radiusMeters)")

        return listOf(
            Shelter(
                id = "shelter-001",
                name = "Central Park Emergency Shelter",
                location = LatLng(center.latitude + 0.020, center.longitude + 0.003),
                address = "Central Park South, Manhattan, NY",
                capacity = 500,
                currentOccupancy = 45, // < 50% — green
                hasAccessibility = true,
                hasMedical = true,
                hasPetArea = true,
                contactPhone = "212-555-0100"
            ),
            Shelter(
                id = "shelter-002",
                name = "Riverside Community Center",
                location = LatLng(center.latitude + 0.010, center.longitude - 0.016),
                address = "Riverside Drive, Manhattan, NY",
                capacity = 300,
                currentOccupancy = 195, // 65% — yellow
                hasAccessibility = true,
                hasMedical = false,
                hasPetArea = false,
                contactPhone = "212-555-0200"
            ),
            Shelter(
                id = "shelter-003",
                name = "Grand Central Terminal Shelter",
                location = LatLng(center.latitude + 0.012, center.longitude + 0.008),
                address = "42nd Street, Manhattan, NY",
                capacity = 1000,
                currentOccupancy = 870, // 87% — red
                hasAccessibility = true,
                hasMedical = true,
                hasPetArea = true,
                contactPhone = "212-555-0300"
            ),
            Shelter(
                id = "shelter-004",
                name = "PS 234 School Gymnasium",
                location = LatLng(center.latitude - 0.005, center.longitude - 0.008),
                address = "Greenwich St, Manhattan, NY",
                capacity = 150,
                currentOccupancy = 20, // 13% — green
                hasAccessibility = false,
                hasMedical = false,
                hasPetArea = false,
                contactPhone = "212-555-0400"
            ),
            Shelter(
                id = "shelter-005",
                name = "Battery Park Community Hub",
                location = LatLng(center.latitude - 0.010, center.longitude + 0.002),
                address = "Battery Place, Manhattan, NY",
                capacity = 200,
                currentOccupancy = 160, // 80% — red
                hasAccessibility = true,
                hasMedical = true,
                hasPetArea = false,
                contactPhone = "212-555-0500"
            )
        )
    }
}
