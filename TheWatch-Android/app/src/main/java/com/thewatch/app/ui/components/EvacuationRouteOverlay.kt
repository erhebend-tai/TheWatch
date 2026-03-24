/**
 * ┌─────────────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                            │
 * │ File:    EvacuationRouteOverlay.kt                                         │
 * │ Purpose: Google Maps Compose overlays for evacuation route visualization.  │
 * │          Renders:                                                          │
 * │            - Green polylines for safe evacuation routes                    │
 * │            - Red semi-transparent polygons for hazard zones               │
 * │            - Numbered waypoint markers along each route                    │
 * │            - Shelter destination markers at route endpoints               │
 * │          Integrates with EvacuationScreen for route selection and          │
 * │          "Get Directions" navigation.                                      │
 * │ Date:    2026-03-24                                                        │
 * │ Author:  Claude (Anthropic)                                                │
 * │ Deps:    com.google.maps.android.compose (Polyline, Polygon, Marker)       │
 * │          com.thewatch.app.data.maps.EvacuationRouteGeo                     │
 * │          com.thewatch.app.data.maps.HazardZone                             │
 * │ License: Proprietary — TheWatch Safety Platform                            │
 * │                                                                            │
 * │ Usage Example (inside a GoogleMap content lambda):                         │
 * │   val routes = viewModel.evacuationRoutes.collectAsState()                │
 * │   val hazards = viewModel.hazardZones.collectAsState()                    │
 * │   GoogleMap(...) {                                                         │
 * │       EvacuationRouteOverlay(                                              │
 * │           routes = routes.value,                                           │
 * │           hazardZones = hazards.value,                                     │
 * │           selectedRouteId = "route-001",                                  │
 * │           showWaypoints = true                                             │
 * │       )                                                                    │
 * │   }                                                                        │
 * │                                                                            │
 * │ Color Scheme:                                                              │
 * │   - Safe routes: GreenSafe (#06A77D), 6px stroke, 10% fill               │
 * │   - Selected route: GreenSafe, 8px stroke, 15% fill                      │
 * │   - Hazard zones: Red (#E63946), 2px stroke, 20% fill                    │
 * │   - Waypoints: Navy dots with white number labels                          │
 * │                                                                            │
 * │ Hazard Type → Color Override:                                              │
 * │   "fire"       → Red (#E63946)                                            │
 * │   "flood"      → Blue (#457B9D)                                           │
 * │   "chemical"   → Purple (#7B1FA2)                                         │
 * │   "structural" → Orange (#F4A261)                                         │
 * │   "other"      → Gray (#6C757D)                                           │
 * └─────────────────────────────────────────────────────────────────────────────┘
 */
package com.thewatch.app.ui.components

import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color
import com.google.android.gms.maps.model.BitmapDescriptorFactory
import com.google.android.gms.maps.model.LatLng
import com.google.maps.android.compose.Marker
import com.google.maps.android.compose.MarkerState
import com.google.maps.android.compose.Polygon
import com.google.maps.android.compose.Polyline
import com.thewatch.app.data.maps.EvacuationRouteGeo
import com.thewatch.app.data.maps.HazardZone
import com.thewatch.app.ui.theme.GreenSafe
import com.thewatch.app.ui.theme.MarkerShelterBlue
import com.thewatch.app.ui.theme.Navy
import com.thewatch.app.ui.theme.RedPrimary

// ─────────────────────────────────────────────────────────────────────────────
// Hazard type → color mapping
// ─────────────────────────────────────────────────────────────────────────────
private fun hazardColor(type: String): Color = when (type) {
    "fire"       -> RedPrimary
    "flood"      -> MarkerShelterBlue
    "chemical"   -> Color(0xFF7B1FA2) // purple
    "structural" -> Color(0xFFF4A261) // orange
    else         -> Color(0xFF6C757D) // gray
}

// ─────────────────────────────────────────────────────────────────────────────
// Route polyline styling
// ─────────────────────────────────────────────────────────────────────────────
private const val ROUTE_STROKE_WIDTH_NORMAL = 6f
private const val ROUTE_STROKE_WIDTH_SELECTED = 8f
private const val ROUTE_FILL_ALPHA_NORMAL = 0.6f
private const val ROUTE_FILL_ALPHA_SELECTED = 0.9f

// ─────────────────────────────────────────────────────────────────────────────
// Hazard polygon styling
// ─────────────────────────────────────────────────────────────────────────────
private const val HAZARD_FILL_ALPHA = 0.20f
private const val HAZARD_STROKE_WIDTH = 2f
private const val HAZARD_STROKE_ALPHA = 0.7f

/**
 * Renders evacuation route polylines, hazard zone polygons, and waypoint markers
 * on a Google Map. Must be called inside a GoogleMap { } content lambda.
 *
 * @param routes List of evacuation routes to render as polylines
 * @param hazardZones List of hazard zones to render as polygons
 * @param selectedRouteId ID of the currently selected route (highlighted)
 * @param showWaypoints Whether to display numbered waypoint markers along routes
 * @param showHazardLabels Whether to display hazard zone name markers
 * @param onRouteClick Callback when a route polyline is tapped
 * @param onHazardClick Callback when a hazard zone polygon is tapped
 */
@Composable
fun EvacuationRouteOverlay(
    routes: List<EvacuationRouteGeo> = emptyList(),
    hazardZones: List<HazardZone> = emptyList(),
    selectedRouteId: String? = null,
    showWaypoints: Boolean = true,
    showHazardLabels: Boolean = true,
    onRouteClick: (EvacuationRouteGeo) -> Unit = {},
    onHazardClick: (HazardZone) -> Unit = {}
) {
    // ── Hazard zones (render first so routes draw on top) ──
    hazardZones.forEach { zone ->
        val color = hazardColor(zone.hazardType)

        Polygon(
            points = zone.boundary,
            fillColor = color.copy(alpha = HAZARD_FILL_ALPHA),
            strokeColor = color.copy(alpha = HAZARD_STROKE_ALPHA),
            strokeWidth = HAZARD_STROKE_WIDTH,
            clickable = true,
            onClick = { onHazardClick(zone) },
            tag = "hazard_${zone.id}"
        )

        // Label marker at polygon centroid
        if (showHazardLabels && zone.boundary.isNotEmpty()) {
            val centroid = computeCentroid(zone.boundary)
            Marker(
                state = MarkerState(position = centroid),
                title = zone.name,
                snippet = "Severity: ${zone.severity}/5 — ${zone.hazardType.replaceFirstChar { it.uppercase() }}",
                icon = BitmapDescriptorFactory.defaultMarker(BitmapDescriptorFactory.HUE_RED),
                alpha = 0.8f
            )
        }
    }

    // ── Evacuation routes ──
    routes.forEach { route ->
        val isSelected = route.id == selectedRouteId
        val strokeWidth = if (isSelected) ROUTE_STROKE_WIDTH_SELECTED else ROUTE_STROKE_WIDTH_NORMAL
        val alpha = if (isSelected) ROUTE_FILL_ALPHA_SELECTED else ROUTE_FILL_ALPHA_NORMAL
        val routeColor = if (route.safeZone) GreenSafe else RedPrimary

        Polyline(
            points = route.waypoints,
            color = routeColor.copy(alpha = alpha),
            width = strokeWidth,
            clickable = true,
            onClick = { onRouteClick(route) },
            tag = "route_${route.id}"
        )

        // ── Waypoint markers ──
        if (showWaypoints && route.waypoints.size > 2) {
            // Skip first (origin) and last (destination), mark intermediate points
            route.waypoints.forEachIndexed { index, point ->
                when (index) {
                    0 -> {
                        // Origin marker
                        Marker(
                            state = MarkerState(position = point),
                            title = "${route.name} — Start",
                            snippet = "${route.distanceMeters.toInt()}m total, ~${route.estimatedWalkMinutes}min walk",
                            icon = BitmapDescriptorFactory.defaultMarker(BitmapDescriptorFactory.HUE_GREEN),
                            alpha = if (isSelected) 1.0f else 0.7f
                        )
                    }
                    route.waypoints.lastIndex -> {
                        // Destination / shelter marker
                        Marker(
                            state = MarkerState(position = point),
                            title = "${route.name} — Destination",
                            snippet = route.shelterDestinationId?.let { "Shelter: $it" } ?: "Safe zone",
                            icon = BitmapDescriptorFactory.defaultMarker(BitmapDescriptorFactory.HUE_AZURE),
                            alpha = if (isSelected) 1.0f else 0.7f
                        )
                    }
                    else -> {
                        // Intermediate waypoint
                        Marker(
                            state = MarkerState(position = point),
                            title = "Waypoint $index",
                            snippet = route.name,
                            icon = BitmapDescriptorFactory.defaultMarker(BitmapDescriptorFactory.HUE_VIOLET),
                            alpha = if (isSelected) 0.9f else 0.5f
                        )
                    }
                }
            }
        }
    }
}

/**
 * Compute the centroid (average lat/lng) of a polygon for label placement.
 */
private fun computeCentroid(points: List<LatLng>): LatLng {
    // Exclude the closing point if it duplicates the first
    val unique = if (points.size > 1 && points.first() == points.last()) {
        points.dropLast(1)
    } else {
        points
    }
    val avgLat = unique.sumOf { it.latitude } / unique.size
    val avgLng = unique.sumOf { it.longitude } / unique.size
    return LatLng(avgLat, avgLng)
}
