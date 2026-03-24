import Foundation

protocol HistoryServiceProtocol {
    func getHistory(userId: String, limit: Int, offset: Int) async throws -> [HistoryEvent]
    func getHistoryEvent(eventId: String) async throws -> HistoryEvent
    func filterHistory(userId: String, dateRange: ClosedRange<Date>, eventTypes: [String], severities: [AlertSeverity], statuses: [AlertStatus]) async throws -> [HistoryEvent]
}

@Observable
final class MockHistoryService: HistoryServiceProtocol {
    func getHistory(userId: String, limit: Int, offset: Int) async throws -> [HistoryEvent] {
        try await Task.sleep(nanoseconds: 300_000_000)
        return [
            HistoryEvent(
                id: "event-001",
                userId: userId,
                eventType: "SOS",
                severity: .critical,
                status: .resolved,
                latitude: 37.7749,
                longitude: -122.4194,
                description: "Medical emergency - lost consciousness",
                respondersCount: 4,
                createdAt: Date(timeIntervalSinceNow: -86400 * 5),
                resolvedAt: Date(timeIntervalSinceNow: -86400 * 5 + 1200),
                duration: 1200
            ),
            HistoryEvent(
                id: "event-002",
                userId: userId,
                eventType: "Safety Check",
                severity: .medium,
                status: .resolved,
                latitude: 37.7849,
                longitude: -122.4094,
                description: "Automated check-in - no response",
                respondersCount: 1,
                createdAt: Date(timeIntervalSinceNow: -86400 * 3),
                resolvedAt: Date(timeIntervalSinceNow: -86400 * 3 + 180),
                duration: 180
            ),
            HistoryEvent(
                id: "event-003",
                userId: userId,
                eventType: "Wellness",
                severity: .low,
                status: .resolved,
                latitude: 37.7949,
                longitude: -122.4294,
                description: "Manual wellness check-in",
                respondersCount: 0,
                createdAt: Date(timeIntervalSinceNow: -86400 * 2),
                resolvedAt: Date(timeIntervalSinceNow: -86400 * 2 + 30),
                duration: 30
            ),
            HistoryEvent(
                id: "event-004",
                userId: userId,
                eventType: "SOS",
                severity: .high,
                status: .resolved,
                latitude: 37.8049,
                longitude: -122.3994,
                description: "Fall detected by Apple Watch",
                respondersCount: 3,
                createdAt: Date(timeIntervalSinceNow: -86400 * 1),
                resolvedAt: Date(timeIntervalSinceNow: -86400 * 1 + 900),
                duration: 900
            ),
            HistoryEvent(
                id: "event-005",
                userId: userId,
                eventType: "Safety Check",
                severity: .medium,
                status: .acknowledged,
                latitude: 37.8149,
                longitude: -122.3894,
                description: "Scheduled daily check-in",
                respondersCount: 2,
                createdAt: Date(timeIntervalSinceNow: -3600),
                resolvedAt: nil,
                duration: nil
            )
        ]
    }

    func getHistoryEvent(eventId: String) async throws -> HistoryEvent {
        try await Task.sleep(nanoseconds: 200_000_000)
        return HistoryEvent(
            id: eventId,
            userId: "user-001",
            eventType: "SOS",
            severity: .critical,
            status: .resolved,
            latitude: 37.7749,
            longitude: -122.4194,
            description: "Medical emergency - lost consciousness. Responders arrived on scene at 5:23 PM. Patient transported to SF General Hospital via ambulance.",
            respondersCount: 4,
            createdAt: Date(timeIntervalSinceNow: -86400 * 5),
            resolvedAt: Date(timeIntervalSinceNow: -86400 * 5 + 1200),
            duration: 1200
        )
    }

    func filterHistory(userId: String, dateRange: ClosedRange<Date>, eventTypes: [String], severities: [AlertSeverity], statuses: [AlertStatus]) async throws -> [HistoryEvent] {
        try await Task.sleep(nanoseconds: 400_000_000)
        let allEvents = try await getHistory(userId: userId, limit: 100, offset: 0)
        return allEvents.filter { event in
            dateRange.contains(event.createdAt) &&
            (eventTypes.isEmpty || eventTypes.contains(event.eventType)) &&
            (severities.isEmpty || severities.contains(event.severity)) &&
            (statuses.isEmpty || statuses.contains(event.status))
        }
    }
}
