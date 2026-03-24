// =============================================================================
// WRITE-AHEAD LOG
// =============================================================================
// File:        MockMapDataService.swift
// Purpose:     Mock adapter implementing MapDataServiceProtocol for SwiftUI
//              previews, unit tests, and offline development. Returns realistic
//              coordinate data centered around San Francisco (37.7749, -122.4194)
//              with varied responder statuses, alert types, shelter capacities,
//              evacuation routes, and hazard zones.
// Date:        2026-03-24
// Author:      Claude (Opus 4.6)
// Deps:        Foundation, CoreLocation,
//              Services/MapDataService.swift (protocol),
//              Models/Responder.swift,
//              Models/CommunityAlert.swift,
//              Models/EvacuationRoute.swift,
//              Models/Shelter.swift,
//              Components/EvacuationRouteOverlay.swift (HazardZone)
// iOS Target:  17.0+
//
// Usage Example:
//   let service = MockMapDataService()
//   let responders = try await service.fetchNearbyResponders(
//       latitude: 37.7749, longitude: -122.4194, radiusMeters: 5000
//   )
//   // Returns 8 mock responders near SF Financial District
//
//   // In SwiftUI Environment:
//   HomeView()
//       .environment(MockMapDataService())
//
// Architecture Notes:
//   - This is an ADAPTER in hexagonal architecture, implementing the
//     MapDataServiceProtocol port.
//   - Simulated network latency via Task.sleep (200-500ms).
//   - All coordinates are realistic San Francisco locations.
//   - @Observable so it can be injected via SwiftUI @Environment.
//   - Thread-safe: all state is read-only after init.
//
// Mock Data Coverage:
//   - 8 responders: mix of roles (volunteer, EMT, firefighter, police, medical),
//     statuses (available, onCall, offDuty), and distances (200m - 4km).
//   - 5 community alerts: medical, gas leak, flood, security, wildfire.
//   - 3 evacuation routes: easy, moderate, difficult with waypoints.
//   - 4 shelters: varying capacities (50-500), open/closed, pet-friendly.
//   - 2 hazard zones: fire and chemical spill polygons.
// =============================================================================

import Foundation
import CoreLocation

// MARK: - MockMapDataService

@Observable
final class MockMapDataService: MapDataServiceProtocol, @unchecked Sendable {

    /// Simulated network delay range in nanoseconds.
    private let minDelay: UInt64 = 200_000_000  // 200ms
    private let maxDelay: UInt64 = 500_000_000  // 500ms

    /// Whether to simulate random failures (useful for error-state testing).
    var simulateErrors: Bool = false

    /// Error rate when simulateErrors is true (0.0 - 1.0).
    var errorRate: Double = 0.15

    // MARK: - Init

    init(simulateErrors: Bool = false) {
        self.simulateErrors = simulateErrors
    }

    // MARK: - Private Helpers

    private func simulateLatency() async throws {
        let delay = UInt64.random(in: minDelay...maxDelay)
        try await Task.sleep(nanoseconds: delay)

        if simulateErrors && Double.random(in: 0...1) < errorRate {
            throw MockMapDataError.simulatedNetworkFailure
        }
    }

    // MARK: - Responders

    func fetchNearbyResponders(
        latitude: Double,
        longitude: Double,
        radiusMeters: Double
    ) async throws -> [Responder] {
        try await simulateLatency()

        // Base coordinates near the requested location with realistic offsets
        let baseLat = latitude
        let baseLon = longitude

        return [
            Responder(
                id: "resp-001",
                name: "Sarah Chen",
                role: .emt,
                latitude: baseLat + 0.002,
                longitude: baseLon - 0.001,
                distance: 250,
                skills: ["CPR", "AED", "Advanced First Aid", "Trauma"],
                isVerified: true,
                responseTime: 180,
                status: .available,
                availability: .available
            ),
            Responder(
                id: "resp-002",
                name: "Marcus Johnson",
                role: .firefighter,
                latitude: baseLat - 0.003,
                longitude: baseLon + 0.002,
                distance: 450,
                skills: ["Fire Suppression", "Rescue", "HazMat Level A"],
                isVerified: true,
                responseTime: 300,
                status: .onCall,
                availability: .busy
            ),
            Responder(
                id: "resp-003",
                name: "Elena Rodriguez",
                role: .volunteer,
                latitude: baseLat + 0.005,
                longitude: baseLon + 0.003,
                distance: 680,
                skills: ["CPR", "First Aid"],
                isVerified: true,
                responseTime: 420,
                status: .available,
                availability: .available
            ),
            Responder(
                id: "resp-004",
                name: "James Wright",
                role: .police,
                latitude: baseLat - 0.001,
                longitude: baseLon - 0.004,
                distance: 520,
                skills: ["Law Enforcement", "Crisis Negotiation", "First Aid"],
                isVerified: true,
                responseTime: 240,
                status: .available,
                availability: .available
            ),
            Responder(
                id: "resp-005",
                name: "Dr. Aisha Patel",
                role: .medical,
                latitude: baseLat + 0.008,
                longitude: baseLon - 0.005,
                distance: 1200,
                skills: ["Emergency Medicine", "Trauma Surgery", "ACLS"],
                isVerified: true,
                responseTime: 600,
                status: .available,
                availability: .available
            ),
            Responder(
                id: "resp-006",
                name: "Tommy Nakamura",
                role: .volunteer,
                latitude: baseLat - 0.006,
                longitude: baseLon - 0.008,
                distance: 1500,
                skills: ["CPR", "Search and Rescue"],
                isVerified: false,
                responseTime: 780,
                status: .offDuty,
                availability: .unavailable
            ),
            Responder(
                id: "resp-007",
                name: "Lisa Park",
                role: .emt,
                latitude: baseLat + 0.012,
                longitude: baseLon + 0.008,
                distance: 2100,
                skills: ["Paramedic", "Pediatric Emergency", "AED"],
                isVerified: true,
                responseTime: 900,
                status: .onCall,
                availability: .busy
            ),
            Responder(
                id: "resp-008",
                name: "Carlos Mendez",
                role: .firefighter,
                latitude: baseLat - 0.015,
                longitude: baseLon + 0.010,
                distance: 3800,
                skills: ["Fire Suppression", "USAR", "Water Rescue"],
                isVerified: true,
                responseTime: 1200,
                status: .available,
                availability: .available
            )
        ].sorted { $0.distance < $1.distance }
    }

    // MARK: - Community Alerts

    func fetchCommunityAlerts(
        latitude: Double,
        longitude: Double,
        radiusMeters: Double
    ) async throws -> [CommunityAlert] {
        try await simulateLatency()

        let baseLat = latitude
        let baseLon = longitude

        return [
            CommunityAlert(
                id: "ca-001",
                title: "Medical Emergency - Market St",
                description: "Pedestrian struck by vehicle at Market & 5th. Multiple injuries reported. EMS on scene.",
                alertType: "Emergency",
                latitude: baseLat + 0.001,
                longitude: baseLon - 0.002,
                radius: 300,
                severity: .critical,
                isActive: true,
                createdBy: "SF Fire Dept",
                createdAt: Date(timeIntervalSinceNow: -600),
                respondingCount: 8
            ),
            CommunityAlert(
                id: "ca-002",
                title: "Gas Leak - Howard Street",
                description: "PG&E reports natural gas leak in 2-block radius. Evacuate immediately if within 500m.",
                alertType: "Emergency",
                latitude: baseLat - 0.003,
                longitude: baseLon + 0.001,
                radius: 500,
                severity: .high,
                isActive: true,
                createdBy: "PG&E Emergency",
                createdAt: Date(timeIntervalSinceNow: -1800),
                respondingCount: 15
            ),
            CommunityAlert(
                id: "ca-003",
                title: "Flash Flood Warning",
                description: "NWS flash flood warning in effect until 10 PM. Low-lying areas near Mission Creek at risk.",
                alertType: "Warning",
                latitude: baseLat - 0.008,
                longitude: baseLon - 0.005,
                radius: 2000,
                severity: .high,
                isActive: true,
                createdBy: "National Weather Service",
                createdAt: Date(timeIntervalSinceNow: -3600),
                respondingCount: 3
            ),
            CommunityAlert(
                id: "ca-004",
                title: "Suspicious Activity - Union Square",
                description: "Multiple reports of suspicious individual near parking garage entrance. SFPD responding.",
                alertType: "Warning",
                latitude: baseLat + 0.006,
                longitude: baseLon + 0.004,
                radius: 200,
                severity: .medium,
                isActive: true,
                createdBy: "Community Watch",
                createdAt: Date(timeIntervalSinceNow: -900),
                respondingCount: 2
            ),
            CommunityAlert(
                id: "ca-005",
                title: "Wildfire Smoke Advisory",
                description: "Air quality index exceeds 150 due to Napa County wildfire. Limit outdoor activity. N95 masks recommended.",
                alertType: "Information",
                latitude: baseLat + 0.015,
                longitude: baseLon - 0.010,
                radius: 10000,
                severity: .medium,
                isActive: true,
                createdBy: "Bay Area Air Quality Mgmt District",
                createdAt: Date(timeIntervalSinceNow: -7200),
                respondingCount: 0
            )
        ].sorted { $0.severity.sortOrder < $1.severity.sortOrder }
    }

    // MARK: - Evacuation Routes

    func fetchEvacuationRoutes(
        latitude: Double,
        longitude: Double,
        radiusMeters: Double
    ) async throws -> [EvacuationRoute] {
        try await simulateLatency()

        let baseLat = latitude
        let baseLon = longitude

        return [
            // Route 1: Market St Corridor (Easy, pedestrian)
            EvacuationRoute(
                id: "evac-001",
                name: "Market St Corridor - Civic Center",
                description: "Primary pedestrian evacuation route via Market Street to Civic Center Plaza. ADA accessible, wide sidewalks.",
                startLatitude: baseLat,
                startLongitude: baseLon,
                endLatitude: baseLat + 0.008,
                endLongitude: baseLon + 0.012,
                distance: 1.4,
                estimatedTime: 1080,
                difficulty: .easy,
                lastUpdated: Date(timeIntervalSinceNow: -300),
                waypoints: [
                    RouteWaypoint(latitude: baseLat + 0.002, longitude: baseLon + 0.003, name: "Powell Station"),
                    RouteWaypoint(latitude: baseLat + 0.004, longitude: baseLon + 0.006, name: "UN Plaza"),
                    RouteWaypoint(latitude: baseLat + 0.006, longitude: baseLon + 0.009, name: "City Hall")
                ]
            ),
            // Route 2: Embarcadero Waterfront (Moderate, longer but open)
            EvacuationRoute(
                id: "evac-002",
                name: "Embarcadero Waterfront Route",
                description: "Secondary route along the Embarcadero to Pier 39 staging area. Open waterfront, vehicle access.",
                startLatitude: baseLat,
                startLongitude: baseLon,
                endLatitude: baseLat + 0.015,
                endLongitude: baseLon - 0.008,
                distance: 3.2,
                estimatedTime: 2400,
                difficulty: .moderate,
                hazards: ["Uneven pavement near Pier 14", "Construction zone at Brannan"],
                lastUpdated: Date(timeIntervalSinceNow: -600),
                waypoints: [
                    RouteWaypoint(latitude: baseLat + 0.003, longitude: baseLon - 0.005, name: "Ferry Building"),
                    RouteWaypoint(latitude: baseLat + 0.007, longitude: baseLon - 0.006, name: "Pier 15 Exploratorium"),
                    RouteWaypoint(latitude: baseLat + 0.011, longitude: baseLon - 0.007, name: "Pier 33"),
                    RouteWaypoint(latitude: baseLat + 0.013, longitude: baseLon - 0.008, name: "Fisherman's Wharf")
                ]
            ),
            // Route 3: Twin Peaks Highland (Difficult, steep terrain)
            EvacuationRoute(
                id: "evac-003",
                name: "Twin Peaks Highland Evacuation",
                description: "Emergency vehicle route to Twin Peaks staging area. Steep grades, not ADA accessible. Wildfire evacuation primary.",
                startLatitude: baseLat,
                startLongitude: baseLon,
                endLatitude: baseLat - 0.020,
                endLongitude: baseLon + 0.025,
                distance: 5.8,
                estimatedTime: 4200,
                difficulty: .difficult,
                hazards: ["Steep grade on Clayton St", "Narrow road at Portola", "Limited cell coverage in canyon"],
                lastUpdated: Date(timeIntervalSinceNow: -1200),
                waypoints: [
                    RouteWaypoint(latitude: baseLat - 0.005, longitude: baseLon + 0.006, name: "Castro Station"),
                    RouteWaypoint(latitude: baseLat - 0.010, longitude: baseLon + 0.012, name: "Diamond Heights"),
                    RouteWaypoint(latitude: baseLat - 0.015, longitude: baseLon + 0.018, name: "Portola Staging")
                ]
            )
        ].sorted { $0.estimatedTime < $1.estimatedTime }
    }

    // MARK: - Shelters

    func fetchShelters(
        latitude: Double,
        longitude: Double,
        radiusMeters: Double
    ) async throws -> [Shelter] {
        try await simulateLatency()

        let baseLat = latitude
        let baseLon = longitude

        return [
            Shelter(
                id: "shelter-001",
                name: "Civic Center Shelter",
                address: "1 Dr Carlton B Goodlett Pl, San Francisco, CA 94102",
                latitude: baseLat + 0.008,
                longitude: baseLon + 0.012,
                capacity: 200,
                currentOccupancy: 75,
                services: ["Medical", "Food", "Water", "Blankets", "Counseling"],
                phone: "415-555-0100",
                website: "https://sf.gov/shelters/civic-center",
                isOpen: true,
                operatingHours: "24/7 during emergency",
                pets: true,
                wheelchairAccessible: true
            ),
            Shelter(
                id: "shelter-002",
                name: "Moscone Convention Center",
                address: "747 Howard St, San Francisco, CA 94103",
                latitude: baseLat - 0.002,
                longitude: baseLon + 0.005,
                capacity: 500,
                currentOccupancy: 420,
                services: ["Medical", "Food", "Water", "Blankets", "Shower", "Charging", "Childcare"],
                phone: "415-555-0200",
                website: "https://sf.gov/shelters/moscone",
                isOpen: true,
                operatingHours: "24/7 during emergency",
                pets: false,
                wheelchairAccessible: true
            ),
            Shelter(
                id: "shelter-003",
                name: "Marina Middle School",
                address: "3500 Fillmore St, San Francisco, CA 94123",
                latitude: baseLat + 0.018,
                longitude: baseLon - 0.010,
                capacity: 150,
                currentOccupancy: 0,
                services: ["Food", "Water"],
                phone: "415-555-0300",
                isOpen: false,
                operatingHours: "Closed - not yet activated",
                pets: false,
                wheelchairAccessible: true
            ),
            Shelter(
                id: "shelter-004",
                name: "St. Mary's Cathedral Community Hall",
                address: "1111 Gough St, San Francisco, CA 94109",
                latitude: baseLat + 0.010,
                longitude: baseLon + 0.008,
                capacity: 80,
                currentOccupancy: 35,
                services: ["Food", "Water", "Blankets", "Counseling"],
                phone: "415-555-0400",
                isOpen: true,
                operatingHours: "6 AM - 10 PM",
                pets: true,
                wheelchairAccessible: false
            )
        ].sorted { $0.availableBeds > $1.availableBeds }
    }

    // MARK: - Hazard Zones

    func fetchHazardZones(
        latitude: Double,
        longitude: Double,
        radiusMeters: Double
    ) async throws -> [HazardZone] {
        try await simulateLatency()

        let baseLat = latitude
        let baseLon = longitude

        return [
            // Gas leak / chemical hazard zone
            HazardZone(
                id: "hz-001",
                name: "Gas Leak Exclusion Zone",
                hazardType: .chemical,
                coordinates: [
                    CLLocationCoordinate2D(latitude: baseLat - 0.004, longitude: baseLon + 0.000),
                    CLLocationCoordinate2D(latitude: baseLat - 0.004, longitude: baseLon + 0.003),
                    CLLocationCoordinate2D(latitude: baseLat - 0.002, longitude: baseLon + 0.003),
                    CLLocationCoordinate2D(latitude: baseLat - 0.002, longitude: baseLon + 0.000)
                ],
                severity: .high
            ),
            // Flood zone near Mission Creek
            HazardZone(
                id: "hz-002",
                name: "Mission Creek Flood Zone",
                hazardType: .flood,
                coordinates: [
                    CLLocationCoordinate2D(latitude: baseLat - 0.010, longitude: baseLon - 0.007),
                    CLLocationCoordinate2D(latitude: baseLat - 0.010, longitude: baseLon - 0.003),
                    CLLocationCoordinate2D(latitude: baseLat - 0.007, longitude: baseLon - 0.002),
                    CLLocationCoordinate2D(latitude: baseLat - 0.006, longitude: baseLon - 0.004),
                    CLLocationCoordinate2D(latitude: baseLat - 0.008, longitude: baseLon - 0.007)
                ],
                severity: .medium
            )
        ]
    }
}

// MARK: - Sort Helper

private extension AlertSeverity {
    /// Lower number = higher priority for sorting (critical first).
    var sortOrder: Int {
        switch self {
        case .critical: return 0
        case .high:     return 1
        case .medium:   return 2
        case .low:      return 3
        }
    }
}

// MARK: - Mock Error

enum MockMapDataError: Error, LocalizedError {
    case simulatedNetworkFailure
    case simulatedTimeout
    case simulatedAuthFailure

    var errorDescription: String? {
        switch self {
        case .simulatedNetworkFailure:
            return "Simulated network failure (mock)"
        case .simulatedTimeout:
            return "Simulated request timeout (mock)"
        case .simulatedAuthFailure:
            return "Simulated authentication failure (mock)"
        }
    }
}
