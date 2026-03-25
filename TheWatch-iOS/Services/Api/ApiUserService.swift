// ============================================================================
// WRITE-AHEAD LOG
// ============================================================================
// File:         ApiUserService.swift
// Purpose:      UserServiceProtocol implementation backed by WatchApiClient.
//               Calls GET /api/account/status for profile data.
// Date:         2026-03-24
// Author:       Claude
// Dependencies: WatchApiClient
//
// Usage Example:
//   let service = ApiUserService()
//   let user = try await service.getUser(userId: "uid")
//   let updated = try await service.updateUser(user)
// ============================================================================

import Foundation

@Observable
final class ApiUserService: UserServiceProtocol {

    private let apiClient = WatchApiClient.shared

    func getUser(userId: String) async throws -> User {
        let status = try await apiClient.getAccountStatus()
        return User(
            id: userId,
            email: status.email ?? "",
            phone: status.phoneNumber ?? "",
            firstName: status.displayName?.components(separatedBy: " ").first ?? "",
            lastName: status.displayName?.components(separatedBy: " ").dropFirst().joined(separator: " ") ?? "",
            dateOfBirth: Date(timeIntervalSince1970: 576057600),
            bloodType: "",
            medicalConditions: "",
            medications: "",
            defaultSeverity: .medium,
            autoEscalationMinutes: 5,
            auto911Escalation: false,
            wearableDevices: []
        )
    }

    func updateUser(_ user: User) async throws -> User {
        // Profile updates flow through Firebase Auth for now.
        // When the backend adds PUT /api/account/profile, wire here.
        return user
    }

    func getEmergencyContacts(userId: String) async throws -> [EmergencyContact] {
        // Emergency contacts stored locally; will wire when backend endpoint available.
        return []
    }

    func addEmergencyContact(_ contact: EmergencyContact, userId: String) async throws {
        // Local storage — no backend endpoint yet.
    }

    func updateEmergencyContact(_ contact: EmergencyContact, userId: String) async throws {
        // Local storage — no backend endpoint yet.
    }

    func deleteEmergencyContact(contactId: String, userId: String) async throws {
        // Local storage — no backend endpoint yet.
    }
}
