import Foundation

struct CommunityAlert: Codable, Hashable, Identifiable {
    let id: String
    var title: String
    var description: String
    var alertType: String // "Emergency", "Warning", "Information"
    var latitude: Double
    var longitude: Double
    var radius: Double // in meters
    var severity: AlertSeverity
    var isActive: Bool
    var createdBy: String
    var createdAt: Date
    var expiresAt: Date?
    var respondingCount: Int

    init(
        id: String = UUID().uuidString,
        title: String = "",
        description: String = "",
        alertType: String = "Emergency",
        latitude: Double = 0,
        longitude: Double = 0,
        radius: Double = 500,
        severity: AlertSeverity = .high,
        isActive: Bool = true,
        createdBy: String = "",
        createdAt: Date = Date(),
        expiresAt: Date? = nil,
        respondingCount: Int = 0
    ) {
        self.id = id
        self.title = title
        self.description = description
        self.alertType = alertType
        self.latitude = latitude
        self.longitude = longitude
        self.radius = radius
        self.severity = severity
        self.isActive = isActive
        self.createdBy = createdBy
        self.createdAt = createdAt
        self.expiresAt = expiresAt
        self.respondingCount = respondingCount
    }
}
