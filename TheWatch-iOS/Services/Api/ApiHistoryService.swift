// ============================================================================
// WRITE-AHEAD LOG
// ============================================================================
// File:         ApiHistoryService.swift
// Purpose:      HistoryServiceProtocol implementation backed by WatchApiClient.
//               Fetches history from GET /api/response/active/{userId}.
// Date:         2026-03-24
// Author:       Claude
// Dependencies: WatchApiClient
//
// Usage Example:
//   let service = ApiHistoryService()
//   let events = try await service.getHistory(userId: "uid", limit: 50, offset: 0)
// ============================================================================

import Foundation

@Observable
final class ApiHistoryService: HistoryServiceProtocol {

    private let apiClient = WatchApiClient.shared

    func getHistory(userId: String, limit: Int, offset: Int) async throws -> [HistoryEvent] {
        let responses = try await apiClient.getActiveResponses(userId: userId)
        return responses.map { dto in
            HistoryEvent(
                id: dto.requestId,
                userId: dto.userId ?? userId,
                eventType: dto.scope ?? "SOS",
                severity: mapScopeToSeverity(dto.scope ?? ""),
                status: mapStatus(dto.status ?? ""),
                latitude: dto.latitude ?? 0,
                longitude: dto.longitude ?? 0,
                description: "Response: \(dto.scope ?? "") - \(dto.status ?? "")",
                respondersCount: dto.acknowledgedResponders?.count ?? 0,
                createdAt: parseDate(dto.createdAt),
                resolvedAt: nil,
                duration: nil
            )
        }
    }

    func getHistoryEvent(eventId: String) async throws -> HistoryEvent {
        let situation = try await apiClient.getSituation(requestId: eventId)
        guard let req = situation.request else {
            throw ApiError.httpError(statusCode: 404, body: "Not found")
        }

        return HistoryEvent(
            id: req.requestId,
            userId: req.userId ?? "",
            eventType: req.scope ?? "SOS",
            severity: mapScopeToSeverity(req.scope ?? ""),
            status: mapStatus(req.status ?? ""),
            latitude: req.latitude ?? 0,
            longitude: req.longitude ?? 0,
            description: "Response: \(req.scope ?? "")",
            respondersCount: req.acknowledgedResponders?.count ?? 0,
            createdAt: parseDate(req.createdAt),
            resolvedAt: nil,
            duration: nil
        )
    }

    func filterHistory(
        userId: String,
        dateRange: ClosedRange<Date>,
        eventTypes: [String],
        severities: [AlertSeverity],
        statuses: [AlertStatus]
    ) async throws -> [HistoryEvent] {
        let allEvents = try await getHistory(userId: userId, limit: 100, offset: 0)
        return allEvents.filter { event in
            dateRange.contains(event.createdAt) &&
            (eventTypes.isEmpty || eventTypes.contains(event.eventType)) &&
            (severities.isEmpty || severities.contains(event.severity)) &&
            (statuses.isEmpty || statuses.contains(event.status))
        }
    }

    // ── Helpers ──────────────────────────────────────────────────

    private func mapScopeToSeverity(_ scope: String) -> AlertSeverity {
        switch scope {
        case "Emergency": return .critical
        case "CommunityWatch": return .high
        case "CheckIn": return .medium
        default: return .medium
        }
    }

    private func mapStatus(_ status: String) -> AlertStatus {
        switch status.lowercased() {
        case "active", "pending", "dispatching": return .active
        case "acknowledged": return .acknowledged
        case "resolved", "completed": return .resolved
        case "cancelled": return .cancelled
        default: return .active
        }
    }

    private func parseDate(_ str: String?) -> Date {
        guard let str else { return Date() }
        let formatter = ISO8601DateFormatter()
        return formatter.date(from: str) ?? Date()
    }
}
