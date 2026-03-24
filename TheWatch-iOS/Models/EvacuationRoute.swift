import Foundation

struct EvacuationRoute: Codable, Hashable, Identifiable {
    let id: String
    var name: String
    var description: String
    var startLatitude: Double
    var startLongitude: Double
    var endLatitude: Double
    var endLongitude: Double
    var distance: Double // in kilometers
    var estimatedTime: TimeInterval // in seconds
    var difficulty: DifficultyLevel
    var hazards: [String]
    var lastUpdated: Date
    var waypoints: [RouteWaypoint]

    init(
        id: String = UUID().uuidString,
        name: String = "",
        description: String = "",
        startLatitude: Double = 0,
        startLongitude: Double = 0,
        endLatitude: Double = 0,
        endLongitude: Double = 0,
        distance: Double = 0,
        estimatedTime: TimeInterval = 0,
        difficulty: DifficultyLevel = .moderate,
        hazards: [String] = [],
        lastUpdated: Date = Date(),
        waypoints: [RouteWaypoint] = []
    ) {
        self.id = id
        self.name = name
        self.description = description
        self.startLatitude = startLatitude
        self.startLongitude = startLongitude
        self.endLatitude = endLatitude
        self.endLongitude = endLongitude
        self.distance = distance
        self.estimatedTime = estimatedTime
        self.difficulty = difficulty
        self.hazards = hazards
        self.lastUpdated = lastUpdated
        self.waypoints = waypoints
    }

    var estimatedTimeDisplay: String {
        let hours = Int(estimatedTime / 3600)
        let minutes = Int((estimatedTime.truncatingRemainder(dividingBy: 3600)) / 60)
        if hours > 0 {
            return "\(hours)h \(minutes)min"
        } else {
            return "\(minutes)min"
        }
    }
}

struct RouteWaypoint: Codable, Hashable {
    let latitude: Double
    let longitude: Double
    let name: String?
}

enum DifficultyLevel: String, Codable, CaseIterable {
    case easy = "Easy"
    case moderate = "Moderate"
    case difficult = "Difficult"
    case extreme = "Extreme"
}
