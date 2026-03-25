// ============================================================================
// WRITE-AHEAD LOG
// ============================================================================
// File:         ApiAlertService.swift
// Purpose:      AlertServiceProtocol implementation backed by WatchApiClient.
//               Calls POST /api/response/trigger, GET /api/response/active,
//               and POST /api/response/{id}/cancel via the HTTP API.
// Date:         2026-03-24
// Author:       Claude
// Dependencies: WatchApiClient, WatchHubConnection
//
// Usage Example:
//   let service = ApiAlertService()
//   let alert = try await service.createAlert(userId: "uid", severity: .critical,
//       type: .sos, latitude: 37.77, longitude: -122.41, description: "Help")
//   let active = try await service.getActiveAlerts(userId: "uid")
// ============================================================================

import Foundation

@Observable
final class ApiAlertService: AlertServiceProtocol {

    private let apiClient = WatchApiClient.shared
    private let hubConnection = WatchHubConnection.shared

    func createAlert(
        userId: String,
        severity: AlertSeverity,
        type: AlertType,
        latitude: Double,
        longitude: Double,
        description: String
    ) async throws -> Alert {
        let response = try await apiClient.triggerResponse(
            userId: userId,
            scope: mapSeverityToScope(severity),
            latitude: latitude,
            longitude: longitude,
            description: description,
            triggerSource: "MANUAL_BUTTON"
        )

        // Join SignalR response group for real-time updates
        hubConnection.joinResponseGroup(requestId: response.requestId)

        return Alert(
            id: response.requestId,
            userId: userId,
            severity: severity,
            type: type,
            latitude: latitude,
            longitude: longitude,
            description: description,
            status: .active,
            createdAt: Date()
        )
    }

    func getActiveAlerts(userId: String) async throws -> [Alert] {
        let responses = try await apiClient.getActiveResponses(userId: userId)
        return responses.map { dto in
            Alert(
                id: dto.requestId,
                userId: dto.userId ?? userId,
                severity: mapScopeToSeverity(dto.scope ?? ""),
                type: .sos,
                latitude: dto.latitude ?? 0,
                longitude: dto.longitude ?? 0,
                description: "Active response: \(dto.scope ?? "")",
                status: .active,
                createdAt: parseDate(dto.createdAt),
                responderIds: dto.acknowledgedResponders?.map { $0.responderId ?? "" } ?? []
            )
        }
    }

    func updateAlertStatus(alertId: String, status: AlertStatus) async throws {
        switch status {
        case .cancelled:
            _ = try await apiClient.cancelResponse(requestId: alertId)
            hubConnection.leaveResponseGroup(requestId: alertId)
        case .resolved:
            _ = try await apiClient.resolveResponse(requestId: alertId)
            hubConnection.leaveResponseGroup(requestId: alertId)
        default:
            break
        }
    }

    func getNearbyAlerts(
        latitude: Double,
        longitude: Double,
        radiusMeters: Double
    ) async throws -> [CommunityAlert] {
        // Community alerts endpoint not yet available on backend;
        // return empty for now. Will wire when GET /api/alerts/nearby is added.
        return []
    }

    // ── Helpers ──────────────────────────────────────────────────

    private func mapSeverityToScope(_ severity: AlertSeverity) -> String {
        switch severity {
        case .low: return "CheckIn"
        case .medium: return "CheckIn"
        case .high: return "Emergency"
        case .critical: return "Emergency"
        }
    }

    private func mapScopeToSeverity(_ scope: String) -> AlertSeverity {
        switch scope {
        case "Emergency": return .critical
        case "CommunityWatch": return .high
        case "CheckIn": return .medium
        default: return .medium
        }
    }

    private func parseDate(_ str: String?) -> Date {
        guard let str else { return Date() }
        let formatter = ISO8601DateFormatter()
        return formatter.date(from: str) ?? Date()
    }
}
