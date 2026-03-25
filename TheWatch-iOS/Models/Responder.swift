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

// ═══════════════════════════════════════════════════════════════
// Responder Communication Models
// ═══════════════════════════════════════════════════════════════

/// Message types for incident-scoped responder communication.
enum ResponderMessageType: String, Codable, CaseIterable {
    case text = "Text"
    case locationShare = "LocationShare"
    case statusUpdate = "StatusUpdate"
    case image = "Image"
    case quickResponse = "QuickResponse"
}

/// Server-side guardrails verdict applied to each message.
/// All messages route through the server for safety filtering before delivery.
enum GuardrailsVerdict: String, Codable, CaseIterable {
    case approved = "Approved"       // Message delivered as-is
    case redacted = "Redacted"       // PII was removed, redacted version delivered
    case blocked = "Blocked"         // Message not delivered (profanity, threats)
    case rateLimited = "RateLimited" // Too many messages, try again later
}

/// A message in an incident's responder communication channel.
/// Every message passes through server guardrails before delivery.
///
/// Example usage:
///   let result = try await commService.sendMessage(
///       requestId: incidentId,
///       content: "Approaching from the north entrance"
///   )
///   switch result.verdict {
///   case .approved: break // delivered
///   case .blocked: showWarning(result.reason)
///   case .redacted: showInfo("Some info was redacted for privacy")
///   case .rateLimited: showInfo("Slow down")
///   }
struct ResponderChatMessage: Codable, Hashable, Identifiable {
    let id: String
    let requestId: String
    let senderId: String
    let senderName: String
    let senderRole: String?
    let messageType: ResponderMessageType
    let content: String
    let latitude: Double?
    let longitude: Double?
    let quickResponseCode: String?
    let verdict: GuardrailsVerdict
    let sentAt: Date

    init(
        id: String = UUID().uuidString,
        requestId: String = "",
        senderId: String = "",
        senderName: String = "",
        senderRole: String? = nil,
        messageType: ResponderMessageType = .text,
        content: String = "",
        latitude: Double? = nil,
        longitude: Double? = nil,
        quickResponseCode: String? = nil,
        verdict: GuardrailsVerdict = .approved,
        sentAt: Date = Date()
    ) {
        self.id = id
        self.requestId = requestId
        self.senderId = senderId
        self.senderName = senderName
        self.senderRole = senderRole
        self.messageType = messageType
        self.content = content
        self.latitude = latitude
        self.longitude = longitude
        self.quickResponseCode = quickResponseCode
        self.verdict = verdict
        self.sentAt = sentAt
    }
}

/// Result from sending a message, including the guardrails verdict.
struct SendMessageResult: Codable, Hashable {
    let messageId: String
    let verdict: GuardrailsVerdict
    let reason: String?
    let redactedContent: String?
    let piiDetected: Bool
    let piiTypes: [String]?
    let profanityDetected: Bool
    let threatDetected: Bool
    let rateLimited: Bool
    let messagesSentInWindow: Int
    let rateLimitMax: Int
}

/// A pre-defined quick response that responders can send with one tap.
struct QuickResponse: Codable, Hashable, Identifiable {
    var id: String { code }
    let code: String          // e.g., "ON_MY_WAY", "NEED_MEDICAL", "ALL_CLEAR"
    let displayText: String   // e.g., "I'm on my way"
    let category: String      // e.g., "Movement", "Request", "Status", "Medical"
}
