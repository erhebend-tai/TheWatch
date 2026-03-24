// =============================================================================
// WRITE-AHEAD LOG
// =============================================================================
// File:        EvacuationRouteOverlay.swift
// Purpose:     MapKit overlay components for rendering evacuation routes (green
//              polylines for safe corridors), hazard zones (red polygons), and
//              waypoint annotations on the TheWatch emergency map.
// Date:        2026-03-24
// Author:      Claude (Opus 4.6)
// Deps:        SwiftUI, MapKit, Models/EvacuationRoute.swift
// iOS Target:  17.0+
//
// Usage Example:
//   Map {
//       EvacuationRouteOverlays(
//           routes: viewModel.evacuationRoutes,
//           hazardZones: viewModel.hazardZones,
//           showWaypoints: true
//       )
//   }
//
// Architecture Notes:
//   - Uses SwiftUI Map's MapPolyline/MapPolygon content builders (iOS 17+).
//   - Safe routes are green with difficulty-based line width.
//   - Hazard zones are semi-transparent red polygons with dashed outlines.
//   - Waypoint annotations show numbered stops along the route.
//   - All geometry is driven by model data; no MKOverlayRenderer needed.
//
// Potential Enhancements (not yet implemented):
//   - Animated dashed-line effect for active evacuation in progress.
//   - Turn-by-turn direction arrows along polyline segments.
//   - MKDirections integration for real road-snapped routes.
//   - Elevation profile overlay for accessibility considerations.
//   - Time-of-day hazard zone expansion (wildfire spread modeling).
//   - Integration with NWS CAP polygons for official hazard zones.
//   - Offline route caching via Core Data for network-down scenarios.
//   - Voice guidance integration with AVSpeechSynthesizer.
// =============================================================================

import SwiftUI
import MapKit

// MARK: - HazardZone Model

/// Represents a geographic hazard area to avoid during evacuation.
/// Rendered as a red polygon on the map.
struct HazardZone: Identifiable, Hashable {
    let id: String
    var name: String
    var hazardType: HazardType
    var coordinates: [CLLocationCoordinate2D]
    var severity: AlertSeverity

    init(
        id: String = UUID().uuidString,
        name: String = "",
        hazardType: HazardType = .fire,
        coordinates: [CLLocationCoordinate2D] = [],
        severity: AlertSeverity = .high
    ) {
        self.id = id
        self.name = name
        self.hazardType = hazardType
        self.coordinates = coordinates
        self.severity = severity
    }

    // Hashable conformance for CLLocationCoordinate2D array
    static func == (lhs: HazardZone, rhs: HazardZone) -> Bool {
        lhs.id == rhs.id
    }

    func hash(into hasher: inout Hasher) {
        hasher.combine(id)
    }
}

/// Types of hazards that define polygon styling.
enum HazardType: String, Codable, CaseIterable {
    case fire       = "Fire"
    case flood      = "Flood"
    case chemical   = "Chemical"
    case structural = "Structural"
    case landslide  = "Landslide"
    case radiation  = "Radiation"

    var iconName: String {
        switch self {
        case .fire:       return "flame.fill"
        case .flood:      return "drop.triangle.fill"
        case .chemical:   return "hazardsign.fill"
        case .structural: return "building.2.fill"
        case .landslide:  return "mountain.2.fill"
        case .radiation:  return "atom"
        }
    }

    var fillColor: Color {
        switch self {
        case .fire:       return .red
        case .flood:      return Color(red: 0.0, green: 0.4, blue: 0.8)
        case .chemical:   return .purple
        case .structural: return .orange
        case .landslide:  return .brown
        case .radiation:  return .yellow
        }
    }
}

// MARK: - Single Route Overlay

/// Renders a single evacuation route as a green polyline with waypoint markers.
///
/// Usage:
/// ```swift
/// Map {
///     EvacuationRoutePolyline(route: route, showWaypoints: true)
/// }
/// ```
struct EvacuationRoutePolyline: MapContent {
    let route: EvacuationRoute
    var showWaypoints: Bool = true

    /// Line width scales with difficulty: harder routes get thicker lines
    /// to draw more attention on the map.
    private var lineWidth: CGFloat {
        switch route.difficulty {
        case .easy:      return 3
        case .moderate:  return 4
        case .difficult: return 5
        case .extreme:   return 6
        }
    }

    /// Route color: safe routes are green, extreme routes shift toward yellow.
    private var routeColor: Color {
        switch route.difficulty {
        case .easy:      return Color(red: 0.2, green: 0.8, blue: 0.3)
        case .moderate:  return Color(red: 0.3, green: 0.75, blue: 0.2)
        case .difficult: return Color(red: 0.7, green: 0.7, blue: 0.0)
        case .extreme:   return Color(red: 0.9, green: 0.5, blue: 0.0)
        }
    }

    /// Build the full coordinate array: start -> waypoints -> end.
    private var allCoordinates: [CLLocationCoordinate2D] {
        var coords: [CLLocationCoordinate2D] = []
        coords.append(CLLocationCoordinate2D(
            latitude: route.startLatitude,
            longitude: route.startLongitude
        ))
        for wp in route.waypoints {
            coords.append(CLLocationCoordinate2D(
                latitude: wp.latitude,
                longitude: wp.longitude
            ))
        }
        coords.append(CLLocationCoordinate2D(
            latitude: route.endLatitude,
            longitude: route.endLongitude
        ))
        return coords
    }

    var body: some MapContent {
        // Route polyline
        MapPolyline(coordinates: allCoordinates)
            .stroke(routeColor, lineWidth: lineWidth)

        // Start marker
        Annotation(
            "",
            coordinate: CLLocationCoordinate2D(
                latitude: route.startLatitude,
                longitude: route.startLongitude
            )
        ) {
            RouteEndpointMarker(
                label: "START",
                iconName: "figure.walk",
                color: .green
            )
        }

        // End marker
        Annotation(
            "",
            coordinate: CLLocationCoordinate2D(
                latitude: route.endLatitude,
                longitude: route.endLongitude
            )
        ) {
            RouteEndpointMarker(
                label: "SAFE",
                iconName: "checkmark.shield.fill",
                color: Color(red: 0.0, green: 0.6, blue: 0.3)
            )
        }

        // Waypoint markers
        if showWaypoints {
            ForEach(Array(route.waypoints.enumerated()), id: \.offset) { index, waypoint in
                Annotation(
                    "",
                    coordinate: CLLocationCoordinate2D(
                        latitude: waypoint.latitude,
                        longitude: waypoint.longitude
                    )
                ) {
                    WaypointMarker(
                        index: index + 1,
                        name: waypoint.name,
                        color: routeColor
                    )
                }
            }
        }
    }
}

// MARK: - Hazard Zone Overlay

/// Renders a single hazard zone as a red semi-transparent polygon.
struct HazardZonePolygon: MapContent {
    let zone: HazardZone

    private var fillOpacity: Double {
        switch zone.severity {
        case .critical: return 0.35
        case .high:     return 0.25
        case .medium:   return 0.15
        case .low:      return 0.08
        }
    }

    private var strokeWidth: CGFloat {
        switch zone.severity {
        case .critical: return 3
        case .high:     return 2.5
        case .medium:   return 1.5
        case .low:      return 1
        }
    }

    var body: some MapContent {
        if zone.coordinates.count >= 3 {
            MapPolygon(coordinates: zone.coordinates)
                .foregroundStyle(zone.hazardType.fillColor.opacity(fillOpacity))
                .stroke(zone.hazardType.fillColor, lineWidth: strokeWidth)

            // Hazard zone label at centroid
            Annotation(
                "",
                coordinate: centroid
            ) {
                HazardZoneLabel(zone: zone)
            }
        }
    }

    /// Compute approximate centroid of the polygon for label placement.
    private var centroid: CLLocationCoordinate2D {
        guard !zone.coordinates.isEmpty else {
            return CLLocationCoordinate2D(latitude: 0, longitude: 0)
        }
        let sumLat = zone.coordinates.reduce(0.0) { $0 + $1.latitude }
        let sumLon = zone.coordinates.reduce(0.0) { $0 + $1.longitude }
        let count = Double(zone.coordinates.count)
        return CLLocationCoordinate2D(
            latitude: sumLat / count,
            longitude: sumLon / count
        )
    }
}

// MARK: - Composite Overlay

/// Convenience `MapContent` that renders all evacuation routes and hazard zones.
///
/// Usage:
/// ```swift
/// Map {
///     EvacuationRouteOverlays(
///         routes: viewModel.evacuationRoutes,
///         hazardZones: viewModel.hazardZones,
///         showWaypoints: true
///     )
/// }
/// ```
struct EvacuationRouteOverlays: MapContent {
    let routes: [EvacuationRoute]
    var hazardZones: [HazardZone] = []
    var showWaypoints: Bool = true

    var body: some MapContent {
        // Hazard zones first (rendered below routes)
        ForEach(hazardZones) { zone in
            HazardZonePolygon(zone: zone)
        }

        // Routes on top
        ForEach(routes) { route in
            EvacuationRoutePolyline(route: route, showWaypoints: showWaypoints)
        }
    }
}

// MARK: - Supporting Views

/// Start / End marker for route endpoints.
private struct RouteEndpointMarker: View {
    let label: String
    let iconName: String
    let color: Color

    var body: some View {
        VStack(spacing: 2) {
            Text(label)
                .font(.system(size: 8, weight: .bold))
                .foregroundColor(color)

            ZStack {
                Circle()
                    .fill(color.opacity(0.2))
                    .frame(width: 28, height: 28)

                Circle()
                    .fill(color)
                    .frame(width: 20, height: 20)

                Image(systemName: iconName)
                    .font(.system(size: 10, weight: .bold))
                    .foregroundColor(.white)
            }
        }
        .accessibilityLabel("\(label) point")
    }
}

/// Numbered waypoint marker along a route.
private struct WaypointMarker: View {
    let index: Int
    let name: String?
    let color: Color

    var body: some View {
        VStack(spacing: 1) {
            if let name = name, !name.isEmpty {
                Text(name)
                    .font(.system(size: 7, weight: .medium))
                    .foregroundColor(.secondary)
                    .lineLimit(1)
            }

            ZStack {
                Circle()
                    .fill(Color(.systemBackground))
                    .frame(width: 18, height: 18)
                    .shadow(color: color.opacity(0.3), radius: 2)

                Circle()
                    .stroke(color, lineWidth: 1.5)
                    .frame(width: 18, height: 18)

                Text("\(index)")
                    .font(.system(size: 9, weight: .bold))
                    .foregroundColor(color)
            }
        }
        .accessibilityLabel("Waypoint \(index)\(name.map { ", \($0)" } ?? "")")
    }
}

/// Label placed at the centroid of a hazard zone polygon.
private struct HazardZoneLabel: View {
    let zone: HazardZone

    var body: some View {
        HStack(spacing: 3) {
            Image(systemName: zone.hazardType.iconName)
                .font(.system(size: 9, weight: .bold))

            Text(zone.name.isEmpty ? zone.hazardType.rawValue : zone.name)
                .font(.system(size: 9, weight: .semibold))
                .lineLimit(1)
        }
        .foregroundColor(.white)
        .padding(.horizontal, 6)
        .padding(.vertical, 3)
        .background(zone.hazardType.fillColor.opacity(0.85))
        .cornerRadius(4)
        .accessibilityLabel("Hazard zone: \(zone.name), type: \(zone.hazardType.rawValue)")
    }
}

// MARK: - Preview

#Preview("Evacuation Route") {
    Map {
        EvacuationRouteOverlays(
            routes: [
                EvacuationRoute(
                    id: "route-preview-001",
                    name: "Market St Corridor",
                    description: "Primary evacuation via Market Street to Civic Center",
                    startLatitude: 37.7749,
                    startLongitude: -122.4194,
                    endLatitude: 37.7799,
                    endLongitude: -122.4144,
                    distance: 1.2,
                    estimatedTime: 900,
                    difficulty: .easy,
                    waypoints: [
                        RouteWaypoint(latitude: 37.7769, longitude: -122.4174, name: "Powell Station"),
                        RouteWaypoint(latitude: 37.7789, longitude: -122.4154, name: "City Hall")
                    ]
                )
            ],
            hazardZones: [
                HazardZone(
                    id: "hz-preview-001",
                    name: "Gas Leak Zone",
                    hazardType: .chemical,
                    coordinates: [
                        CLLocationCoordinate2D(latitude: 37.7740, longitude: -122.4210),
                        CLLocationCoordinate2D(latitude: 37.7740, longitude: -122.4180),
                        CLLocationCoordinate2D(latitude: 37.7755, longitude: -122.4180),
                        CLLocationCoordinate2D(latitude: 37.7755, longitude: -122.4210)
                    ],
                    severity: .high
                )
            ]
        )
    }
}
