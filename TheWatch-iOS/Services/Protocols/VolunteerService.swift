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
