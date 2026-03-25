import Foundation

protocol VolunteerServiceProtocol {
    func enrollAsVolunteer(userId: String, role: ResponderRole, skills: [String], radiusMeters: Double, hasVehicle: Bool) async throws
    func getVolunteerProfile(userId: String) async throws -> VolunteerProfile
    func updateVolunteerAvailability(userId: String, isAvailable: Bool, radius: Double) async throws
    func updateVehicleStatus(userId: String, hasVehicle: Bool) async throws
    func getNearbyResponders(latitude: Double, longitude: Double, radiusMeters: Double) async throws -> [Responder]
    func addSkill(userId: String, skill: String) async throws
    func removeSkill(userId: String, skill: String) async throws

    /// Accept an incident response. Returns directions to the incident so the
    /// responder can launch turn-by-turn navigation.
    ///
    /// Example:
    ///   let ack = try await service.acceptResponse(userId: uid, requestId: rid)
    ///   if let url = URL(string: ack.directions.appleMapsUrl) {
    ///       await UIApplication.shared.open(url)
    ///   }
    func acceptResponse(userId: String, requestId: String) async throws -> AcknowledgmentResponse
    func declineResponse(userId: String, requestId: String) async throws

    // ── Responder Communication ────────────────────────────────────

    /// Send a message in an incident's responder channel.
    /// All messages are filtered through server-side guardrails before delivery.
    ///
    /// Example:
    ///   let result = try await service.sendMessage(
    ///       requestId: incidentId,
    ///       senderId: myUserId,
    ///       senderName: "Maria Santos",
    ///       content: "I can see the building, arriving in 2 min"
    ///   )
    ///   if result.verdict == .blocked {
    ///       showAlert("Message not sent: \(result.reason ?? "")")
    ///   }
    func sendMessage(requestId: String, senderId: String, senderName: String,
                     senderRole: String?, messageType: ResponderMessageType,
                     content: String, latitude: Double?, longitude: Double?,
                     quickResponseCode: String?) async throws -> SendMessageResult

    /// Get message history for an incident's responder channel.
    func getMessages(requestId: String, limit: Int, since: Date?) async throws -> [ResponderChatMessage]

    /// Get available quick responses.
    func getQuickResponses() async throws -> [QuickResponse]
}

@Observable
final class MockVolunteerService: VolunteerServiceProtocol {
    func enrollAsVolunteer(userId: String, role: ResponderRole, skills: [String], radiusMeters: Double, hasVehicle: Bool) async throws {
        try await Task.sleep(nanoseconds: 500_000_000)
    }

    func getVolunteerProfile(userId: String) async throws -> VolunteerProfile {
        try await Task.sleep(nanoseconds: 300_000_000)
        return VolunteerProfile(
            userId: userId,
            role: .volunteer,
            isEnrolled: true,
            skills: ["First Aid", "CPR", "Wilderness Rescue"],
            responsesCompleted: 23,
            averageResponseTime: 320,
            verificationBadge: true,
            instantResponseEnabled: true,
            responsibilityRadius: 2000,
            availabilitySchedule: "Weekends 9 AM - 6 PM, Weekdays after 5 PM",
            hasVehicle: true
        )
    }

    func updateVolunteerAvailability(userId: String, isAvailable: Bool, radius: Double) async throws {
        try await Task.sleep(nanoseconds: 300_000_000)
    }

    func updateVehicleStatus(userId: String, hasVehicle: Bool) async throws {
        try await Task.sleep(nanoseconds: 200_000_000)
    }

    func getNearbyResponders(latitude: Double, longitude: Double, radiusMeters: Double) async throws -> [Responder] {
        try await Task.sleep(nanoseconds: 400_000_000)
        return [
            Responder(
                id: "resp-001",
                name: "Maria Santos",
                role: .emt,
                latitude: latitude + 0.001,
                longitude: longitude + 0.001,
                distance: 150,
                skills: ["CPR", "First Aid", "Advanced Life Support"],
                isVerified: true,
                responseTime: 120,
                status: .available,
                availability: .available,
                hasVehicle: true
            ),
            Responder(
                id: "resp-002",
                name: "James Thompson",
                role: .firefighter,
                latitude: latitude - 0.002,
                longitude: longitude + 0.0015,
                distance: 280,
                skills: ["Rescue", "Fire Suppression", "CPR"],
                isVerified: true,
                responseTime: 180,
                status: .available,
                availability: .available,
                hasVehicle: true
            ),
            Responder(
                id: "resp-003",
                name: "Sarah Kim",
                role: .volunteer,
                latitude: latitude + 0.0015,
                longitude: longitude - 0.002,
                distance: 320,
                skills: ["First Aid", "Mental Health Support"],
                isVerified: true,
                responseTime: nil,
                status: .available,
                availability: .available,
                hasVehicle: false  // On foot — will only be dispatched within walking distance
            )
        ]
    }

    func addSkill(userId: String, skill: String) async throws {
        try await Task.sleep(nanoseconds: 200_000_000)
    }

    func removeSkill(userId: String, skill: String) async throws {
        try await Task.sleep(nanoseconds: 200_000_000)
    }

    func acceptResponse(userId: String, requestId: String) async throws -> AcknowledgmentResponse {
        try await Task.sleep(nanoseconds: 400_000_000)
        return AcknowledgmentResponse(
            ackId: UUID().uuidString,
            requestId: requestId,
            responderId: userId,
            status: "EnRoute",
            estimatedArrival: "00:05:00",
            directions: NavigationDirections(
                travelMode: "driving",
                distanceMeters: 1200,
                estimatedTravelTimeMinutes: 4.5,
                googleMapsUrl: "https://www.google.com/maps/dir/?api=1&origin=30.27,-97.74&destination=30.28,-97.73&travelmode=driving",
                appleMapsUrl: "https://maps.apple.com/?saddr=30.27,-97.74&daddr=30.28,-97.73&dirflg=d",
                wazeUrl: "https://waze.com/ul?ll=30.28,-97.73&navigate=yes"
            )
        )
    }

    func declineResponse(userId: String, requestId: String) async throws {
        try await Task.sleep(nanoseconds: 200_000_000)
    }

    // ── Responder Communication ────────────────────────────────────

    func sendMessage(requestId: String, senderId: String, senderName: String,
                     senderRole: String?, messageType: ResponderMessageType,
                     content: String, latitude: Double?, longitude: Double?,
                     quickResponseCode: String?) async throws -> SendMessageResult {
        try await Task.sleep(nanoseconds: 200_000_000)
        return SendMessageResult(
            messageId: UUID().uuidString,
            verdict: .approved,
            reason: nil,
            redactedContent: nil,
            piiDetected: false,
            piiTypes: nil,
            profanityDetected: false,
            threatDetected: false,
            rateLimited: false,
            messagesSentInWindow: 1,
            rateLimitMax: 30
        )
    }

    func getMessages(requestId: String, limit: Int = 100, since: Date? = nil) async throws -> [ResponderChatMessage] {
        try await Task.sleep(nanoseconds: 300_000_000)
        return [
            ResponderChatMessage(
                requestId: requestId,
                senderId: "resp-001",
                senderName: "Maria Santos",
                senderRole: "EMT",
                content: "I'm 2 minutes out, approaching from the east",
                verdict: .approved,
                sentAt: Date().addingTimeInterval(-120)
            ),
            ResponderChatMessage(
                requestId: requestId,
                senderId: "resp-002",
                senderName: "James Thompson",
                senderRole: "Firefighter",
                messageType: .quickResponse,
                content: "I'm on my way",
                quickResponseCode: "ON_MY_WAY",
                verdict: .approved,
                sentAt: Date().addingTimeInterval(-90)
            ),
            ResponderChatMessage(
                requestId: requestId,
                senderId: "resp-001",
                senderName: "Maria Santos",
                senderRole: "EMT",
                content: "Arrived on scene. Victim is conscious and alert.",
                verdict: .approved,
                sentAt: Date().addingTimeInterval(-30)
            )
        ]
    }

    func getQuickResponses() async throws -> [QuickResponse] {
        return [
            QuickResponse(code: "ON_MY_WAY", displayText: "I'm on my way", category: "Movement"),
            QuickResponse(code: "ARRIVED", displayText: "I've arrived on scene", category: "Movement"),
            QuickResponse(code: "NEED_MEDICAL", displayText: "Need medical assistance here", category: "Request"),
            QuickResponse(code: "NEED_BACKUP", displayText: "Need additional responders", category: "Request"),
            QuickResponse(code: "ALL_CLEAR", displayText: "All clear — situation resolved", category: "Status"),
            QuickResponse(code: "SCENE_SECURED", displayText: "Scene is secured", category: "Status"),
            QuickResponse(code: "VICTIM_CONSCIOUS", displayText: "Victim is conscious and alert", category: "Medical"),
            QuickResponse(code: "VICTIM_UNCONSCIOUS", displayText: "Victim is unconscious", category: "Medical"),
            QuickResponse(code: "CPR_IN_PROGRESS", displayText: "Performing CPR", category: "Medical"),
            QuickResponse(code: "HAZARD_PRESENT", displayText: "Hazard present — approach with caution", category: "Safety"),
            QuickResponse(code: "STANDOWN", displayText: "Standing down — enough responders", category: "Movement"),
            QuickResponse(code: "DELAYED", displayText: "I'm delayed — ETA update coming", category: "Movement"),
        ]
    }
}

struct VolunteerProfile: Codable, Hashable {
    let userId: String
    var role: ResponderRole
    var isEnrolled: Bool
    var skills: [String]
    var responsesCompleted: Int
    var averageResponseTime: TimeInterval? // in seconds
    var verificationBadge: Bool
    var instantResponseEnabled: Bool
    var responsibilityRadius: Double // in meters
    var availabilitySchedule: String
    var hasVehicle: Bool
}
