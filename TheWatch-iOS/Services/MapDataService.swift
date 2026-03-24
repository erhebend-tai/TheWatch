// =============================================================================
// WRITE-AHEAD LOG
// =============================================================================
// File:        MapDataService.swift
// Purpose:     Protocol (port) defining the contract for fetching map overlay
//              data: nearby responders, community alerts, evacuation routes,
//              shelters, and hazard zones. Follows hexagonal / port-adapter
//              architecture per project standard.
// Date:        2026-03-24
// Author:      Claude (Opus 4.6)
// Deps:        Foundation, CoreLocation,
//              Models/Responder.swift,
//              Models/CommunityAlert.swift,
//              Models/EvacuationRoute.swift,
//              Models/Shelter.swift,
//              Components/EvacuationRouteOverlay.swift (HazardZone)
// iOS Target:  17.0+
//
// Usage Example:
//   // Inject via @Environment or init parameter:
//   let service: any MapDataServiceProtocol = MockMapDataService()
//   let responders = try await service.fetchNearbyResponders(
//       latitude: 37.7749, longitude: -122.4194, radiusMeters: 5000
//   )
//
// Architecture Notes:
//   - This is the PORT in hexagonal architecture. Adapters (Firebase, REST,
//     gRPC, mock) implement this protocol.
//   - All methods are async throws to support both network and local sources.
//   - Location parameters use raw Double lat/lon for maximum portability
//     across adapters (some backends expect GeoJSON, others GeoHash, etc.).
//   - The protocol is class-constrained (AnyObject) so adapters can be
//     @Observable for SwiftUI environment injection.
//
// Potential Adapters (not yet implemented):
//   - FirebaseMapDataAdapter   -- Firestore GeoHash queries
//   - RESTMapDataAdapter       -- REST API with GeoJSON responses
//   - GRPCMapDataAdapter       -- Protocol Buffers streaming
//   - CoreDataMapDataAdapter   -- Offline-first local cache
//   - CloudKitMapDataAdapter   -- Apple CloudKit zone queries
//   - AWSMapDataAdapter        -- DynamoDB + API Gateway
//   - AzureMapDataAdapter      -- Cosmos DB + Azure Functions
//   - OracleMapDataAdapter     -- Oracle Spatial queries
//   - GraphQLMapDataAdapter    -- Apollo/Relay subscriptions
//   - WebSocketMapDataAdapter  -- Real-time push updates
// =============================================================================

import Foundation
import CoreLocation

// MARK: - MapDataServiceProtocol (Port)

/// Hexagonal port for all map-related data fetching.
///
/// Adapters implement this protocol to provide responders, alerts,
/// evacuation routes, shelters, and hazard zones from any backend.
///
/// ```swift
/// // In production DI container:
/// let mapService: any MapDataServiceProtocol = FirebaseMapDataAdapter(db: Firestore.firestore())
///
/// // In SwiftUI previews / tests:
/// let mapService: any MapDataServiceProtocol = MockMapDataService()
/// ```
protocol MapDataServiceProtocol: AnyObject, Sendable {

    // MARK: - Responders

    /// Fetch volunteer/professional responders within a radius of the given coordinate.
    ///
    /// - Parameters:
    ///   - latitude:     Center latitude (WGS84).
    ///   - longitude:    Center longitude (WGS84).
    ///   - radiusMeters: Search radius in meters. Typical range: 500 - 50_000.
    /// - Returns: Array of `Responder` sorted by distance (nearest first).
    /// - Throws: Network, auth, or decoding errors.
    func fetchNearbyResponders(
        latitude: Double,
        longitude: Double,
        radiusMeters: Double
    ) async throws -> [Responder]

    // MARK: - Community Alerts

    /// Fetch active community-sourced alerts within a radius.
    ///
    /// - Parameters:
    ///   - latitude:     Center latitude (WGS84).
    ///   - longitude:    Center longitude (WGS84).
    ///   - radiusMeters: Search radius in meters.
    /// - Returns: Array of `CommunityAlert` sorted by severity (highest first).
    /// - Throws: Network, auth, or decoding errors.
    func fetchCommunityAlerts(
        latitude: Double,
        longitude: Double,
        radiusMeters: Double
    ) async throws -> [CommunityAlert]

    // MARK: - Evacuation Routes

    /// Fetch evacuation routes originating near the given coordinate.
    ///
    /// - Parameters:
    ///   - latitude:     Origin latitude (WGS84).
    ///   - longitude:    Origin longitude (WGS84).
    ///   - radiusMeters: Search radius for route start points.
    /// - Returns: Array of `EvacuationRoute` sorted by estimated time (fastest first).
    /// - Throws: Network, auth, or decoding errors.
    func fetchEvacuationRoutes(
        latitude: Double,
        longitude: Double,
        radiusMeters: Double
    ) async throws -> [EvacuationRoute]

    // MARK: - Shelters

    /// Fetch emergency shelters within a radius.
    ///
    /// - Parameters:
    ///   - latitude:     Center latitude (WGS84).
    ///   - longitude:    Center longitude (WGS84).
    ///   - radiusMeters: Search radius in meters.
    /// - Returns: Array of `Shelter` sorted by available capacity (most beds first).
    /// - Throws: Network, auth, or decoding errors.
    func fetchShelters(
        latitude: Double,
        longitude: Double,
        radiusMeters: Double
    ) async throws -> [Shelter]

    // MARK: - Hazard Zones

    /// Fetch active hazard zone polygons that intersect the given bounding box.
    ///
    /// - Parameters:
    ///   - latitude:     Center latitude (WGS84).
    ///   - longitude:    Center longitude (WGS84).
    ///   - radiusMeters: Approximate bounding radius.
    /// - Returns: Array of `HazardZone` polygons.
    /// - Throws: Network, auth, or decoding errors.
    func fetchHazardZones(
        latitude: Double,
        longitude: Double,
        radiusMeters: Double
    ) async throws -> [HazardZone]
}

// MARK: - Default Implementations

extension MapDataServiceProtocol {
    /// Default hazard zones returns empty (not all backends support this yet).
    func fetchHazardZones(
        latitude: Double,
        longitude: Double,
        radiusMeters: Double
    ) async throws -> [HazardZone] {
        return []
    }
}
