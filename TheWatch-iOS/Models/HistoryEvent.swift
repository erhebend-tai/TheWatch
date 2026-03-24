import Foundation

struct HistoryEvent: Codable, Hashable, Identifiable {
    let id: String
    let userId: String
    var eventType: String // "SOS", "Safety Check", "Wellness", etc.
    var severity: AlertSeverity
    var status: AlertStatus
    var latitude: Double
    var longitude: Double
    var description: String
    var respondersCount: Int
    var createdAt: Date
    var resolvedAt: Date?
    var duration: TimeInterval?

    var displayDuration: String {
        guard let duration = duration else { return "—" }
        let minutes = Int(duration / 60)
        if minutes < 1 {
            return "< 1 min"
        } else if minutes < 60 {
            return "\(minutes) min"
        } else {
            let hours = minutes / 60
            let remainingMinutes = minutes % 60
            return "\(hours)h \(remainingMinutes)m"
        }
    }

    init(
        id: String = UUID().uuidString,
        userId: String = "",
        eventType: String = "SOS",
        severity: AlertSeverity = .medium,
        status: AlertStatus = .active,
        latitude: Double = 0,
        longitude: Double = 0,
        description: String = "",
        respondersCount: Int = 0,
        createdAt: Date = Date(),
        resolvedAt: Date? = nil,
        duration: TimeInterval? = nil
    ) {
        self.id = id
        self.userId = userId
        self.eventType = eventType
        self.severity = severity
        self.status = status
        self.latitude = latitude
        self.longitude = longitude
        self.description = description
        self.respondersCount = respondersCount
        self.createdAt = createdAt
        self.resolvedAt = resolvedAt
        self.duration = duration
    }
}
