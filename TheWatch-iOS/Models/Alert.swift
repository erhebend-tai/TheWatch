import Foundation

struct Alert: Codable, Hashable, Identifiable {
    let id: String
    let userId: String
    var severity: AlertSeverity
    var type: AlertType
    var latitude: Double
    var longitude: Double
    var description: String
    var status: AlertStatus
    var createdAt: Date
    var resolvedAt: Date?
    var responderIds: [String]
    var communityAlertId: String?

    init(
        id: String = UUID().uuidString,
        userId: String = "",
        severity: AlertSeverity = .medium,
        type: AlertType = .sos,
        latitude: Double = 0,
        longitude: Double = 0,
        description: String = "",
        status: AlertStatus = .active,
        createdAt: Date = Date(),
        resolvedAt: Date? = nil,
        responderIds: [String] = [],
        communityAlertId: String? = nil
    ) {
        self.id = id
        self.userId = userId
        self.severity = severity
        self.type = type
        self.latitude = latitude
        self.longitude = longitude
        self.description = description
        self.status = status
        self.createdAt = createdAt
        self.resolvedAt = resolvedAt
        self.responderIds = responderIds
        self.communityAlertId = communityAlertId
    }
}

enum AlertSeverity: String, Codable, CaseIterable {
    case low = "Low"
    case medium = "Medium"
    case high = "High"
    case critical = "Critical"

    var color: String {
        switch self {
        case .low:
            return "gray"
        case .medium:
            return "yellow"
        case .high:
            return "orange"
        case .critical:
            return "red"
        }
    }
}

enum AlertType: String, Codable, CaseIterable {
    case sos = "SOS"
    case safetyCheck = "Safety Check"
    case accident = "Accident"
    case wellness = "Wellness"
    case custom = "Custom"
}

enum AlertStatus: String, Codable, CaseIterable {
    case active = "Active"
    case acknowledged = "Acknowledged"
    case resolved = "Resolved"
    case cancelled = "Cancelled"
}
