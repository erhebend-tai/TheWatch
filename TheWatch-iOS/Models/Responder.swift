import Foundation

struct Responder: Codable, Hashable, Identifiable {
    let id: String
    var name: String
    var role: ResponderRole
    var latitude: Double
    var longitude: Double
    var distance: Double // in meters
    var skills: [String]
    var isVerified: Bool
    var responseTime: TimeInterval? // in seconds
    var status: ResponderStatus
    var availability: AvailabilityStatus
    var hasVehicle: Bool

    init(
        id: String = UUID().uuidString,
        name: String = "",
        role: ResponderRole = .volunteer,
        latitude: Double = 0,
        longitude: Double = 0,
        distance: Double = 0,
        skills: [String] = [],
        isVerified: Bool = true,
        responseTime: TimeInterval? = nil,
        status: ResponderStatus = .available,
        availability: AvailabilityStatus = .available,
        hasVehicle: Bool = false
    ) {
        self.id = id
        self.name = name
        self.role = role
        self.latitude = latitude
        self.longitude = longitude
        self.distance = distance
        self.skills = skills
        self.isVerified = isVerified
        self.responseTime = responseTime
        self.status = status
        self.availability = availability
        self.hasVehicle = hasVehicle
    }

    var distanceDisplay: String {
        if distance < 1000 {
            return String(format: "%.0f m", distance)
        } else {
            return String(format: "%.1f km", distance / 1000)
        }
    }
}

/// Navigation directions returned by the API after a responder acknowledges an incident.
/// Contains deep links to launch Apple Maps, Google Maps, or Waze with turn-by-turn navigation.
///
/// Example usage:
///   if let directions = ackResponse.directions,
///      let url = URL(string: directions.appleMapsUrl) {
///       UIApplication.shared.open(url)
///   }
struct NavigationDirections: Codable, Hashable {
    let travelMode: String           // "driving" or "walking"
    let distanceMeters: Double
    let estimatedTravelTimeMinutes: Double?
    let googleMapsUrl: String
    let appleMapsUrl: String
    let wazeUrl: String
}

/// Response returned when a responder acknowledges an incident.
/// Includes navigation directions to the incident location.
struct AcknowledgmentResponse: Codable, Hashable {
    let ackId: String
    let requestId: String
    let responderId: String
    let status: String
    let estimatedArrival: String?
    let directions: NavigationDirections
}

enum ResponderRole: String, Codable, CaseIterable {
    case volunteer = "Volunteer"
    case emt = "EMT"
    case firefighter = "Firefighter"
    case police = "Police"
    case medical = "Medical Professional"
}

enum ResponderStatus: String, Codable, CaseIterable {
    case available = "Available"
    case onCall = "On Call"
    case offDuty = "Off Duty"
}

enum AvailabilityStatus: String, Codable, CaseIterable {
    case available = "Available"
    case unavailable = "Unavailable"
    case busy = "Busy"
}
