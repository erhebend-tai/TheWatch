// ============================================================================
// WRITE-AHEAD LOG
// ============================================================================
// File:         ApiVolunteerService.swift
// Purpose:      VolunteerServiceProtocol implementation backed by WatchApiClient.
//               Maps GET/PUT /api/response/participation and
//               POST /api/response/{id}/ack to the volunteer interface.
// Date:         2026-03-24
// Author:       Claude
// Dependencies: WatchApiClient
//
// Usage Example:
//   let service = ApiVolunteerService()
//   try await service.enrollAsVolunteer(userId: "uid", role: .emt,
//       skills: ["CPR"], radiusMeters: 5000, hasVehicle: true)
//   let ack = try await service.acceptResponse(userId: "uid", requestId: "req_001")
// ============================================================================

import Foundation

@Observable
final class ApiVolunteerService: VolunteerServiceProtocol {

    private let apiClient = WatchApiClient.shared

    func enrollAsVolunteer(
        userId: String,
        role: ResponderRole,
        skills: [String],
        radiusMeters: Double,
        hasVehicle: Bool
    ) async throws {
        let prefs = ParticipationPreferencesDto(
            userId: userId,
            isAvailable: true,
            optInScopes: ["CheckIn", "Emergency", "CommunityWatch"],
            certifications: skills,
            hasVehicle: hasVehicle,
            maxRadiusMeters: radiusMeters
        )
        _ = try await apiClient.updateParticipation(prefs)
    }

    func getVolunteerProfile(userId: String) async throws -> VolunteerProfile {
        let prefs = try await apiClient.getParticipation(userId: userId)
        return VolunteerProfile(
            userId: userId,
            role: .volunteer,
            isEnrolled: prefs.isAvailable ?? false,
            skills: prefs.certifications ?? [],
            responsesCompleted: 0,
            averageResponseTime: nil,
            verificationBadge: true,
            instantResponseEnabled: true,
            responsibilityRadius: prefs.maxRadiusMeters ?? 5000,
            availabilitySchedule: formatSchedule(prefs.weeklySchedule),
            hasVehicle: prefs.hasVehicle ?? false
        )
    }

    func updateVolunteerAvailability(userId: String, isAvailable: Bool, radius: Double) async throws {
        _ = try await apiClient.setAvailability(userId: userId, isAvailable: isAvailable)
        do {
            var existing = try await apiClient.getParticipation(userId: userId)
            existing.isAvailable = isAvailable
            existing.maxRadiusMeters = radius
            _ = try await apiClient.updateParticipation(existing)
        } catch {
            // If participation prefs don't exist, just set availability
        }
    }

    func updateVehicleStatus(userId: String, hasVehicle: Bool) async throws {
        var existing = try await apiClient.getParticipation(userId: userId)
        existing.hasVehicle = hasVehicle
        _ = try await apiClient.updateParticipation(existing)
    }

    func getNearbyResponders(
        latitude: Double,
        longitude: Double,
        radiusMeters: Double
    ) async throws -> [Responder] {
        // Nearby responders are fetched via active response situation data
        // when an alert is active. For the passive map view, return empty.
        return []
    }

    func addSkill(userId: String, skill: String) async throws {
        var existing = try await apiClient.getParticipation(userId: userId)
        var skills = existing.certifications ?? []
        if !skills.contains(skill) { skills.append(skill) }
        existing.certifications = skills
        _ = try await apiClient.updateParticipation(existing)
    }

    func removeSkill(userId: String, skill: String) async throws {
        var existing = try await apiClient.getParticipation(userId: userId)
        existing.certifications = (existing.certifications ?? []).filter { $0 != skill }
        _ = try await apiClient.updateParticipation(existing)
    }

    func acceptResponse(userId: String, requestId: String) async throws -> AcknowledgmentResponse {
        let result = try await apiClient.acknowledgeResponse(
            requestId: requestId,
            responderId: userId,
            responderRole: "VOLUNTEER",
            hasVehicle: true
        )

        let directionsDict = (result["Directions"]?.value as? [String: Any]) ?? [:]

        return AcknowledgmentResponse(
            ackId: (result["AckId"]?.value as? String) ?? UUID().uuidString,
            requestId: (result["RequestId"]?.value as? String) ?? requestId,
            responderId: (result["ResponderId"]?.value as? String) ?? userId,
            status: (result["Status"]?.value as? String) ?? "EnRoute",
            estimatedArrival: result["EstimatedArrival"]?.value as? String,
            directions: NavigationDirections(
                travelMode: (directionsDict["TravelMode"] as? String) ?? "driving",
                distanceMeters: (directionsDict["DistanceMeters"] as? Double) ?? 0,
                estimatedTravelTimeMinutes: directionsDict["EstimatedTravelTime"] as? Double,
                googleMapsUrl: (directionsDict["GoogleMapsUrl"] as? String) ?? "",
                appleMapsUrl: (directionsDict["AppleMapsUrl"] as? String) ?? "",
                wazeUrl: (directionsDict["WazeUrl"] as? String) ?? ""
            )
        )
    }

    func declineResponse(userId: String, requestId: String) async throws {
        _ = try await apiClient.sendResponderMessage(
            requestId: requestId,
            senderId: userId,
            content: "Responder declined",
            messageType: "StatusUpdate"
        )
    }

    // ── Responder Communication ────────────────────────────────────

    func sendMessage(
        requestId: String,
        senderId: String,
        senderName: String,
        senderRole: String?,
        messageType: ResponderMessageType,
        content: String,
        latitude: Double?,
        longitude: Double?,
        quickResponseCode: String?
    ) async throws -> SendMessageResult {
        let result = try await apiClient.sendResponderMessage(
            requestId: requestId,
            senderId: senderId,
            senderName: senderName,
            senderRole: senderRole,
            messageType: messageType.rawValue,
            content: content,
            latitude: latitude,
            longitude: longitude,
            quickResponseCode: quickResponseCode
        )

        return SendMessageResult(
            messageId: (result["MessageId"]?.value as? String) ?? UUID().uuidString,
            verdict: GuardrailsVerdict(rawValue: (result["Verdict"]?.value as? String) ?? "Approved") ?? .approved,
            reason: result["Reason"]?.value as? String,
            redactedContent: result["RedactedContent"]?.value as? String,
            piiDetected: (result["PiiDetected"]?.value as? Bool) ?? false,
            piiTypes: nil,
            profanityDetected: (result["ProfanityDetected"]?.value as? Bool) ?? false,
            threatDetected: (result["ThreatDetected"]?.value as? Bool) ?? false,
            rateLimited: (result["RateLimited"]?.value as? Bool) ?? false,
            messagesSentInWindow: (result["MessagesSentInWindow"]?.value as? Int) ?? 0,
            rateLimitMax: (result["RateLimitMax"]?.value as? Int) ?? 30
        )
    }

    func getMessages(requestId: String, limit: Int = 100, since: Date? = nil) async throws -> [ResponderChatMessage] {
        let results = try await apiClient.getResponderMessages(
            requestId: requestId,
            limit: limit,
            since: since
        )

        return results.map { msg in
            ResponderChatMessage(
                id: (msg["MessageId"]?.value as? String) ?? UUID().uuidString,
                requestId: (msg["RequestId"]?.value as? String) ?? requestId,
                senderId: (msg["SenderId"]?.value as? String) ?? "",
                senderName: (msg["SenderName"]?.value as? String) ?? "",
                senderRole: msg["SenderRole"]?.value as? String,
                messageType: ResponderMessageType(rawValue: (msg["MessageType"]?.value as? String) ?? "Text") ?? .text,
                content: (msg["Content"]?.value as? String) ?? "",
                verdict: GuardrailsVerdict(rawValue: (msg["Verdict"]?.value as? String) ?? "Approved") ?? .approved,
                sentAt: Date()
            )
        }
    }

    func getQuickResponses() async throws -> [QuickResponse] {
        let results = try await apiClient.getQuickResponses()
        return results.map { qr in
            QuickResponse(
                code: qr["Code"] ?? qr["code"] ?? "",
                displayText: qr["DisplayText"] ?? qr["displayText"] ?? "",
                category: qr["Category"] ?? qr["category"] ?? ""
            )
        }
    }

    // ── Helpers ──────────────────────────────────────────────────

    private func formatSchedule(_ schedule: [String: [String]]?) -> String {
        guard let schedule, !schedule.isEmpty else { return "Not configured" }
        return schedule.map { "\($0.key): \($0.value.joined(separator: ", "))" }.joined(separator: "; ")
    }
}
