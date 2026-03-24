import Foundation

protocol AlertServiceProtocol {
    func createAlert(userId: String, severity: AlertSeverity, type: AlertType, latitude: Double, longitude: Double, description: String) async throws -> Alert
    func getActiveAlerts(userId: String) async throws -> [Alert]
    func updateAlertStatus(alertId: String, status: AlertStatus) async throws
    func getNearbyAlerts(latitude: Double, longitude: Double, radiusMeters: Double) async throws -> [CommunityAlert]
}

@Observable
final class MockAlertService: AlertServiceProtocol {
    func createAlert(userId: String, severity: AlertSeverity, type: AlertType, latitude: Double, longitude: Double, description: String) async throws -> Alert {
        try await Task.sleep(nanoseconds: 500_000_000)
        return Alert(
            id: UUID().uuidString,
            userId: userId,
            severity: severity,
            type: type,
            latitude: latitude,
            longitude: longitude,
            description: description,
            status: .active
        )
    }

    func getActiveAlerts(userId: String) async throws -> [Alert] {
        try await Task.sleep(nanoseconds: 300_000_000)
        return [
            Alert(
                id: "alert-001",
                userId: userId,
                severity: .critical,
                type: .sos,
                latitude: 37.7749,
                longitude: -122.4194,
                description: "Medical emergency",
                status: .active,
                responderIds: ["resp-001", "resp-002"],
                createdAt: Date(timeIntervalSinceNow: -300)
            )
        ]
    }

    func updateAlertStatus(alertId: String, status: AlertStatus) async throws {
        try await Task.sleep(nanoseconds: 300_000_000)
    }

    func getNearbyAlerts(latitude: Double, longitude: Double, radiusMeters: Double) async throws -> [CommunityAlert] {
        try await Task.sleep(nanoseconds: 400_000_000)
        return [
            CommunityAlert(
                id: "comm-alert-001",
                title: "Gas Leak on Market Street",
                description: "Local utility company reports gas leak in 3-block radius",
                alertType: "Emergency",
                latitude: 37.7749,
                longitude: -122.4194,
                radius: 500,
                severity: .high,
                isActive: true,
                createdBy: "SF Fire Dept",
                createdAt: Date(timeIntervalSinceNow: -1800),
                respondingCount: 12
            ),
            CommunityAlert(
                id: "comm-alert-002",
                title: "Severe Weather Warning",
                description: "Flash flood warning in effect until 10 PM",
                alertType: "Warning",
                latitude: 37.7849,
                longitude: -122.4094,
                radius: 2000,
                severity: .high,
                isActive: true,
                createdBy: "National Weather Service",
                createdAt: Date(timeIntervalSinceNow: -3600),
                respondingCount: 8
            )
        ]
    }
}
